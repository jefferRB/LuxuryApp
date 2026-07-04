using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.SaaS
{
    /// <summary>Datos para iniciar un cambio de plan (origen + destino).</summary>
    public sealed record PlanChangeRequest
    {
        public required Guid TenantId { get; init; }

        public Guid? FromPlanId { get; init; }
        public string? FromPlanCode { get; init; }
        public int? FromWorkerCount { get; init; }
        public int? FromTilopayRecurringPlanId { get; init; }
        public string? FromProviderSubscriptionId { get; init; }

        public required Guid ToPlanId { get; init; }
        public required string ToPlanCode { get; init; }
        public required int ToWorkerCount { get; init; }
        public required BillingCycle ToBillingCycle { get; init; }
        public required int ToTilopayRecurringPlanId { get; init; }
    }

    public sealed record PlanChangeStartResult
    {
        public PlanChangeIntent? Intent { get; init; }
        public string? Error { get; init; }
        public bool Succeeded => Intent is not null && string.IsNullOrEmpty(Error);

        public static PlanChangeStartResult Ok(PlanChangeIntent intent) => new() { Intent = intent };
        public static PlanChangeStartResult Fail(string error) => new() { Error = error };
    }

    public interface IPlanChangeService
    {
        Task<PlanChangeIntent?> GetOpenIntentAsync(Guid tenantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Crea (o reutiliza) el intento de cambio de plan. Anti doble-cambio: si ya hay un
        /// intento Pending para OTRO plan destino, lo rechaza; si es el mismo destino, lo reutiliza.
        /// </summary>
        Task<PlanChangeStartResult> CreateOrReuseAsync(PlanChangeRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marca aplicado el intento del tenant que apunta al plan recurrente recién activado.
        /// Idempotente. Si la suscripción anterior del proveedor difiere, la marca para
        /// cancelación manual y emite una alerta de plataforma. Sin intento abierto => no-op
        /// (fue una alta nueva, no un cambio).
        /// </summary>
        Task ApplyAppliedAsync(
            Guid tenantId,
            int appliedRecurringPlanId,
            string? newProviderSubscriptionId,
            CancellationToken cancellationToken = default);
    }

    public sealed class PlanChangeService : IPlanChangeService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PlanChangeService> _logger;

        public PlanChangeService(ApplicationDbContext db, ILogger<PlanChangeService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task<PlanChangeIntent?> GetOpenIntentAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            _db.PlanChangeIntents
                .IgnoreQueryFilters()
                .Where(intent => intent.TenantId == tenantId && intent.Estado == PlanChangeIntentState.Pending)
                .OrderByDescending(intent => intent.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

        public async Task<PlanChangeStartResult> CreateOrReuseAsync(
            PlanChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var open = await GetOpenIntentAsync(request.TenantId, cancellationToken);
            if (open is not null)
            {
                if (open.ToTilopayRecurringPlanId == request.ToTilopayRecurringPlanId)
                {
                    return PlanChangeStartResult.Ok(open);
                }

                return PlanChangeStartResult.Fail(
                    $"Ya tenés un cambio de plan en proceso (hacia {open.ToPlanCode}). Completá ese pago o cancelalo antes de iniciar otro.");
            }

            var intent = new PlanChangeIntent
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                FromPlanId = request.FromPlanId,
                FromPlanCode = request.FromPlanCode,
                FromWorkerCount = request.FromWorkerCount,
                FromTilopayRecurringPlanId = request.FromTilopayRecurringPlanId,
                FromProviderSubscriptionId = request.FromProviderSubscriptionId,
                ToPlanId = request.ToPlanId,
                ToPlanCode = request.ToPlanCode,
                ToWorkerCount = request.ToWorkerCount,
                ToBillingCycle = request.ToBillingCycle,
                ToTilopayRecurringPlanId = request.ToTilopayRecurringPlanId,
                Estado = PlanChangeIntentState.Pending,
                OldProviderCancellation = ProviderCancellationState.NotRequired,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.PlanChangeIntents.Add(intent);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // El índice único filtrado (un Pending por tenant) bloqueó una carrera de doble click.
                _db.Entry(intent).State = EntityState.Detached;
                var existing = await GetOpenIntentAsync(request.TenantId, cancellationToken);
                if (existing is not null && existing.ToTilopayRecurringPlanId == request.ToTilopayRecurringPlanId)
                {
                    return PlanChangeStartResult.Ok(existing);
                }

                return PlanChangeStartResult.Fail(
                    "Ya hay un cambio de plan en proceso para tu cuenta. Esperá a que termine o cancelalo antes de iniciar otro.");
            }

            return PlanChangeStartResult.Ok(intent);
        }

        public async Task ApplyAppliedAsync(
            Guid tenantId,
            int appliedRecurringPlanId,
            string? newProviderSubscriptionId,
            CancellationToken cancellationToken = default)
        {
            var intent = await _db.PlanChangeIntents
                .IgnoreQueryFilters()
                .Where(current =>
                    current.TenantId == tenantId &&
                    current.Estado == PlanChangeIntentState.Pending &&
                    current.ToTilopayRecurringPlanId == appliedRecurringPlanId)
                .OrderByDescending(current => current.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (intent is null)
            {
                // No había cambio en curso para este plan: fue una alta/renovación normal.
                return;
            }

            var nowUtc = DateTime.UtcNow;
            intent.Estado = PlanChangeIntentState.Applied;
            intent.NewProviderSubscriptionId = newProviderSubscriptionId;
            intent.AppliedAtUtc = nowUtc;
            intent.UpdatedAtUtc = nowUtc;

            var oldSubscriberId = intent.FromProviderSubscriptionId?.Trim();
            var requiresCancellation =
                !string.IsNullOrWhiteSpace(oldSubscriberId) &&
                !string.Equals(oldSubscriberId, newProviderSubscriptionId?.Trim(), StringComparison.OrdinalIgnoreCase);

            if (requiresCancellation)
            {
                intent.OldProviderCancellation = ProviderCancellationState.PendingManualCancellation;
                intent.Notes = $"Cancelar manualmente en TiloPay la suscripción anterior {oldSubscriberId} (plan {intent.FromPlanCode}).";

                _db.PlatformAuditLogs.Add(new PlatformAuditLog
                {
                    Id = Guid.NewGuid(),
                    ActorUserId = "system",
                    ActorEmail = "system",
                    Action = PlatformAuditActions.PlanUpgradeRequiresProviderCancellation,
                    EntityType = PlatformAuditEntityTypes.Subscription,
                    EntityId = intent.Id.ToString(),
                    TenantId = tenantId,
                    Reason = $"Upgrade de {intent.FromPlanCode} a {intent.ToPlanCode}. Cancelar la suscripción TiloPay anterior {oldSubscriberId} para evitar doble cobro.",
                    CreatedAtUtc = nowUtc
                });

                _logger.LogWarning(
                    "Upgrade aplicado: requiere cancelar manualmente la suscripción anterior en TiloPay. TenantId {TenantId}. From {FromCode} ({FromSubscriber}). To {ToCode} ({ToSubscriber}).",
                    tenantId,
                    intent.FromPlanCode,
                    oldSubscriberId,
                    intent.ToPlanCode,
                    newProviderSubscriptionId);
            }
            else
            {
                intent.OldProviderCancellation = ProviderCancellationState.NotRequired;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
