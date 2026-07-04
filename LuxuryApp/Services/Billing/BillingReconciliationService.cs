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
        private readonly ILogger<BillingReconciliationService> _logger;

        public BillingReconciliationService(
            ApplicationDbContext db,
            SuscripcionService suscripcionService,
            ITenantExecutionContextAccessor tenantExecutionContextAccessor,
            IBusinessDateTimeProvider clock,
            IOptions<TilopayRepeatOptions> repeatOptions,
            IOptions<BillingReconciliationOptions> options,
            ILogger<BillingReconciliationService> logger)
        {
            _db = db;
            _suscripcionService = suscripcionService;
            _tenantExecutionContextAccessor = tenantExecutionContextAccessor;
            _clock = clock;
            _repeatOptions = repeatOptions.Value;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<BillingReconciliationReport> RunAsync(CancellationToken cancellationToken = default)
        {
            var nowUtc = GetUtcNow();
            var report = new BillingReconciliationReport { StartedUtc = nowUtc };

            await ReconcileOrphanConfirmedPaymentsAsync(report, nowUtc, cancellationToken);
            await AlertOverdueRenewalsAsync(report, nowUtc, cancellationToken);
            await ExpireStalePendingAttemptsAsync(report, nowUtc, cancellationToken);
            await AlertStaleManualReviewsAsync(report, nowUtc, cancellationToken);
            await AlertStuckEventsAsync(report, nowUtc, cancellationToken);

            report.FinishedUtc = GetUtcNow();

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

            var stalePendings = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .Where(payment =>
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.Pendiente &&
                    payment.FechaCreacionUtc < staleCutoffUtc)
                .ToListAsync(cancellationToken);

            if (stalePendings.Count == 0)
            {
                return;
            }

            foreach (var payment in stalePendings)
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

            await _db.SaveChangesAsync(cancellationToken);
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
