using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Billing
{
    /// <summary>
    /// Fotografía operativa del módulo Billing para diagnóstico sin acceso directo a la BD.
    /// Solo lectura, cross-tenant, consumida únicamente por la consola de plataforma.
    /// </summary>
    public sealed record BillingHealthSnapshot
    {
        public DateTime GeneratedAtUtc { get; init; }

        // Suscripciones base por estado EFECTIVO (calculado con fechas, no el almacenado).
        public int ActiveSubscriptions { get; init; }
        public int TrialSubscriptions { get; init; }
        public int MorosaSubscriptions { get; init; }
        public int SuspendedSubscriptions { get; init; }
        public int PendingSubscriptions { get; init; }
        public int CancelledSubscriptions { get; init; }
        public int FailedSubscriptions { get; init; }
        public int ActiveWhatsAppAddons { get; init; }

        // Pagos.
        public int PendingPayments { get; init; }
        public int ManualReviewPayments { get; init; }
        public int ConfirmedPaymentsLast24h { get; init; }
        public int FailedPaymentsLast24h { get; init; }

        // Renovaciones.
        public int OverdueRenewals { get; init; }

        // Webhooks / eventos.
        public int UnprocessedEvents { get; init; }
        public int ErrorEventsLast24h { get; init; }
        public int ManualReviewEventsLast7d { get; init; }
        public int UnmatchedEventsLast7d { get; init; }
        public DateTime? LastWebhookReceivedUtc { get; init; }
        public DateTime? LastWebhookProcessedUtc { get; init; }
        public double? AvgWebhookProcessingMsLast24h { get; init; }

        // Reconciliación y alertas.
        public DateTime? LastReconciliationUtc { get; init; }
        public string? LastReconciliationSummaryJson { get; init; }
        public int OpenAlertsLast24h { get; init; }
        public int AutoRepairsLast7d { get; init; }
    }

    public interface IBillingHealthService
    {
        Task<BillingHealthSnapshot> BuildAsync(CancellationToken cancellationToken = default);
    }

    public sealed class BillingHealthService : IBillingHealthService
    {
        private readonly ApplicationDbContext _db;
        private readonly SuscripcionService _suscripcionService;

        public BillingHealthService(ApplicationDbContext db, SuscripcionService suscripcionService)
        {
            _db = db;
            _suscripcionService = suscripcionService;
        }

        public async Task<BillingHealthSnapshot> BuildAsync(CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var last24hUtc = nowUtc.AddHours(-24);
            var last7dUtc = nowUtc.AddDays(-7);

            // Estado efectivo: el volumen de suscripciones es bajo (una por tenant), por lo que
            // proyectarlas y clasificarlas en memoria con la MISMA regla del resolver evita
            // divergencias entre el health check y el control de acceso real.
            var subscriptions = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(s => new Suscripcion
                {
                    Id = s.Id,
                    TenantId = s.TenantId,
                    PlanId = s.PlanId,
                    Estado = s.Estado,
                    FechaFin = s.FechaFin,
                    FechaTrialFin = s.FechaTrialFin,
                    FechaFinGraciaUtc = s.FechaFinGraciaUtc
                })
                .ToListAsync(cancellationToken);

            int active = 0, trial = 0, morosa = 0, suspended = 0, pending = 0, cancelled = 0, failed = 0;
            foreach (var subscription in subscriptions)
            {
                switch (_suscripcionService.GetEffectiveStatus(subscription))
                {
                    case EstadoSuscripcion.Activa: active++; break;
                    case EstadoSuscripcion.Trial: trial++; break;
                    case EstadoSuscripcion.Morosa: morosa++; break;
                    case EstadoSuscripcion.Suspendida: suspended++; break;
                    case EstadoSuscripcion.Pendiente: pending++; break;
                    case EstadoSuscripcion.Cancelada: cancelled++; break;
                    case EstadoSuscripcion.Fallida: failed++; break;
                }
            }

            var addons = await _db.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var activeAddons = addons.Count(addon => _suscripcionService.IsWhatsAppAddonActive(addon));

            var pendingPayments = await _db.PagosSuscripcion.IgnoreQueryFilters()
                .CountAsync(p => p.Estado == EstadoPagoProveedor.Pendiente, cancellationToken);
            var manualReviewPayments = await _db.PagosSuscripcion.IgnoreQueryFilters()
                .CountAsync(p => p.Estado == EstadoPagoProveedor.ManualReview, cancellationToken);
            var confirmed24h = await _db.PagosSuscripcion.IgnoreQueryFilters()
                .CountAsync(p => p.Estado == EstadoPagoProveedor.Confirmado && p.FechaConfirmacionUtc >= last24hUtc, cancellationToken);
            var failed24h = await _db.PagosSuscripcion.IgnoreQueryFilters()
                .CountAsync(p =>
                    (p.Estado == EstadoPagoProveedor.Fallido || p.Estado == EstadoPagoProveedor.Cancelado) &&
                    p.FechaActualizacionUtc >= last24hUtc, cancellationToken);

            var overdueRenewals = await _db.Suscripciones.IgnoreQueryFilters()
                .CountAsync(s =>
                    s.Proveedor == PaymentProviderType.Tilopay &&
                    s.TilopayRecurringPlanId != null &&
                    s.Estado == EstadoSuscripcion.Activa &&
                    !s.CancelAtPeriodEnd &&
                    s.FechaProximoCobroUtc != null &&
                    s.FechaProximoCobroUtc < nowUtc, cancellationToken);

            var unprocessedEvents = await _db.EventosPago.IgnoreQueryFilters()
                .CountAsync(e => !e.Procesado &&
                    (e.EstadoProcesamiento == "Recibido" || e.EstadoProcesamiento == "Error"), cancellationToken);
            var errorEvents24h = await _db.EventosPago.IgnoreQueryFilters()
                .CountAsync(e => e.EstadoProcesamiento == "Error" && e.FechaRecepcionUtc >= last24hUtc, cancellationToken);
            var manualReviewEvents7d = await _db.EventosPago.IgnoreQueryFilters()
                .CountAsync(e => e.EstadoProcesamiento == "PendingManualReview" && e.FechaRecepcionUtc >= last7dUtc, cancellationToken);
            var unmatchedEvents7d = await _db.EventosPago.IgnoreQueryFilters()
                .CountAsync(e => e.EstadoProcesamiento == "SinRelacion" && e.FechaRecepcionUtc >= last7dUtc, cancellationToken);

            var lastWebhookReceived = await _db.EventosPago.IgnoreQueryFilters()
                .MaxAsync(e => (DateTime?)e.FechaRecepcionUtc, cancellationToken);
            var lastWebhookProcessed = await _db.EventosPago.IgnoreQueryFilters()
                .Where(e => e.Procesado)
                .MaxAsync(e => (DateTime?)e.FechaProcesamientoUtc, cancellationToken);

            // Duración promedio: proyección acotada y cálculo en memoria (fechas de C#,
            // sin depender de traducción SQL de DateDiff).
            var processedTimings = await _db.EventosPago.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => e.Procesado && e.FechaProcesamientoUtc != null && e.FechaRecepcionUtc >= last24hUtc)
                .OrderByDescending(e => e.FechaRecepcionUtc)
                .Take(500)
                .Select(e => new { e.FechaRecepcionUtc, e.FechaProcesamientoUtc })
                .ToListAsync(cancellationToken);
            double? avgProcessingMs = processedTimings.Count == 0
                ? null
                : processedTimings.Average(t => (t.FechaProcesamientoUtc!.Value - t.FechaRecepcionUtc).TotalMilliseconds);

            var lastReconciliation = await _db.PlatformAuditLogs
                .AsNoTracking()
                .Where(log => log.Action == PlatformAuditActions.BillingReconciliationCompleted)
                .OrderByDescending(log => log.CreatedAtUtc)
                .Select(log => new { log.CreatedAtUtc, log.AfterJson })
                .FirstOrDefaultAsync(cancellationToken);

            var openAlerts24h = await _db.PlatformAuditLogs
                .CountAsync(log =>
                    (log.Action == PlatformAuditActions.BillingReconciliationAlert ||
                     log.Action == PlatformAuditActions.PaymentWebhookRequiresManualReview) &&
                    log.CreatedAtUtc >= last24hUtc, cancellationToken);

            var autoRepairs7d = await _db.PlatformAuditLogs
                .CountAsync(log =>
                    log.Action == PlatformAuditActions.BillingAutoRepairApplied &&
                    log.CreatedAtUtc >= last7dUtc, cancellationToken);

            return new BillingHealthSnapshot
            {
                GeneratedAtUtc = nowUtc,
                ActiveSubscriptions = active,
                TrialSubscriptions = trial,
                MorosaSubscriptions = morosa,
                SuspendedSubscriptions = suspended,
                PendingSubscriptions = pending,
                CancelledSubscriptions = cancelled,
                FailedSubscriptions = failed,
                ActiveWhatsAppAddons = activeAddons,
                PendingPayments = pendingPayments,
                ManualReviewPayments = manualReviewPayments,
                ConfirmedPaymentsLast24h = confirmed24h,
                FailedPaymentsLast24h = failed24h,
                OverdueRenewals = overdueRenewals,
                UnprocessedEvents = unprocessedEvents,
                ErrorEventsLast24h = errorEvents24h,
                ManualReviewEventsLast7d = manualReviewEvents7d,
                UnmatchedEventsLast7d = unmatchedEvents7d,
                LastWebhookReceivedUtc = lastWebhookReceived,
                LastWebhookProcessedUtc = lastWebhookProcessed,
                AvgWebhookProcessingMsLast24h = avgProcessingMs,
                LastReconciliationUtc = lastReconciliation?.CreatedAtUtc,
                LastReconciliationSummaryJson = lastReconciliation?.AfterJson,
                OpenAlertsLast24h = openAlerts24h,
                AutoRepairsLast7d = autoRepairs7d
            };
        }
    }
}
