using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Security;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Billing
{
    public sealed record PaymentRecoveryActionResult
    {
        public required bool Succeeded { get; init; }
        public string? Message { get; init; }

        public static PaymentRecoveryActionResult Ok(string message) => new() { Succeeded = true, Message = message };
        public static PaymentRecoveryActionResult Fail(string message) => new() { Succeeded = false, Message = message };
    }

    /// <summary>Vista (solo lectura, sanitizada) de un incidente para la consola de plataforma.</summary>
    public sealed record PaymentRecoveryConsoleItem
    {
        public required Guid IncidentId { get; init; }
        public required Guid TenantId { get; init; }

        /// <summary>Ámbito del incidente: plan base o add-on de WhatsApp. Determina qué plan usa la update URL.</summary>
        public PaymentIncidentScope Scope { get; init; }

        /// <summary>Plan recurrente de TiloPay del incidente (base o add-on): el "id" para recurrentUrl.</summary>
        public int? TilopayRecurringPlanId { get; init; }

        public string? TenantName { get; init; }
        public string? ClienteEmail { get; init; }
        public string? PlanCode { get; init; }

        /// <summary>id_suscriptor enmascarado (nunca completo).</summary>
        public string? ProviderSubscriberSuffix { get; init; }

        public PaymentIncidentStatus Status { get; init; }
        public int FailureCount { get; init; }
        public DateTime FailureDetectedAtUtc { get; init; }
        public DateTime? GraceEndsAtUtc { get; init; }
        public int NotificationCount { get; init; }
        public DateTime? LastNotificationAtUtc { get; init; }
        public DateTime? LastReminderAtUtc { get; init; }
        public string? ProviderResultCode { get; init; }
        public string? ProviderResultMessage { get; init; }

        /// <summary>true si el acceso quedó suspendido por impago (distinto de "gracia vencida sin suspensión").</summary>
        public bool SuspendedForNonPayment { get; init; }
    }

    public interface IPaymentRecoveryService
    {
        /// <summary>
        /// Registra un pago recurrente fallido: abre/actualiza el incidente y el período de gracia.
        /// Idempotente (un incidente Open por tenant/plan; incrementa FailureCount). No accionable si
        /// la renovación ya está cancelada+dada de baja, si el fallo es de un plan viejo, o si ya hubo
        /// un pago confirmado más reciente (success-gana). Best-effort, local, sin HTTP.
        /// </summary>
        Task RegisterFailedPaymentAsync(
            Guid tenantId,
            int? failedRecurringPlanId,
            string? providerSubscriberId,
            string? resultCode,
            string? resultMessage,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resuelve el incidente abierto tras un pago exitoso del plan actual y limpia el estado de
        /// recuperación. Un éxito de OTRO plan no toca el incidente actual. Idempotente.
        /// </summary>
        Task ResolveOnSuccessAsync(
            Guid tenantId,
            int? paidRecurringPlanId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Igual que <see cref="RegisterFailedPaymentAsync"/> pero para el ADD-ON de WhatsApp: abre/
        /// actualiza un incidente con Scope=WhatsAppAddon ligado a la fila del add-on. NUNCA toca el
        /// plan base ni sus incidentes. La gracia/estado del add-on ya los maneja SuscripcionService;
        /// acá solo se agrega el incidente (historial + visibilidad en Mission Control). Sin emails.
        /// </summary>
        Task RegisterFailedAddonPaymentAsync(
            Guid tenantId,
            int? failedRecurringPlanId,
            string? providerSubscriberId,
            string? resultCode,
            string? resultMessage,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resuelve el incidente ABIERTO del add-on tras un pago exitoso del add-on actual. Un éxito de
        /// otro paquete no toca el incidente actual. Idempotente. NUNCA toca el plan base.
        /// </summary>
        Task ResolveAddonOnSuccessAsync(
            Guid tenantId,
            int? paidRecurringPlanId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pase LOCAL (sin HTTP) que cierra los incidentes cuya gracia venció: los marca GraceExpired
        /// y, SOLO si <c>AutoSuspendAfterGrace=true</c>, suspende el acceso; si no, deja rastro dry-run
        /// sin cortar. Nunca suspende antes de la fecha efectiva ni con renovación cancelada vigente.
        /// Idempotente. Devuelve cuántos incidentes procesó.
        /// </summary>
        Task<int> RunGraceExpirationPassAsync(CancellationToken cancellationToken = default);

        /// <summary>Lista (cross-tenant, solo lectura, sanitizada) los incidentes vivos para la consola de plataforma.</summary>
        Task<IReadOnlyList<PaymentRecoveryConsoleItem>> ListConsoleIncidentsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// SuperAdmin cierra manualmente un incidente (Resolved) y limpia el estado de recuperación de
        /// la suscripción. NO cambia el acceso (Estado): reactivar un acceso suspendido es una acción de
        /// ciclo de vida aparte. Idempotente. Local, sin HTTP.
        /// </summary>
        Task<PaymentRecoveryActionResult> ResolveManuallyAsync(Guid incidentId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default);

        /// <summary>
        /// SuperAdmin marca un incidente como Ignorado (no accionable) con motivo y limpia el banner de
        /// recuperación de la suscripción. NO cambia el acceso. Idempotente. Local, sin HTTP.
        /// </summary>
        Task<PaymentRecoveryActionResult> IgnoreAsync(Guid incidentId, string actorUserId, string actorEmail, string? reason, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Gestiona los incidentes de recuperación de pago (<see cref="SubscriptionPaymentIncident"/>).
    /// Se engancha en el webhook (fallo/éxito) de forma best-effort y POST-commit: abre su propio
    /// <c>BeginScope(tenantId)</c> para que el INSERT del incidente (ITenantEntity) pase el RLS, y
    /// guarda en una transacción corta aparte. La gracia se apoya en <c>FechaFinGraciaUtc</c> +
    /// estado Morosa que YA maneja SuscripcionService; acá se agrega solo el tracking/incidente.
    /// </summary>
    public sealed class PaymentRecoveryService : IPaymentRecoveryService
    {
        private readonly ApplicationDbContext _db;
        private readonly ITenantExecutionContextAccessor _tenantExecutionContextAccessor;
        private readonly IBusinessDateTimeProvider _clock;
        private readonly BillingPaymentRecoveryOptions _options;
        private readonly ITenantCommercialAccessCache? _accessCache;
        private readonly ILogger<PaymentRecoveryService> _logger;

        public PaymentRecoveryService(
            ApplicationDbContext db,
            ITenantExecutionContextAccessor tenantExecutionContextAccessor,
            IBusinessDateTimeProvider clock,
            IOptions<BillingPaymentRecoveryOptions> options,
            ILogger<PaymentRecoveryService> logger,
            ITenantCommercialAccessCache? accessCache = null)
        {
            _db = db;
            _tenantExecutionContextAccessor = tenantExecutionContextAccessor;
            _clock = clock;
            _options = options.Value;
            _accessCache = accessCache;
            _logger = logger;
        }

        public async Task RegisterFailedPaymentAsync(
            Guid tenantId,
            int? failedRecurringPlanId,
            string? providerSubscriberId,
            string? resultCode,
            string? resultMessage,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || tenantId == Guid.Empty)
            {
                return;
            }

            using var scope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            var nowUtc = GetUtcNow();

            var subscription = await LoadSubscriptionAsync(tenantId, cancellationToken);
            if (subscription is null)
            {
                return;
            }

            // El fallo es de un plan VIEJO (ya cambiado/cancelado): no marca el plan actual.
            if (failedRecurringPlanId is { } failedPlan &&
                subscription.TilopayRecurringPlanId is { } currentPlan &&
                failedPlan != currentPlan)
            {
                _logger.LogInformation(
                    "Pago fallido de un plan distinto al actual: no se abre incidente. TenantId {TenantId}. FailedPlan {FailedPlan}. CurrentPlan {CurrentPlan}.",
                    tenantId, failedPlan, currentPlan);
                return;
            }

            // Renovación cancelada y suscriptor ya dado de baja en el proveedor: el fallo no es accionable.
            if (subscription.CancelAtPeriodEnd &&
                ProviderSubscriberStatusRules.IsProviderSubscriberInactive(subscription.ProviderStatusRaw))
            {
                _logger.LogInformation(
                    "Pago fallido no accionable (renovación cancelada + suscriptor inactivo). TenantId {TenantId}.",
                    tenantId);
                return;
            }

            var planId = subscription.TilopayRecurringPlanId;

            // Success-gana: si el pago confirmado más reciente es posterior al fallido más reciente,
            // el éxito ya ganó (webhooks desordenados): NO se abre/reabre incidente.
            if (await ConfirmedPaymentWinsAsync(tenantId, planId, cancellationToken))
            {
                _logger.LogInformation(
                    "Pago fallido ignorado: hay un pago confirmado más reciente (success-gana). TenantId {TenantId}.",
                    tenantId);
                return;
            }

            var existing = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .Where(i =>
                    i.TenantId == tenantId &&
                    i.Scope == PaymentIncidentScope.BasePlan &&
                    i.Status == PaymentIncidentStatus.Open &&
                    i.TilopayRecurringPlanId == planId)
                .OrderByDescending(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var graceDays = Math.Clamp(_options.GraceDays, 1, 60);

            if (existing is not null)
            {
                // Reintento del mismo ciclo: NO se resetea la gracia; solo se incrementa el conteo.
                existing.FailureCount += 1;
                existing.ProviderResultCode = Trim(resultCode, 40) ?? existing.ProviderResultCode;
                existing.ProviderResultMessage = Trim(resultMessage, 300) ?? existing.ProviderResultMessage;
                existing.ProviderSubscriptionId = providerSubscriberId ?? existing.ProviderSubscriptionId;
                existing.UpdatedAtUtc = nowUtc;
                subscription.LastPaymentFailedAtUtc = nowUtc;
                subscription.FechaUltimaActualizacionUtc = nowUtc;

                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Pago fallido recurrente adicional. TenantId {TenantId}. FailureCount {FailureCount}.",
                    tenantId, existing.FailureCount);
                return;
            }

            var graceEndsAtUtc = nowUtc.AddDays(graceDays);
            var clienteEmail = await ResolveTenantEmailAsync(tenantId, cancellationToken);

            var incident = new SubscriptionPaymentIncident
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SuscripcionId = subscription.Id,
                PlanCode = subscription.CodigoPlan ?? subscription.Plan?.Codigo,
                TilopayRecurringPlanId = planId,
                ProviderSubscriptionId = providerSubscriberId ?? subscription.ProviderSubscriptionId,
                ClienteEmail = Trim(clienteEmail, 320),
                Status = PaymentIncidentStatus.Open,
                FailureDetectedAtUtc = nowUtc,
                GraceEndsAtUtc = graceEndsAtUtc,
                ProviderEventKey = BuildEventKey(tenantId, planId, resultCode, nowUtc),
                ProviderResultCode = Trim(resultCode, 40),
                ProviderResultMessage = Trim(resultMessage, 300),
                FailureCount = 1,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.SubscriptionPaymentIncidents.Add(incident);

            // Resumen en la suscripción + alinear la gracia con GraceDays de recuperación.
            subscription.LastPaymentFailedAtUtc = nowUtc;
            subscription.PaymentRecoveryStatus = "GraceActive";
            subscription.FechaFinGraciaUtc = graceEndsAtUtc;
            subscription.FechaUltimaActualizacionUtc = nowUtc;

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.SubscriptionPaymentFailedGraceStarted,
                tenantId,
                incident.Id.ToString(),
                $"Pago recurrente fallido: incidente abierto. Gracia hasta {graceEndsAtUtc:yyyy-MM-dd HH:mm} UTC. " +
                $"Plan {incident.PlanCode}. SuscriptorSuffix {SensitiveDataMasker.MaskReference(incident.ProviderSubscriptionId)}. Code {Trim(resultCode, 40)}.",
                nowUtc));

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Incidente de pago recurrente abierto. TenantId {TenantId}. IncidentId {IncidentId}. GraceEnds {GraceEnds}.",
                tenantId, incident.Id, graceEndsAtUtc);
        }

        public async Task ResolveOnSuccessAsync(
            Guid tenantId,
            int? paidRecurringPlanId,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || tenantId == Guid.Empty)
            {
                return;
            }

            using var scope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            var nowUtc = GetUtcNow();

            var subscription = await LoadSubscriptionAsync(tenantId, cancellationToken);
            if (subscription is null)
            {
                return;
            }

            // Un éxito de OTRO plan no resuelve el incidente del plan actual.
            if (paidRecurringPlanId is { } paidPlan &&
                subscription.TilopayRecurringPlanId is { } currentPlan &&
                paidPlan != currentPlan)
            {
                return;
            }

            var open = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .Where(i =>
                    i.TenantId == tenantId &&
                    i.Scope == PaymentIncidentScope.BasePlan &&
                    i.Status == PaymentIncidentStatus.Open)
                .OrderByDescending(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var clearedRecoveryFields = subscription.LastPaymentFailedAtUtc is not null ||
                                        subscription.PaymentRecoveryStatus is not null;

            if (open is null && !clearedRecoveryFields)
            {
                return; // Nada que resolver.
            }

            if (open is not null)
            {
                open.Status = PaymentIncidentStatus.Resolved;
                open.ResolvedAtUtc = nowUtc;
                open.UpdatedAtUtc = nowUtc;
            }

            subscription.LastPaymentFailedAtUtc = null;
            subscription.PaymentRecoveryStatus = null;
            subscription.FechaUltimaActualizacionUtc = nowUtc;

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.SubscriptionPaymentRecoveryResolved,
                tenantId,
                (open?.Id ?? subscription.Id).ToString(),
                "Pago recurrente confirmado: incidente de recuperación resuelto y gracia limpiada.",
                nowUtc));

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Incidente de recuperación resuelto por pago confirmado. TenantId {TenantId}. IncidentId {IncidentId}.",
                tenantId, open?.Id);
        }

        public async Task RegisterFailedAddonPaymentAsync(
            Guid tenantId,
            int? failedRecurringPlanId,
            string? providerSubscriberId,
            string? resultCode,
            string? resultMessage,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || tenantId == Guid.Empty)
            {
                return;
            }

            using var scope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            var nowUtc = GetUtcNow();

            var addon = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (addon is null)
            {
                return;
            }

            // Fallo de un add-on VIEJO (ya cambiado): no abre incidente del actual.
            if (failedRecurringPlanId is { } failedPlan &&
                addon.TilopayRecurringPlanId is { } currentPlan &&
                failedPlan != currentPlan)
            {
                return;
            }

            var planId = addon.TilopayRecurringPlanId;

            var existing = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .Where(i =>
                    i.TenantId == tenantId &&
                    i.Status == PaymentIncidentStatus.Open &&
                    i.Scope == PaymentIncidentScope.WhatsAppAddon &&
                    i.AddonId == addon.Id)
                .OrderByDescending(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is not null)
            {
                existing.FailureCount += 1;
                existing.ProviderResultCode = Trim(resultCode, 40) ?? existing.ProviderResultCode;
                existing.ProviderResultMessage = Trim(resultMessage, 300) ?? existing.ProviderResultMessage;
                existing.ProviderSubscriptionId = providerSubscriberId ?? existing.ProviderSubscriptionId;
                existing.UpdatedAtUtc = nowUtc;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var clienteEmail = await ResolveTenantEmailAsync(tenantId, cancellationToken);
            var graceEndsAtUtc = addon.FechaFinGraciaUtc ?? nowUtc.AddDays(Math.Clamp(_options.GraceDays, 1, 60));

            var incident = new SubscriptionPaymentIncident
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Scope = PaymentIncidentScope.WhatsAppAddon,
                AddonId = addon.Id,
                SuscripcionId = Guid.Empty, // no aplica al add-on (columna escalar, sin FK)
                PlanCode = addon.AddonCode,
                TilopayRecurringPlanId = planId,
                ProviderSubscriptionId = providerSubscriberId ?? addon.ProviderSubscriptionId,
                ClienteEmail = Trim(clienteEmail, 320),
                Status = PaymentIncidentStatus.Open,
                FailureDetectedAtUtc = nowUtc,
                GraceEndsAtUtc = graceEndsAtUtc,
                ProviderEventKey = BuildEventKey(tenantId, planId, resultCode, nowUtc),
                ProviderResultCode = Trim(resultCode, 40),
                ProviderResultMessage = Trim(resultMessage, 300),
                FailureCount = 1,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            _db.SubscriptionPaymentIncidents.Add(incident);

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.AddonPaymentFailedGraceStarted,
                tenantId,
                incident.Id.ToString(),
                $"Pago recurrente del ADD-ON fallido: incidente abierto (no afecta el plan base). " +
                $"Add-on {addon.AddonCode}. Gracia hasta {graceEndsAtUtc:yyyy-MM-dd HH:mm} UTC. Code {Trim(resultCode, 40)}.",
                nowUtc));

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Incidente de pago del add-on abierto. TenantId {TenantId}. IncidentId {IncidentId}. AddonId {AddonId}.",
                tenantId, incident.Id, addon.Id);
        }

        public async Task ResolveAddonOnSuccessAsync(
            Guid tenantId,
            int? paidRecurringPlanId,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || tenantId == Guid.Empty)
            {
                return;
            }

            using var scope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            var nowUtc = GetUtcNow();

            var addon = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (addon is null)
            {
                return;
            }

            // Un éxito de OTRO paquete no resuelve el incidente del add-on actual.
            if (paidRecurringPlanId is { } paidPlan &&
                addon.TilopayRecurringPlanId is { } currentPlan &&
                paidPlan != currentPlan)
            {
                return;
            }

            var open = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .Where(i =>
                    i.TenantId == tenantId &&
                    i.Status == PaymentIncidentStatus.Open &&
                    i.Scope == PaymentIncidentScope.WhatsAppAddon &&
                    i.AddonId == addon.Id)
                .OrderByDescending(i => i.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (open is null)
            {
                return;
            }

            open.Status = PaymentIncidentStatus.Resolved;
            open.ResolvedAtUtc = nowUtc;
            open.UpdatedAtUtc = nowUtc;

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.AddonPaymentRecoveryResolved,
                tenantId,
                open.Id.ToString(),
                "Pago del add-on confirmado: incidente de recuperación del add-on resuelto.",
                nowUtc));

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Incidente de recuperación del add-on resuelto por pago confirmado. TenantId {TenantId}. IncidentId {IncidentId}.",
                tenantId, open.Id);
        }

        public async Task<int> RunGraceExpirationPassAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                return 0;
            }

            var nowUtc = GetUtcNow();
            // SOLO incidentes de PLAN BASE: la expiración de gracia suspende (con AutoSuspend) la
            // suscripción base. Los incidentes de add-on NO pasan por acá — el corte del add-on lo hace
            // su propio estado efectivo al vencer su gracia, sin tocar el plan base.
            var candidates = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i =>
                    i.Scope == PaymentIncidentScope.BasePlan &&
                    i.Status == PaymentIncidentStatus.Open &&
                    i.GraceEndsAtUtc != null &&
                    i.GraceEndsAtUtc <= nowUtc)
                .Select(i => new { i.Id, i.TenantId })
                .ToListAsync(cancellationToken);

            var openChecked = candidates.Count;
            int graceExpiredMarked = 0, suspended = 0, dryRuns = 0, ignored = 0, processed = 0;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.TenantId == Guid.Empty)
                {
                    continue;
                }

                try
                {
                    _db.ChangeTracker.Clear();
                    switch (await ExpireOneGraceAsync(candidate.Id, candidate.TenantId, nowUtc, cancellationToken))
                    {
                        case GraceExpirationOutcome.Ignored:
                            ignored++; processed++; break;
                        case GraceExpirationOutcome.DryRunMarked:
                            dryRuns++; graceExpiredMarked++; processed++; break;
                        case GraceExpirationOutcome.Suspended:
                            suspended++; graceExpiredMarked++; processed++; break;
                        case GraceExpirationOutcome.Skipped:
                        default:
                            break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _db.ChangeTracker.Clear();
                    _logger.LogError(ex, "No se pudo procesar la expiración de gracia del incidente {IncidentId}; se continúa.", candidate.Id);
                }
            }

            if (openChecked > 0)
            {
                _logger.LogInformation(
                    "Pase de expiración de gracia. OpenChecked {OpenChecked}. GraceExpiredMarked {GraceExpiredMarked}. Suspended {Suspended}. DryRuns {DryRuns}. Ignored {Ignored}. AutoSuspend {AutoSuspend}.",
                    openChecked, graceExpiredMarked, suspended, dryRuns, ignored, _options.AutoSuspendAfterGrace);
            }

            return processed;
        }

        /// <summary>Qué le pasó a un incidente en el pase de expiración de gracia.</summary>
        private enum GraceExpirationOutcome
        {
            /// <summary>No aplicaba (ya no Open, o la gracia aún no venció entre la lectura y el tracked).</summary>
            Skipped,

            /// <summary>Renovación cancelada: fallo no accionable, incidente marcado Ignored.</summary>
            Ignored,

            /// <summary>Marcado GraceExpired conservando el acceso (AutoSuspend=false, o período pagado aún vigente).</summary>
            DryRunMarked,

            /// <summary>Marcado GraceExpired y acceso suspendido por impago (AutoSuspend=true y período pagado vencido).</summary>
            Suspended
        }

        public async Task<IReadOnlyList<PaymentRecoveryConsoleItem>> ListConsoleIncidentsAsync(CancellationToken cancellationToken = default)
        {
            // Incidentes vivos que le importan a soporte: en curso, con gracia vencida o en revisión.
            var incidents = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i =>
                    i.Status == PaymentIncidentStatus.Open ||
                    i.Status == PaymentIncidentStatus.GraceExpired ||
                    i.Status == PaymentIncidentStatus.ManualReview)
                .OrderBy(i => i.GraceEndsAtUtc)
                .ThenByDescending(i => i.FailureDetectedAtUtc)
                .Take(200)
                .Select(i => new
                {
                    i.Id,
                    i.TenantId,
                    i.Scope,
                    i.TilopayRecurringPlanId,
                    i.ClienteEmail,
                    i.PlanCode,
                    i.ProviderSubscriptionId,
                    i.Status,
                    i.FailureCount,
                    i.FailureDetectedAtUtc,
                    i.GraceEndsAtUtc,
                    i.NotificationCount,
                    i.LastNotificationAtUtc,
                    i.LastReminderAtUtc,
                    i.ProviderResultCode,
                    i.ProviderResultMessage
                })
                .ToListAsync(cancellationToken);

            if (incidents.Count == 0)
            {
                return Array.Empty<PaymentRecoveryConsoleItem>();
            }

            var tenantIds = incidents.Select(i => i.TenantId).Distinct().ToList();
            var tenantNames = await _db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Nombre, cancellationToken);
            var suspendedTenants = (await _db.Suscripciones
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => tenantIds.Contains(s.TenantId) && s.PaymentRecoveryStatus == "Suspended")
                    .Select(s => s.TenantId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            return incidents.Select(i => new PaymentRecoveryConsoleItem
            {
                IncidentId = i.Id,
                TenantId = i.TenantId,
                Scope = i.Scope,
                TilopayRecurringPlanId = i.TilopayRecurringPlanId,
                TenantName = tenantNames.TryGetValue(i.TenantId, out var name) ? name : null,
                ClienteEmail = i.ClienteEmail,
                PlanCode = i.PlanCode,
                ProviderSubscriberSuffix = SensitiveDataMasker.MaskReference(i.ProviderSubscriptionId),
                Status = i.Status,
                FailureCount = i.FailureCount,
                FailureDetectedAtUtc = i.FailureDetectedAtUtc,
                GraceEndsAtUtc = i.GraceEndsAtUtc,
                NotificationCount = i.NotificationCount,
                LastNotificationAtUtc = i.LastNotificationAtUtc,
                LastReminderAtUtc = i.LastReminderAtUtc,
                ProviderResultCode = i.ProviderResultCode,
                ProviderResultMessage = i.ProviderResultMessage,
                SuspendedForNonPayment = suspendedTenants.Contains(i.TenantId)
            }).ToList();
        }

        public Task<PaymentRecoveryActionResult> ResolveManuallyAsync(Guid incidentId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default) =>
            CloseIncidentAsync(
                incidentId, actorUserId, actorEmail,
                targetStatus: PaymentIncidentStatus.Resolved,
                auditAction: PlatformAuditActions.PaymentRecoveryManuallyResolved,
                reason: null,
                successMessage: "Incidente cerrado manualmente. El estado de recuperación de la suscripción quedó limpio.",
                cancellationToken);

        public Task<PaymentRecoveryActionResult> IgnoreAsync(Guid incidentId, string actorUserId, string actorEmail, string? reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Task.FromResult(PaymentRecoveryActionResult.Fail("Indicá un motivo para ignorar el incidente."));
            }

            return CloseIncidentAsync(
                incidentId, actorUserId, actorEmail,
                targetStatus: PaymentIncidentStatus.Ignored,
                auditAction: PlatformAuditActions.PaymentRecoveryIgnored,
                reason: reason,
                successMessage: "Incidente marcado como ignorado.",
                cancellationToken);
        }

        /// <summary>
        /// Cierre manual de un incidente (Resolved/Ignored) por SuperAdmin. Resuelve el tenant a partir
        /// del incidente y escribe bajo BeginScope(tenantId) para el RLS. NO cambia el Estado/acceso de
        /// la suscripción (reactivar un suspendido es una acción de ciclo de vida aparte). Idempotente.
        /// </summary>
        private async Task<PaymentRecoveryActionResult> CloseIncidentAsync(
            Guid incidentId,
            string actorUserId,
            string actorEmail,
            PaymentIncidentStatus targetStatus,
            string auditAction,
            string? reason,
            string successMessage,
            CancellationToken cancellationToken)
        {
            if (incidentId == Guid.Empty)
            {
                return PaymentRecoveryActionResult.Fail("Incidente no especificado.");
            }

            var tenantId = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i => i.Id == incidentId)
                .Select(i => (Guid?)i.TenantId)
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantId is not { } owner || owner == Guid.Empty)
            {
                return PaymentRecoveryActionResult.Fail("No se encontró el incidente.");
            }

            using var scope = _tenantExecutionContextAccessor.BeginScope(owner);
            var nowUtc = GetUtcNow();

            var incident = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == incidentId && i.TenantId == owner, cancellationToken);
            if (incident is null)
            {
                return PaymentRecoveryActionResult.Fail("No se encontró el incidente.");
            }

            if (incident.Status == targetStatus)
            {
                return PaymentRecoveryActionResult.Ok(successMessage); // idempotente
            }

            incident.Status = targetStatus;
            incident.UpdatedAtUtc = nowUtc;
            if (targetStatus == PaymentIncidentStatus.Resolved)
            {
                incident.ResolvedAtUtc = nowUtc;
            }

            // Solo se limpia el estado de recuperación de la suscripción si NO quedan otros incidentes
            // vivos (otro incidente abierto/vencido/revisión sigue siendo válido y no debe borrarse).
            var hasOtherLiveIncidents = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .AnyAsync(i =>
                    i.TenantId == owner &&
                    i.Id != incidentId &&
                    (i.Status == PaymentIncidentStatus.Open ||
                     i.Status == PaymentIncidentStatus.GraceExpired ||
                     i.Status == PaymentIncidentStatus.ManualReview),
                    cancellationToken);

            var subscription = await LoadSubscriptionAsync(owner, cancellationToken);
            if (subscription is not null)
            {
                subscription.FechaUltimaActualizacionUtc = nowUtc;

                if (!hasOtherLiveIncidents)
                {
                    // Una suspensión REAL (acceso cortado) no se limpia ni se reactiva desde acá:
                    // reactivar un suspendido es una acción de ciclo de vida explícita.
                    var reallySuspended =
                        subscription.Estado == EstadoSuscripcion.Suspendida ||
                        string.Equals(subscription.PaymentRecoveryStatus, "Suspended", StringComparison.Ordinal);

                    subscription.LastPaymentFailedAtUtc = null;
                    subscription.LastPaymentRecoveryNotificationAtUtc = null;

                    if (!reallySuspended)
                    {
                        subscription.PaymentRecoveryStatus = null;
                        subscription.FechaFinGraciaUtc = null;

                        // Si la morosidad la causó recovery y la suscripción sigue vigente (sin otros
                        // bloqueos), al RESOLVER se vuelve a Activa para que la UI quede consistente.
                        if (targetStatus == PaymentIncidentStatus.Resolved &&
                            subscription.Estado == EstadoSuscripcion.Morosa &&
                            !subscription.CancelAtPeriodEnd &&
                            subscription.ProviderPausedAtUtc is null &&
                            SubscriptionEffectiveDates.GetEffectiveEndUtc(subscription.FechaFin, subscription.ProviderExpiresAtUtc) is { } end &&
                            end > nowUtc)
                        {
                            subscription.Estado = EstadoSuscripcion.Activa;
                            subscription.MotivoEstado = "Incidente de pago resuelto manualmente por soporte.";
                            _accessCache?.Invalidate(owner);
                        }
                    }
                }
            }

            var detail = reason is null
                ? $"Incidente {targetStatus} manualmente por soporte."
                : $"Incidente marcado {targetStatus}. Motivo: {Trim(reason, 250)}";
            _db.PlatformAuditLogs.Add(BuildAudit(auditAction, owner, incidentId.ToString(), detail, nowUtc));

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Incidente de recuperación {IncidentId} cerrado manualmente ({Status}) por {Actor}.",
                incidentId, targetStatus, actorEmail);
            return PaymentRecoveryActionResult.Ok(successMessage);
        }

        private async Task<GraceExpirationOutcome> ExpireOneGraceAsync(Guid incidentId, Guid tenantId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            using var scope = _tenantExecutionContextAccessor.BeginScope(tenantId);

            var incident = await _db.SubscriptionPaymentIncidents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == incidentId && i.TenantId == tenantId, cancellationToken);

            // Re-verificación bajo tracked: pudo resolverse entre la lectura y ahora.
            if (incident is null ||
                incident.Status != PaymentIncidentStatus.Open ||
                incident.GraceEndsAtUtc is null ||
                incident.GraceEndsAtUtc > nowUtc)
            {
                return GraceExpirationOutcome.Skipped;
            }

            var subscription = await LoadSubscriptionAsync(tenantId, cancellationToken);

            // Renovación cancelada: el fallo no es accionable (la cancelación manda). Se marca Ignored.
            if (subscription?.CancelAtPeriodEnd == true)
            {
                incident.Status = PaymentIncidentStatus.Ignored;
                incident.UpdatedAtUtc = nowUtc;
                await _db.SaveChangesAsync(cancellationToken);
                return GraceExpirationOutcome.Ignored;
            }

            // Marcar GraceExpired SIEMPRE que la gracia haya vencido: es un ESTADO (no corta acceso por
            // sí solo). El corte de acceso se decide aparte y NUNCA quita un período ya pagado.
            incident.Status = PaymentIncidentStatus.GraceExpired;
            incident.UpdatedAtUtc = nowUtc;

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.SubscriptionPaymentGraceExpired,
                tenantId, incident.Id.ToString(),
                $"Gracia de pago vencida ({incident.GraceEndsAtUtc:yyyy-MM-dd HH:mm} UTC). AutoSuspend {_options.AutoSuspendAfterGrace}.",
                nowUtc));

            // Solo se suspende con AutoSuspend=true Y cuando el período pagado (local/proveedor) ya
            // venció: nunca se corta acceso ya pagado.
            var effectiveEndUtc = subscription is null
                ? null
                : SubscriptionEffectiveDates.GetEffectiveEndUtc(subscription.FechaFin, subscription.ProviderExpiresAtUtc);
            var paidPeriodActive = effectiveEndUtc is { } end && end > nowUtc;

            if (_options.AutoSuspendAfterGrace && !paidPeriodActive)
            {
                if (subscription is not null &&
                    subscription.Estado is EstadoSuscripcion.Activa or EstadoSuscripcion.Morosa)
                {
                    subscription.Estado = EstadoSuscripcion.Suspendida;
                    subscription.PaymentRecoveryStatus = "Suspended";
                    subscription.MotivoEstado = "Suspendida por impago tras vencer el período de gracia.";
                    subscription.FechaUltimaActualizacionUtc = nowUtc;
                    _accessCache?.Invalidate(tenantId);
                }

                _db.PlatformAuditLogs.Add(BuildAudit(
                    PlatformAuditActions.SubscriptionSuspendedForNonPayment,
                    tenantId, incident.Id.ToString(),
                    "Acceso suspendido por impago (AutoSuspendAfterGrace=true).",
                    nowUtc));

                await _db.SaveChangesAsync(cancellationToken);
                return GraceExpirationOutcome.Suspended;
            }

            // Dry-run: se conserva el acceso (AutoSuspend=false, o período pagado todavía vigente).
            if (subscription is not null)
            {
                subscription.PaymentRecoveryStatus = "GraceExpired";
                subscription.FechaUltimaActualizacionUtc = nowUtc;
            }

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.SubscriptionPaymentGraceExpiredDryRun,
                tenantId, incident.Id.ToString(),
                paidPeriodActive
                    ? "Gracia vencida; acceso conservado porque el período pagado sigue vigente."
                    : "Gracia vencida SIN suspensión (AutoSuspendAfterGrace=false): solo alerta, acceso conservado.",
                nowUtc));

            await _db.SaveChangesAsync(cancellationToken);
            return GraceExpirationOutcome.DryRunMarked;
        }

        // ── Internos ─────────────────────────────────────────────────────────────

        private Task<Suscripcion?> LoadSubscriptionAsync(Guid tenantId, CancellationToken cancellationToken) =>
            _db.Suscripciones
                .IgnoreQueryFilters()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .FirstOrDefaultAsync(cancellationToken);

        private async Task<bool> ConfirmedPaymentWinsAsync(Guid tenantId, int? planId, CancellationToken cancellationToken)
        {
            var latestConfirmed = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .Where(p =>
                    p.TenantId == tenantId &&
                    p.Proveedor == PaymentProviderType.Tilopay &&
                    p.Estado == EstadoPagoProveedor.Confirmado &&
                    (planId == null || p.TilopayRecurringPlanId == planId))
                .MaxAsync(p => (DateTime?)p.FechaConfirmacionUtc, cancellationToken);

            if (latestConfirmed is null)
            {
                return false;
            }

            var latestFailed = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .Where(p =>
                    p.TenantId == tenantId &&
                    p.Proveedor == PaymentProviderType.Tilopay &&
                    (p.Estado == EstadoPagoProveedor.Fallido || p.Estado == EstadoPagoProveedor.Cancelado) &&
                    (planId == null || p.TilopayRecurringPlanId == planId))
                .MaxAsync(p => (DateTime?)p.FechaActualizacionUtc, cancellationToken);

            // El éxito gana si es igual o posterior al último fallo (o si no hay fallo registrado).
            return latestFailed is null || latestConfirmed >= latestFailed;
        }

        private Task<string?> ResolveTenantEmailAsync(Guid tenantId, CancellationToken cancellationToken) =>
            _db.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.Email != null)
                .OrderBy(u => u.Email)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

        private PlatformAuditLog BuildAudit(string action, Guid tenantId, string entityId, string reason, DateTime nowUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                ActorUserId = "system",
                ActorEmail = "system",
                Action = action,
                EntityType = PlatformAuditEntityTypes.Subscription,
                EntityId = entityId,
                TenantId = tenantId,
                Reason = reason.Length <= 500 ? reason : reason[..500],
                CreatedAtUtc = nowUtc
            };

        private static string BuildEventKey(Guid tenantId, int? planId, string? resultCode, DateTime nowUtc) =>
            $"{tenantId:N}:{planId?.ToString() ?? "-"}:{resultCode ?? "-"}:{nowUtc:yyyyMMdd}";

        private DateTime GetUtcNow() => _clock.NowOffset().UtcDateTime;

        private static string? Trim(string? value, int max) =>
            string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
    }
}
