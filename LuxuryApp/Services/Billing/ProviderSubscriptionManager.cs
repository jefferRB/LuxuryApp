using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Security;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Billing
{
    public sealed record ProviderSubscriptionActionResult
    {
        public required bool Succeeded { get; init; }
        public string? Message { get; init; }

        public static ProviderSubscriptionActionResult Ok(string message) => new() { Succeeded = true, Message = message };
        public static ProviderSubscriptionActionResult Fail(string message) => new() { Succeeded = false, Message = message };
    }

    public interface IProviderSubscriptionManager
    {
        bool IsEnabled { get; }

        /// <summary>
        /// Tras aplicar un upgrade, cancela en TiloPay el suscriptor ANTERIOR para evitar doble
        /// cobro. Best-effort y post-commit (HTTP fuera de transacción). Éxito => intent Cancelled +
        /// audit Completed; fallo => queda PendingManualCancellation + audit crítico Failed.
        /// </summary>
        Task TryCancelOldSubscriberForUpgradeAsync(Guid tenantId, CancellationToken cancellationToken = default);

        /// <summary>Cancela (elimina) el suscriptor del proveedor y marca la suscripción local cancelada.</summary>
        Task<ProviderSubscriptionActionResult> CancelAsync(Guid tenantId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default);

        Task<ProviderSubscriptionActionResult> PauseAsync(Guid tenantId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default);
        Task<ProviderSubscriptionActionResult> ReactivateAsync(Guid tenantId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default);
    }

    public sealed class ProviderSubscriptionManager : IProviderSubscriptionManager
    {
        private readonly ApplicationDbContext _db;
        private readonly ITilopayRepeatAdminService _adminService;
        private readonly ITenantExecutionContextAccessor _tenantExecutionContextAccessor;
        private readonly IBusinessDateTimeProvider _clock;
        private readonly OpcionesTilopayRepeatAdmin _adminOptions;
        private readonly ILogger<ProviderSubscriptionManager> _logger;

        public ProviderSubscriptionManager(
            ApplicationDbContext db,
            ITilopayRepeatAdminService adminService,
            ITenantExecutionContextAccessor tenantExecutionContextAccessor,
            IBusinessDateTimeProvider clock,
            IOptions<OpcionesTilopayRepeatAdmin> adminOptions,
            ILogger<ProviderSubscriptionManager> logger)
        {
            _db = db;
            _adminService = adminService;
            _tenantExecutionContextAccessor = tenantExecutionContextAccessor;
            _clock = clock;
            _adminOptions = adminOptions.Value;
            _logger = logger;
        }

        public bool IsEnabled => _adminService.IsEnabled;

        public async Task TryCancelOldSubscriberForUpgradeAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            if (!_adminService.IsEnabled || !_adminOptions.AutoCancelOldSubscriberOnUpgrade)
            {
                return; // Deshabilitado: queda la alerta PendingManualCancellation existente.
            }

            var intent = await _db.PlanChangeIntents
                .IgnoreQueryFilters()
                .Where(i =>
                    i.TenantId == tenantId &&
                    i.Estado == PlanChangeIntentState.Applied &&
                    i.OldProviderCancellation == ProviderCancellationState.PendingManualCancellation &&
                    i.FromProviderSubscriptionId != null)
                .OrderByDescending(i => i.AppliedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (intent?.FromProviderSubscriptionId is not { } oldSubscriberId ||
                string.IsNullOrWhiteSpace(oldSubscriberId))
            {
                return;
            }

            // No cancelar si el suscriptor viejo es el mismo que el nuevo (mismo id => sin doble cobro).
            if (string.Equals(oldSubscriberId.Trim(), intent.NewProviderSubscriptionId?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            TilopayAdminOperationResult result;
            try
            {
                result = await _adminService.DeleteSubscriberAsync(oldSubscriberId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción cancelando suscriptor viejo en upgrade. TenantId {TenantId}.", tenantId);
                result = TilopayAdminOperationResult.Fail("Excepción al cancelar el suscriptor anterior.");
            }

            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            var nowUtc = GetUtcNow();

            var tracked = await _db.PlanChangeIntents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.Id == intent.Id, cancellationToken);

            if (tracked is null)
            {
                return;
            }

            if (result.Succeeded)
            {
                tracked.OldProviderCancellation = ProviderCancellationState.Cancelled;
                tracked.UpdatedAtUtc = nowUtc;
                tracked.Notes = $"Suscriptor anterior {oldSubscriberId} cancelado automáticamente en TiloPay.";

                _db.PlatformAuditLogs.Add(new PlatformAuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = "system",
                    ActorEmail = "system",
                    Action = PlatformAuditActions.UpgradeOldProviderSubscriptionCancellationCompleted,
                    EntityType = PlatformAuditEntityTypes.Subscription,
                    EntityId = tracked.Id.ToString(),
                    TenantId = tenantId,
                    Reason = $"Upgrade a {tracked.ToPlanCode}: suscriptor anterior cancelado en TiloPay. SubscriberIdSuffix {SensitiveDataMasker.MaskReference(oldSubscriberId)}.",
                    CreatedAtUtc = nowUtc
                });

                _logger.LogInformation(
                    "Suscriptor anterior cancelado en upgrade. TenantId {TenantId}. IntentId {IntentId}.",
                    tenantId,
                    tracked.Id);
            }
            else
            {
                // NO ocultar: queda pendiente + alerta crítica para revisión manual (evita doble cobro).
                _db.PlatformAuditLogs.Add(new PlatformAuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = "system",
                    ActorEmail = "system",
                    Action = PlatformAuditActions.UpgradeOldProviderSubscriptionCancellationFailed,
                    EntityType = PlatformAuditEntityTypes.Subscription,
                    EntityId = tracked.Id.ToString(),
                    TenantId = tenantId,
                    Reason = $"CRÍTICO: no se pudo cancelar el suscriptor anterior {oldSubscriberId} en TiloPay tras upgrade a {tracked.ToPlanCode}. Riesgo de doble cobro. Cancelar manualmente. Detalle: {result.Message}",
                    CreatedAtUtc = nowUtc
                });

                _logger.LogError(
                    "Falló la cancelación automática del suscriptor anterior en upgrade. TenantId {TenantId}. IntentId {IntentId}.",
                    tenantId,
                    tracked.Id);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<ProviderSubscriptionActionResult> CancelAsync(
            Guid tenantId,
            string actorUserId,
            string actorEmail,
            CancellationToken cancellationToken = default)
        {
            if (!_adminService.IsEnabled)
            {
                return ProviderSubscriptionActionResult.Fail("La integración de TiloPay Repeat Admin está deshabilitada.");
            }

            var subscriberId = await GetSubscriberIdAsync(tenantId, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return ProviderSubscriptionActionResult.Fail("La suscripción no tiene un id_suscriptor de TiloPay registrado.");
            }

            var result = await _adminService.DeleteSubscriberAsync(subscriberId, cancellationToken);

            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            var nowUtc = GetUtcNow();

            if (result.Succeeded)
            {
                var subscription = await _db.Suscripciones
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
                if (subscription is not null)
                {
                    subscription.Estado = EstadoSuscripcion.Cancelada;
                    subscription.FechaCancelacionUtc = nowUtc;
                    subscription.FechaFin = nowUtc;
                    subscription.FechaUltimaActualizacionUtc = nowUtc;
                    subscription.MotivoEstado = "Cancelación ejecutada en TiloPay desde plataforma.";
                }
            }

            AddOperationAudit(
                result.Succeeded
                    ? PlatformAuditActions.ProviderSubscriptionDeleted
                    : PlatformAuditActions.ProviderSubscriptionDeleteFailed,
                tenantId,
                actorUserId,
                actorEmail,
                subscriberId,
                result.Succeeded
                    ? "Suscriptor eliminado en TiloPay y suscripción local cancelada."
                    : $"Falló la eliminación del suscriptor en TiloPay. Detalle: {result.Message}",
                nowUtc);

            await _db.SaveChangesAsync(cancellationToken);

            return result.Succeeded
                ? ProviderSubscriptionActionResult.Ok("Suscripción cancelada en TiloPay.")
                : ProviderSubscriptionActionResult.Fail(result.Message ?? "No fue posible cancelar en TiloPay.");
        }

        public Task<ProviderSubscriptionActionResult> PauseAsync(Guid tenantId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default) =>
            RunStatusOperationAsync(
                tenantId,
                actorUserId,
                actorEmail,
                subscriberId => _adminService.PauseSubscriberAsync(subscriberId, cancellationToken),
                PlatformAuditActions.ProviderSubscriptionPaused,
                "pausar",
                cancellationToken);

        public Task<ProviderSubscriptionActionResult> ReactivateAsync(Guid tenantId, string actorUserId, string actorEmail, CancellationToken cancellationToken = default) =>
            RunStatusOperationAsync(
                tenantId,
                actorUserId,
                actorEmail,
                subscriberId => _adminService.ReactivateSubscriberAsync(subscriberId, cancellationToken),
                PlatformAuditActions.ProviderSubscriptionReactivated,
                "reactivar",
                cancellationToken);

        private async Task<ProviderSubscriptionActionResult> RunStatusOperationAsync(
            Guid tenantId,
            string actorUserId,
            string actorEmail,
            Func<string, Task<TilopayAdminOperationResult>> operation,
            string auditAction,
            string verb,
            CancellationToken cancellationToken)
        {
            if (!_adminService.IsEnabled)
            {
                return ProviderSubscriptionActionResult.Fail("La integración de TiloPay Repeat Admin está deshabilitada.");
            }

            var subscriberId = await GetSubscriberIdAsync(tenantId, cancellationToken);
            if (string.IsNullOrWhiteSpace(subscriberId))
            {
                return ProviderSubscriptionActionResult.Fail("La suscripción no tiene un id_suscriptor de TiloPay registrado.");
            }

            var result = await operation(subscriberId);

            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(tenantId);
            AddOperationAudit(
                auditAction,
                tenantId,
                actorUserId,
                actorEmail,
                subscriberId,
                result.Succeeded ? $"Suscriptor {verb} en TiloPay." : $"Falló {verb} suscriptor. Detalle: {result.Message}",
                GetUtcNow());
            await _db.SaveChangesAsync(cancellationToken);

            return result.Succeeded
                ? ProviderSubscriptionActionResult.Ok($"Suscriptor {verb} en TiloPay.")
                : ProviderSubscriptionActionResult.Fail(result.Message ?? $"No fue posible {verb} el suscriptor.");
        }

        private async Task<string?> GetSubscriberIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
            await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.ProviderSubscriptionId != null)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .Select(s => s.ProviderSubscriptionId)
                .FirstOrDefaultAsync(cancellationToken);

        private void AddOperationAudit(
            string action,
            Guid tenantId,
            string actorUserId,
            string actorEmail,
            string subscriberId,
            string reason,
            DateTime nowUtc)
        {
            _db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? "system" : actorUserId,
                ActorEmail = string.IsNullOrWhiteSpace(actorEmail) ? "system" : actorEmail,
                Action = action,
                EntityType = PlatformAuditEntityTypes.Subscription,
                EntityId = SensitiveDataMasker.MaskReference(subscriberId),
                TenantId = tenantId,
                Reason = reason.Length <= 500 ? reason : reason[..500],
                CreatedAtUtc = nowUtc
            });
        }

        private DateTime GetUtcNow() => _clock.NowOffset().UtcDateTime;
    }
}
