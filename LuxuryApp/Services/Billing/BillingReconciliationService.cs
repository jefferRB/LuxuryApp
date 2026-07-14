using System.Text.Json;
using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Billing
{
    public interface IBillingReconciliationService
    {
        /// <summary>
        /// Pase completo de reconciliación: repara lo determinístico, alerta lo ambiguo,
        /// limpia lo abandonado. Nunca modifica datos ambiguos. Todo queda auditado.
        /// </summary>
        Task<BillingReconciliationReport> RunAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Red de seguridad del módulo Billing. El flujo normal es 100% webhook-driven; este
    /// servicio cubre los huecos que dejan webhooks perdidos, reinicios y estados parciales:
    ///
    /// 1. Pago recurrente CONFIRMADO cuya suscripción no quedó activa => reparación automática
    ///    (el dinero ya está cobrado; activar es determinístico) o alerta si no es seguro.
    /// 2. Suscripción/add-on Activa con FechaProximoCobroUtc vencida => ALERTA únicamente
    ///    (no sabemos si TiloPay cobró; extender sin evidencia sería regalar servicio,
    ///    suspender sin evidencia sería castigar a un cliente que pagó).
    /// 3. Pendiente sin webhook por más de N días => Expirado (limpieza segura: la correlación
    ///    de webhooks solo mira 48h hacia atrás, ese intento ya no puede activarse solo).
    /// 4. ManualReview viejo => alerta; NUNCA se toca (puede haber dinero cobrado detrás).
    /// 5. EventoPago atascado en Recibido/Error => alerta (procesamiento murió a mitad).
    /// </summary>
    public sealed class BillingReconciliationService : IBillingReconciliationService
    {
        private readonly ApplicationDbContext _db;
        private readonly SuscripcionService _suscripcionService;
        private readonly ITenantExecutionContextAccessor _tenantExecutionContextAccessor;
        private readonly IBusinessDateTimeProvider _clock;
        private readonly TilopayRepeatOptions _repeatOptions;
        private readonly BillingReconciliationOptions _options;
        private readonly ISubscriberResolutionService? _subscriberResolutionService;
        private readonly int _adminMaxAttempts;
        private readonly ILogger<BillingReconciliationService> _logger;

        public BillingReconciliationService(
            ApplicationDbContext db,
            SuscripcionService suscripcionService,
            ITenantExecutionContextAccessor tenantExecutionContextAccessor,
            IBusinessDateTimeProvider clock,
            IOptions<TilopayRepeatOptions> repeatOptions,
            IOptions<BillingReconciliationOptions> options,
            ILogger<BillingReconciliationService> logger,
            ISubscriberResolutionService? subscriberResolutionService = null,
            IOptions<OpcionesTilopayRepeatAdmin>? adminOptions = null)
        {
            _db = db;
            _suscripcionService = suscripcionService;
            _tenantExecutionContextAccessor = tenantExecutionContextAccessor;
            _clock = clock;
            _repeatOptions = repeatOptions.Value;
            _options = options.Value;
            _subscriberResolutionService = subscriberResolutionService;
            _adminMaxAttempts = adminOptions?.Value.MaxReconciliationResolveAttempts ?? 6;
            _logger = logger;
        }

        public async Task<BillingReconciliationReport> RunAsync(CancellationToken cancellationToken = default)
        {
            var nowUtc = GetUtcNow();
            var report = new BillingReconciliationReport { StartedUtc = nowUtc };

            // Cada fase se ejecuta AISLADA: un fallo en una (p.ej. expiración de stale de un tenant)
            // no impide que corran las demás (en particular, el backfill de id_suscriptor). Cada
            // fase empieza con el ChangeTracker limpio para no arrastrar entidades de otro tenant.
            await RunPhaseAsync("OrphanConfirmedPayments", () => ReconcileOrphanConfirmedPaymentsAsync(report, nowUtc, cancellationToken), cancellationToken);
            await RunPhaseAsync("OverdueRenewals", () => AlertOverdueRenewalsAsync(report, nowUtc, cancellationToken), cancellationToken);
            await RunPhaseAsync("ExpireStalePendings", () => ExpireStalePendingAttemptsAsync(report, nowUtc, cancellationToken), cancellationToken);
            await RunPhaseAsync("StaleManualReviews", () => AlertStaleManualReviewsAsync(report, nowUtc, cancellationToken), cancellationToken);
            await RunPhaseAsync("StuckEvents", () => AlertStuckEventsAsync(report, nowUtc, cancellationToken), cancellationToken);
            await RunPhaseAsync("SubscriberBackfill", () => BackfillMissingSubscriberIdsAsync(report, nowUtc, cancellationToken), cancellationToken);

            report.FinishedUtc = GetUtcNow();

            // El resumen del pase es un PlatformAuditLog (cross-tenant, NO ITenantEntity): se guarda
            // con el ChangeTracker ya limpio para que no arrastre entidades tenant-scoped colgadas.
            DetachAllTracked();

            // Cierre del pase: SIEMPRE se registra (sin cooldown) para que el health check
            // pueda mostrar "última reconciliación" y su resumen sin consultar logs de texto.
            _db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = "system",
                ActorEmail = "system",
                Action = PlatformAuditActions.BillingReconciliationCompleted,
                EntityType = PlatformAuditEntityTypes.Billing,
                Reason = report.HasFindings
                    ? "Pase de reconciliación con hallazgos. Ver AfterJson."
                    : "Pase de reconciliación sin hallazgos.",
                AfterJson = JsonSerializer.Serialize(report),
                CreatedAtUtc = report.FinishedUtc
            });

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Reconciliación Billing completada. DurationMs {DurationMs}. Reparados {Repaired}. AlertasHuérfanos {OrphanAlerts}. RenovacionesVencidas {Overdue}. AddonsVencidos {OverdueAddons}. PendientesExpirados {StaleExpired}. ManualReviewViejos {StaleManual}. EventosAtascados {StuckEvents}. AlertasSuprimidas {Suppressed}.",
                report.DurationMs,
                report.OrphanPaymentsRepaired,
                report.OrphanPaymentsAlerted,
                report.OverdueRenewalsAlerted,
                report.OverdueAddonsAlerted,
                report.StalePendingsExpired,
                report.StaleManualReviewsAlerted,
                report.StuckEventsAlerted,
                report.AlertsSuppressedByCooldown);

            return report;
        }

        // ── Aislamiento de fases y multi-tenant ──────────────────────────────────────

        /// <summary>
        /// Ejecuta una fase con el ChangeTracker limpio y aislada de fallos: si lanza, se registra,
        /// se audita y el pase CONTINÚA con las demás fases. Nunca deja entidades tenant-scoped
        /// colgadas que puedan mezclar tenants en un SaveChanges posterior.
        /// </summary>
        private async Task RunPhaseAsync(string phase, Func<Task> body, CancellationToken cancellationToken)
        {
            DetachAllTracked();

            try
            {
                await body();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Descartar cualquier cambio parcial para poder auditar el fallo sin re-lanzar.
                DetachAllTracked();

                _logger.LogError(ex, "Fase de reconciliación aislada por fallo. Phase {Phase}.", phase);

                try
                {
                    _db.PlatformAuditLogs.Add(new PlatformAuditLog
                    {
                        Id = Guid.NewGuid(),
                        ActorUserId = "system",
                        ActorEmail = "system",
                        Action = PlatformAuditActions.BillingReconciliationAlert,
                        EntityType = PlatformAuditEntityTypes.Billing,
                        Reason = Trim($"La fase '{phase}' de la reconciliación falló y se aisló; el resto del pase continuó. Detalle: {ex.Message}", 500),
                        CreatedAtUtc = GetUtcNow()
                    });

                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception auditEx)
                {
                    _logger.LogError(auditEx, "No fue posible auditar el fallo de la fase {Phase}.", phase);
                    DetachAllTracked();
                }
            }
        }

        private static string Trim(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        /// <summary>Deja el ChangeTracker vacío para que ninguna fase arrastre entidades de otra.</summary>
        private void DetachAllTracked()
        {
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        /// <summary>
        /// Registra que se encontraron registros sin TenantId donde debería existir: no se mezclan
        /// con entidades tenant-scoped; se auditan y se saltan (task 6).
        /// </summary>
        private async Task AuditMissingTenantSkipAsync(string phase, int count, CancellationToken cancellationToken)
        {
            _db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = "system",
                ActorEmail = "system",
                Action = PlatformAuditActions.BillingReconciliationAlert,
                EntityType = PlatformAuditEntityTypes.Billing,
                Reason = $"La fase '{phase}' encontró {count} registro(s) sin TenantId resuelto. Se auditan y se saltan para no mezclar tenants.",
                CreatedAtUtc = GetUtcNow()
            });

            await _db.SaveChangesAsync(cancellationToken);
            DetachAllTracked();

            _logger.LogWarning(
                "Reconciliación: {Count} registro(s) sin TenantId en la fase {Phase}; saltados.",
                count,
                phase);
        }

        // ── 6. Backfill de id_suscriptor faltante (subscriber resolution) ─────────────

        private async Task BackfillMissingSubscriberIdsAsync(
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            // Solo si la integración admin de TiloPay está activa; si no, no hay forma de resolver.
            if (_subscriberResolutionService is null || !_subscriberResolutionService.IsEnabled)
            {
                return;
            }

            // Pass A (local, sin API): suscripción base con ProviderSubscriptionId NULL cuando ya
            // existe un pago confirmado del mismo tenant con ProviderSubscriberId conocido → copiar.
            // Escaneo cross-tenant SOLO lectura; la escritura va por tenant bajo su propio scope.
            var subscriptionsMissingId = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(subscription =>
                    subscription.Proveedor == PaymentProviderType.Tilopay &&
                    subscription.TilopayRecurringPlanId != null &&
                    subscription.ProviderSubscriptionId == null &&
                    (subscription.Estado == EstadoSuscripcion.Activa ||
                     subscription.Estado == EstadoSuscripcion.Morosa))
                .Select(subscription => new { subscription.Id, subscription.TenantId })
                .ToListAsync(cancellationToken);

            foreach (var group in subscriptionsMissingId.GroupBy(subscription => subscription.TenantId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (group.Key == Guid.Empty)
                {
                    await AuditMissingTenantSkipAsync("SubscriberBackfillLocal", group.Count(), cancellationToken);
                    continue;
                }

                try
                {
                    await BackfillSubscriberIdsLocallyForTenantAsync(group.Key, report, nowUtc, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DetachAllTracked();
                    _logger.LogError(
                        ex,
                        "Backfill local de id_suscriptor falló para el tenant {TenantId}; se continúa con los demás.",
                        group.Key);
                }
            }

            // Pass B (API): pagos recurrentes CONFIRMADOS sin ProviderSubscriberId y con email.
            // Se resuelve por (plan, email) contra TiloPay; el servicio persiste y audita.
            var lookbackUtc = nowUtc.AddDays(-Math.Max(1, _options.ConfirmedPaymentLookbackDays));
            var paymentsMissingSubscriber = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(payment =>
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.Confirmado &&
                    payment.TilopayRecurringPlanId != null &&
                    payment.ProviderSubscriberId == null &&
                    payment.ClienteEmail != null &&
                    payment.FechaConfirmacionUtc >= lookbackUtc)
                .OrderByDescending(payment => payment.FechaConfirmacionUtc)
                .Select(payment => new
                {
                    payment.Id,
                    payment.TenantId,
                    payment.PlanId,
                    payment.TilopayRecurringPlanId,
                    payment.ClienteEmail
                })
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var payment in paymentsMissingSubscriber)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // No reintentar indefinidamente: si ya hubo demasiados intentos fallidos/pending,
                // se deja como alerta persistente (visible en BillingHealth) y no se llama más.
                var priorAttempts = await _db.PlatformAuditLogs.CountAsync(
                    log =>
                        (log.Action == PlatformAuditActions.ProviderSubscriberResolutionPending ||
                         log.Action == PlatformAuditActions.ProviderSubscriberResolutionFailed) &&
                        log.EntityId == payment.Id.ToString(),
                    cancellationToken);

                if (priorAttempts >= Math.Max(1, _adminMaxAttempts))
                {
                    continue;
                }

                var isAddon = _repeatOptions.FindByRecurringPlanId(payment.TilopayRecurringPlanId)?.IsAddon ?? false;

                var outcome = await _subscriberResolutionService.TryResolveAndPersistAsync(
                    new SubscriberResolutionContext
                    {
                        TenantId = payment.TenantId,
                        TilopayRecurringPlanId = payment.TilopayRecurringPlanId!.Value,
                        Email = payment.ClienteEmail,
                        PaymentId = payment.Id,
                        IsAddon = isAddon,
                        Source = "reconciliation"
                    },
                    cancellationToken);

                switch (outcome)
                {
                    case SubscriberPersistenceOutcome.Resolved:
                        report.SubscriberIdsResolved++;
                        break;
                    case SubscriberPersistenceOutcome.Ambiguous:
                        report.SubscriberIdsAmbiguous++;
                        break;
                    case SubscriberPersistenceOutcome.Pending:
                    case SubscriberPersistenceOutcome.Failed:
                        report.SubscriberIdsPending++;
                        break;
                }
            }
        }

        /// <summary>
        /// Copia el id_suscriptor conocido (de un pago confirmado) a las suscripciones del tenant
        /// que lo tienen NULL. Todo bajo el scope del tenant y un único SaveChanges por tenant.
        /// </summary>
        private async Task BackfillSubscriberIdsLocallyForTenantAsync(
            Guid tenantId,
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(tenantId);

            // Filtros EXPLÍCITOS por tenant: no dependemos del query filter ambiente.
            var knownSubscriberId = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(payment =>
                    payment.TenantId == tenantId &&
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.Confirmado &&
                    payment.ProviderSubscriberId != null)
                .OrderByDescending(payment => payment.FechaConfirmacionUtc)
                .Select(payment => payment.ProviderSubscriberId)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(knownSubscriberId))
            {
                return;
            }

            var subscriptions = await _db.Suscripciones
                .IgnoreQueryFilters()
                .Where(subscription =>
                    subscription.TenantId == tenantId &&
                    subscription.Proveedor == PaymentProviderType.Tilopay &&
                    subscription.TilopayRecurringPlanId != null &&
                    subscription.ProviderSubscriptionId == null &&
                    (subscription.Estado == EstadoSuscripcion.Activa ||
                     subscription.Estado == EstadoSuscripcion.Morosa))
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
            {
                return;
            }

            foreach (var subscription in subscriptions)
            {
                subscription.ProviderSubscriptionId = knownSubscriberId;
                subscription.FechaUltimaActualizacionUtc = nowUtc;

                _db.PlatformAuditLogs.Add(new PlatformAuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = "system",
                    ActorEmail = "system",
                    Action = PlatformAuditActions.ProviderSubscriberResolved,
                    EntityType = PlatformAuditEntityTypes.Subscription,
                    EntityId = subscription.Id.ToString(),
                    TenantId = subscription.TenantId,
                    Reason = "id_suscriptor copiado localmente desde un pago confirmado del mismo tenant (reconciliación).",
                    CreatedAtUtc = nowUtc
                });

                report.SubscriberIdsBackfilledLocally++;
            }

            await _db.SaveChangesAsync(cancellationToken);
            DetachAllTracked();
        }

        // ── 1. Pagos confirmados sin activación ──────────────────────────────────────

        private async Task ReconcileOrphanConfirmedPaymentsAsync(
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var lookbackUtc = nowUtc.AddDays(-Math.Max(1, _options.ConfirmedPaymentLookbackDays));

            var confirmedPayments = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(payment => payment.Plan)
                .Where(payment =>
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.Confirmado &&
                    payment.TilopayRecurringPlanId != null &&
                    payment.FechaConfirmacionUtc != null &&
                    payment.FechaConfirmacionUtc >= lookbackUtc)
                .ToListAsync(cancellationToken);

            foreach (var payment in confirmedPayments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var repeatPlan = _repeatOptions.FindByRecurringPlanId(payment.TilopayRecurringPlanId);

                if (repeatPlan is null || payment.Plan is null || !payment.Plan.Activo)
                {
                    continue; // Plan retirado del catálogo: no hay forma segura de decidir nada.
                }

                if (repeatPlan.IsAddon)
                {
                    await ReconcileOrphanAddonPaymentAsync(report, payment, cancellationToken);
                    continue;
                }

                var suscripcion = await _db.Suscripciones
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(current => current.TenantId == payment.TenantId, cancellationToken);

                if (suscripcion is not null && _suscripcionService.CanAccessApp(suscripcion))
                {
                    continue; // Sano: hay acceso vigente, el pago fue aplicado.
                }

                var paymentAppliedToSubscription =
                    suscripcion is not null &&
                    suscripcion.FechaUltimoPagoUtc.HasValue &&
                    payment.FechaConfirmacionUtc.HasValue &&
                    suscripcion.FechaUltimoPagoUtc.Value >= payment.FechaConfirmacionUtc.Value.AddMinutes(-2);

                if (paymentAppliedToSubscription)
                {
                    // El pago sí se aplicó y la suscripción venció después por causas normales.
                    // Eso lo cubre el chequeo de renovaciones vencidas, no este.
                    continue;
                }

                if (suscripcion is not null && suscripcion.Estado == EstadoSuscripcion.Cancelada)
                {
                    // Pago confirmado sobre una suscripción cancelada explícitamente: ambiguo
                    // (¿cobro póstumo de TiloPay? ¿cancelación equivocada?). Humano decide.
                    if (await TryAddAlertAsync(
                        report,
                        PlatformAuditEntityTypes.Subscription,
                        payment.Id.ToString(),
                        payment.TenantId,
                        $"Pago recurrente confirmado ({payment.Monto:0.00} {payment.Moneda}, tx {payment.ProviderTransactionId}) sobre una suscripción CANCELADA. Posible cobro póstumo en TiloPay: revisar y cancelar la suscripción del proveedor o reactivar según corresponda.",
                        cancellationToken))
                    {
                        report.OrphanPaymentsAlerted++;
                    }

                    continue;
                }

                if (!_options.AutoRepairEnabled)
                {
                    if (await TryAddAlertAsync(
                        report,
                        PlatformAuditEntityTypes.Subscription,
                        payment.Id.ToString(),
                        payment.TenantId,
                        $"Pago recurrente confirmado sin activación ({payment.Monto:0.00} {payment.Moneda}, tx {payment.ProviderTransactionId}). AutoRepair está deshabilitado: activar manualmente desde Platform/RecurringCheckouts.",
                        cancellationToken))
                    {
                        report.OrphanPaymentsAlerted++;
                    }

                    continue;
                }

                await RepairOrphanBasePaymentAsync(report, payment, suscripcion, cancellationToken);
            }
        }

        private async Task RepairOrphanBasePaymentAsync(
            BillingReconciliationReport report,
            PagoSuscripcion payment,
            Suscripcion? suscripcionBefore,
            CancellationToken cancellationToken)
        {
            var estadoAnterior = suscripcionBefore?.Estado.ToString() ?? "(sin suscripción)";

            // Tenant scope: SESSION_CONTEXT correcto para RLS al escribir fuera de un request.
            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(payment.TenantId);

            await _suscripcionService.ActivarSuscripcionRecurrenteAsync(
                payment.TenantId,
                payment.Plan!,
                payment.TilopayRecurringPlanId!.Value,
                payment.ProviderSubscriberId,
                payment.ProviderTransactionId,
                payment.ProviderReference ?? payment.ReferenciaInterna,
                motivo: "Reparación automática: pago confirmado sin activación detectado por reconciliación.",
                cancellationToken: cancellationToken);

            _db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = "system",
                ActorEmail = "system",
                Action = PlatformAuditActions.BillingAutoRepairApplied,
                EntityType = PlatformAuditEntityTypes.Subscription,
                EntityId = payment.Id.ToString(),
                TenantId = payment.TenantId,
                BeforeJson = JsonSerializer.Serialize(new { estado = estadoAnterior }),
                AfterJson = JsonSerializer.Serialize(new { estado = EstadoSuscripcion.Activa.ToString() }),
                Reason = $"Pago confirmado sin activación reparado. Monto {payment.Monto:0.00} {payment.Moneda}. Tx {payment.ProviderTransactionId}. Plan {payment.Plan!.Codigo ?? payment.Plan.Nombre}.",
                CreatedAtUtc = GetUtcNow()
            });

            await _db.SaveChangesAsync(cancellationToken);

            report.OrphanPaymentsRepaired++;

            _logger.LogWarning(
                "Reconciliación reparó pago confirmado sin activación. TenantId {TenantId}. PaymentId {PaymentId}. PlanId {PlanId}. Monto {Monto} {Moneda}. TransactionId {TransactionId}. EstadoAnterior {EstadoAnterior}. EstadoNuevo {EstadoNuevo}.",
                payment.TenantId,
                payment.Id,
                payment.PlanId,
                payment.Monto,
                payment.Moneda,
                payment.ProviderTransactionId,
                estadoAnterior,
                EstadoSuscripcion.Activa);
        }

        private async Task ReconcileOrphanAddonPaymentAsync(
            BillingReconciliationReport report,
            PagoSuscripcion payment,
            CancellationToken cancellationToken)
        {
            var addon = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(current => current.TenantId == payment.TenantId, cancellationToken);

            if (addon is not null && _suscripcionService.IsWhatsAppAddonActive(addon))
            {
                return;
            }

            var paymentApplied =
                addon is not null &&
                payment.FechaConfirmacionUtc.HasValue &&
                addon.UpdatedAtUtc >= payment.FechaConfirmacionUtc.Value.AddMinutes(-2);

            if (paymentApplied)
            {
                return;
            }

            // Los add-ons NO se auto-reparan: activarlos enciende automatizaciones de WhatsApp
            // hacia clientes finales. Un humano confirma primero que el plan base está sano.
            if (await TryAddAlertAsync(
                report,
                PlatformAuditEntityTypes.Subscription,
                payment.Id.ToString(),
                payment.TenantId,
                $"Pago de add-on WhatsApp confirmado ({payment.Monto:0.00} {payment.Moneda}, tx {payment.ProviderTransactionId}) sin add-on activo. Revisar y activar manualmente desde Platform/RecurringCheckouts.",
                cancellationToken))
            {
                report.OrphanPaymentsAlerted++;
            }
        }

        // ── 2. Renovaciones vencidas (solo alerta) ───────────────────────────────────

        private async Task AlertOverdueRenewalsAsync(
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var overdueCutoffUtc = nowUtc.AddHours(-Math.Max(1, _options.OverdueRenewalToleranceHours));

            var overdueSubscriptions = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(subscription =>
                    subscription.Proveedor == PaymentProviderType.Tilopay &&
                    subscription.TilopayRecurringPlanId != null &&
                    subscription.Estado == EstadoSuscripcion.Activa &&
                    !subscription.CancelAtPeriodEnd &&
                    subscription.FechaProximoCobroUtc != null &&
                    subscription.FechaProximoCobroUtc < overdueCutoffUtc)
                .Select(subscription => new
                {
                    subscription.Id,
                    subscription.TenantId,
                    subscription.CodigoPlan,
                    subscription.FechaProximoCobroUtc
                })
                .ToListAsync(cancellationToken);

            foreach (var subscription in overdueSubscriptions)
            {
                if (await TryAddAlertAsync(
                    report,
                    PlatformAuditEntityTypes.Subscription,
                    subscription.Id.ToString(),
                    subscription.TenantId,
                    $"Renovación vencida sin webhook: plan {subscription.CodigoPlan}, próximo cobro esperado {subscription.FechaProximoCobroUtc:yyyy-MM-dd HH:mm} UTC. Verificar en TiloPay si el cobro ocurrió (activar por conciliación) o falló (el período de gracia aplicará solo).",
                    cancellationToken))
                {
                    report.OverdueRenewalsAlerted++;
                }
            }

            var overdueAddons = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(addon =>
                    addon.Estado == EstadoSuscripcion.Activa &&
                    addon.TilopayRecurringPlanId != null &&
                    addon.FechaProximoCobroUtc != null &&
                    addon.FechaProximoCobroUtc < overdueCutoffUtc)
                .Select(addon => new { addon.Id, addon.TenantId, addon.AddonCode, addon.FechaProximoCobroUtc })
                .ToListAsync(cancellationToken);

            foreach (var addon in overdueAddons)
            {
                if (await TryAddAlertAsync(
                    report,
                    PlatformAuditEntityTypes.Subscription,
                    addon.Id.ToString(),
                    addon.TenantId,
                    $"Renovación de add-on vencida sin webhook: {addon.AddonCode}, próximo cobro esperado {addon.FechaProximoCobroUtc:yyyy-MM-dd HH:mm} UTC. Verificar el cobro en TiloPay.",
                    cancellationToken))
                {
                    report.OverdueAddonsAlerted++;
                }
            }
        }

        // ── 3. Pendientes abandonados (limpieza segura) ──────────────────────────────

        private async Task ExpireStalePendingAttemptsAsync(
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var staleCutoffUtc = nowUtc.AddDays(-Math.Max(1, _options.StalePendingDays));

            // Escaneo cross-tenant SOLO lectura (id + tenant). La escritura se hace por tenant,
            // cada uno bajo su propio scope y SaveChanges, para no mezclar tenants (guard RLS).
            var stalePendings = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(payment =>
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.Pendiente &&
                    payment.FechaCreacionUtc < staleCutoffUtc)
                .Select(payment => new { payment.Id, payment.TenantId })
                .ToListAsync(cancellationToken);

            if (stalePendings.Count == 0)
            {
                return;
            }

            foreach (var group in stalePendings.GroupBy(payment => payment.TenantId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (group.Key == Guid.Empty)
                {
                    // Task 6: registros sin TenantId no se mezclan; se auditan y se saltan.
                    await AuditMissingTenantSkipAsync("ExpireStalePendings", group.Count(), cancellationToken);
                    continue;
                }

                try
                {
                    await ExpireStalePendingsForTenantAsync(
                        group.Key,
                        group.Select(payment => payment.Id).ToList(),
                        report,
                        nowUtc,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Aislar por tenant: un tenant que falle no detiene la expiración de los demás.
                    DetachAllTracked();
                    _logger.LogError(
                        ex,
                        "No fue posible expirar pendientes stale del tenant {TenantId}; se continúa con los demás.",
                        group.Key);
                }
            }
        }

        private async Task ExpireStalePendingsForTenantAsync(
            Guid tenantId,
            IReadOnlyList<Guid> paymentIds,
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(tenantId);

            // Filtro EXPLÍCITO por tenant (no depende del query filter ambiente): recargamos tracked
            // solo las filas de ESTE tenant. El scope sirve para que el guard use la ruta tenant.
            var payments = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .Where(payment => payment.TenantId == tenantId &&
                                  paymentIds.Contains(payment.Id) &&
                                  payment.Estado == EstadoPagoProveedor.Pendiente)
                .ToListAsync(cancellationToken);

            if (payments.Count == 0)
            {
                return;
            }

            foreach (var payment in payments)
            {
                payment.Estado = EstadoPagoProveedor.Expirado;
                payment.ProviderResultCode = "EXPIRED_STALE";
                payment.ProviderResultMessage =
                    $"Expirado por reconciliación: {_options.StalePendingDays} días sin webhook ni confirmación (checkout abandonado).";
                payment.FechaActualizacionUtc = nowUtc;

                _db.PlatformAuditLogs.Add(new PlatformAuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = "system",
                    ActorEmail = "system",
                    Action = PlatformAuditActions.BillingReconciliationCleanup,
                    EntityType = PlatformAuditEntityTypes.Billing,
                    EntityId = payment.Id.ToString(),
                    TenantId = payment.TenantId,
                    Reason = $"Intento Pendiente abandonado ({payment.FechaCreacionUtc:yyyy-MM-dd}) marcado Expirado. Plan {payment.PlanId}.",
                    CreatedAtUtc = nowUtc
                });

                report.StalePendingsExpired++;
            }

            // Changeset de un solo tenant: el guard usa la ruta tenant y no lanza "mezclar tenants".
            await _db.SaveChangesAsync(cancellationToken);
            DetachAllTracked();
        }

        // ── 4. ManualReview viejos (solo alerta, nunca se tocan) ─────────────────────

        private async Task AlertStaleManualReviewsAsync(
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var staleCutoffUtc = nowUtc.AddHours(-Math.Max(1, _options.StaleManualReviewHours));

            var staleReviews = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(payment =>
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.ManualReview &&
                    (payment.FechaActualizacionUtc ?? payment.FechaCreacionUtc) < staleCutoffUtc)
                .Select(payment => new { payment.Id, payment.TenantId, payment.Monto, payment.Moneda, payment.ProviderResultMessage })
                .ToListAsync(cancellationToken);

            foreach (var payment in staleReviews)
            {
                if (await TryAddAlertAsync(
                    report,
                    PlatformAuditEntityTypes.Subscription,
                    payment.Id.ToString(),
                    payment.TenantId,
                    $"Pago en ManualReview sin resolver hace más de {_options.StaleManualReviewHours}h ({payment.Monto:0.00} {payment.Moneda}). Puede haber dinero cobrado sin activar y el tenant tiene el checkout bloqueado. Resolver en Platform/RecurringCheckouts. Detalle: {payment.ProviderResultMessage}",
                    cancellationToken))
                {
                    report.StaleManualReviewsAlerted++;
                }
            }
        }

        // ── 5. Eventos de pago atascados (solo alerta) ───────────────────────────────

        private async Task AlertStuckEventsAsync(
            BillingReconciliationReport report,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var stuckCutoffUtc = nowUtc.AddMinutes(-Math.Max(5, _options.StuckEventMinutes));

            var stuckEvents = await _db.EventosPago
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(evento =>
                    !evento.Procesado &&
                    (evento.EstadoProcesamiento == "Recibido" || evento.EstadoProcesamiento == "Error") &&
                    evento.FechaRecepcionUtc < stuckCutoffUtc)
                .Select(evento => new { evento.Id, evento.TenantId, evento.Tipo, evento.EstadoProcesamiento, evento.Error })
                .ToListAsync(cancellationToken);

            foreach (var evento in stuckEvents)
            {
                if (await TryAddAlertAsync(
                    report,
                    PlatformAuditEntityTypes.Billing,
                    evento.Id.ToString(),
                    evento.TenantId,
                    $"Evento de pago atascado en '{evento.EstadoProcesamiento}' (tipo {evento.Tipo}). El procesamiento se interrumpió y TiloPay no reintentó. Error: {evento.Error ?? "(sin detalle)"}",
                    cancellationToken))
                {
                    report.StuckEventsAlerted++;
                }
            }
        }

        // ── Infraestructura de alertas ───────────────────────────────────────────────

        /// <summary>
        /// Alerta idempotente: no repite la misma alerta sobre la misma entidad dentro
        /// de la ventana de cooldown, para que el pase diario no llene la bitácora.
        /// </summary>
        private async Task<bool> TryAddAlertAsync(
            BillingReconciliationReport report,
            string entityType,
            string entityId,
            Guid? tenantId,
            string reason,
            CancellationToken cancellationToken)
        {
            var cooldownCutoffUtc = GetUtcNow().AddHours(-Math.Max(1, _options.AlertCooldownHours));

            var alreadyAlerted = await _db.PlatformAuditLogs.AnyAsync(
                log =>
                    log.Action == PlatformAuditActions.BillingReconciliationAlert &&
                    log.EntityId == entityId &&
                    log.CreatedAtUtc >= cooldownCutoffUtc,
                cancellationToken);

            if (alreadyAlerted)
            {
                report.AlertsSuppressedByCooldown++;
                return false;
            }

            _db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = "system",
                ActorEmail = "system",
                Action = PlatformAuditActions.BillingReconciliationAlert,
                EntityType = entityType,
                EntityId = entityId,
                TenantId = tenantId,
                Reason = reason.Length <= 500 ? reason : reason[..500],
                CreatedAtUtc = GetUtcNow()
            });

            // Persistir la alerta de INMEDIATO: es un PlatformAuditLog (NO ITenantEntity), así que
            // guardarlo solo nunca mezcla tenants; y así el aislamiento de fases (DetachAllTracked)
            // no puede descartar alertas aún no guardadas.
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Alerta de reconciliación Billing. EntityType {EntityType}. EntityId {EntityId}. TenantId {TenantId}. Reason {Reason}.",
                entityType,
                entityId,
                tenantId,
                reason);

            return true;
        }

        private DateTime GetUtcNow() => _clock.NowOffset().UtcDateTime;
    }
}
