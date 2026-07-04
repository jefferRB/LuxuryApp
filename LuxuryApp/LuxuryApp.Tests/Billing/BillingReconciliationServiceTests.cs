using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Billing;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// Red de seguridad diaria: repara solo lo determinístico, alerta lo ambiguo,
    /// limpia lo abandonado, y NUNCA toca ManualReview ni suscripciones canceladas.
    /// </summary>
    public class BillingReconciliationServiceTests
    {
        // Reloj fijo de la suite (FixedBusinessDateTimeProvider): 2026-05-26 10:30 -06:00.
        private static readonly DateTime NowUtc =
            new DateTimeOffset(new DateTime(2026, 5, 26, 10, 30, 0), TimeSpan.FromHours(-6)).UtcDateTime;

        [Fact]
        public async Task Run_ConfirmedPaymentWithoutActivation_RepairsSubscriptionAndAudits()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(1, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            var payment = SeedPayment(
                context,
                tenantId,
                plan.Id,
                data.RecurringPlanId,
                EstadoPagoProveedor.Confirmado,
                confirmadoUtc: NowUtc.AddHours(-6));

            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Pendiente,
                Proveedor = PaymentProviderType.Tilopay,
                FechaInicio = NowUtc.AddDays(-1)
            });

            await context.SaveChangesAsync();

            var service = CreateService(context);
            var report = await service.RunAsync();

            var suscripcion = await context.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoSuscripcion.Activa, suscripcion.Estado);
            Assert.Equal(plan.Id, suscripcion.PlanId);
            Assert.Equal(1, report.OrphanPaymentsRepaired);
            Assert.Equal(0, report.OrphanPaymentsAlerted);

            var repairLog = await context.PlatformAuditLogs
                .SingleAsync(log => log.Action == PlatformAuditActions.BillingAutoRepairApplied);
            Assert.Equal(tenantId, repairLog.TenantId);
            Assert.Equal(payment.Id.ToString(), repairLog.EntityId);

            Assert.Equal(1, await context.PlatformAuditLogs
                .CountAsync(log => log.Action == PlatformAuditActions.BillingReconciliationCompleted));
        }

        [Fact]
        public async Task Run_ConfirmedPaymentOnCancelledSubscription_AlertsWithoutResurrecting()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(2, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            SeedPayment(context, tenantId, plan.Id, data.RecurringPlanId, EstadoPagoProveedor.Confirmado, NowUtc.AddHours(-4));

            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Cancelada,
                Proveedor = PaymentProviderType.Tilopay,
                FechaInicio = NowUtc.AddMonths(-2),
                FechaCancelacionUtc = NowUtc.AddDays(-10),
                FechaFin = NowUtc.AddDays(-10)
            });

            await context.SaveChangesAsync();

            var report = await CreateService(context).RunAsync();

            var suscripcion = await context.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoSuscripcion.Cancelada, suscripcion.Estado);
            Assert.Equal(0, report.OrphanPaymentsRepaired);
            Assert.Equal(1, report.OrphanPaymentsAlerted);
            Assert.Equal(1, await context.PlatformAuditLogs
                .CountAsync(log => log.Action == PlatformAuditActions.BillingReconciliationAlert));
        }

        [Fact]
        public async Task Run_HealthyActiveSubscription_ProducesNoFindings()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(3, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            SeedPayment(context, tenantId, plan.Id, data.RecurringPlanId, EstadoPagoProveedor.Confirmado, NowUtc.AddHours(-4));

            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = data.RecurringPlanId,
                FechaInicio = NowUtc.AddHours(-4),
                FechaFin = NowUtc.AddMonths(1),
                FechaProximoCobroUtc = NowUtc.AddMonths(1),
                FechaUltimoPagoUtc = NowUtc.AddHours(-4)
            });

            await context.SaveChangesAsync();

            var report = await CreateService(context).RunAsync();

            Assert.False(report.HasFindings);
            Assert.Equal(0, await context.PlatformAuditLogs
                .CountAsync(log => log.Action != PlatformAuditActions.BillingReconciliationCompleted));
        }

        [Fact]
        public async Task Run_OverdueRenewal_AlertsWithoutModifyingSubscription()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(1, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            var originalNextBilling = NowUtc.AddDays(-3);
            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = data.RecurringPlanId,
                FechaInicio = NowUtc.AddMonths(-1),
                FechaFin = originalNextBilling,
                FechaProximoCobroUtc = originalNextBilling
            });

            await context.SaveChangesAsync();

            var report = await CreateService(context).RunAsync();

            // La renovación vencida es AMBIGUA (no sabemos si TiloPay cobró): solo alerta.
            var suscripcion = await context.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoSuscripcion.Activa, suscripcion.Estado);
            Assert.Equal(originalNextBilling, suscripcion.FechaProximoCobroUtc);
            Assert.Equal(1, report.OverdueRenewalsAlerted);
            Assert.Equal(1, await context.PlatformAuditLogs
                .CountAsync(log => log.Action == PlatformAuditActions.BillingReconciliationAlert));
        }

        [Fact]
        public async Task Run_StalePending_ExpiresOnlyOldAttempts()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(1, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            var stale = SeedPayment(context, tenantId, plan.Id, data.RecurringPlanId, EstadoPagoProveedor.Pendiente, confirmadoUtc: null);
            stale.FechaCreacionUtc = NowUtc.AddDays(-10);

            var fresh = SeedPayment(context, tenantId, plan.Id, data.RecurringPlanId, EstadoPagoProveedor.Pendiente, confirmadoUtc: null);
            fresh.FechaCreacionUtc = NowUtc.AddDays(-1);

            await context.SaveChangesAsync();

            var report = await CreateService(context).RunAsync();

            var attempts = await context.PagosSuscripcion.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(EstadoPagoProveedor.Expirado, attempts.Single(p => p.Id == stale.Id).Estado);
            Assert.Equal("EXPIRED_STALE", attempts.Single(p => p.Id == stale.Id).ProviderResultCode);
            Assert.Equal(EstadoPagoProveedor.Pendiente, attempts.Single(p => p.Id == fresh.Id).Estado);
            Assert.Equal(1, report.StalePendingsExpired);
            Assert.Equal(1, await context.PlatformAuditLogs
                .CountAsync(log => log.Action == PlatformAuditActions.BillingReconciliationCleanup));
        }

        [Fact]
        public async Task Run_StaleManualReview_AlertsButNeverTouchesThePayment()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(1, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            var review = SeedPayment(context, tenantId, plan.Id, data.RecurringPlanId, EstadoPagoProveedor.ManualReview, confirmadoUtc: null);
            review.FechaCreacionUtc = NowUtc.AddDays(-3);
            review.FechaActualizacionUtc = NowUtc.AddDays(-3);

            await context.SaveChangesAsync();

            var report = await CreateService(context).RunAsync();

            var payment = await context.PagosSuscripcion.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoPagoProveedor.ManualReview, payment.Estado);
            Assert.Equal(1, report.StaleManualReviewsAlerted);
        }

        [Fact]
        public async Task Run_StuckEvent_Alerts()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.EventosPago.Add(new EventoPago
            {
                Id = Guid.NewGuid(),
                Proveedor = PaymentProviderType.Tilopay,
                ProveedorEventId = "evt-stuck-1",
                Tipo = "repeat_payment_success",
                EstadoProcesamiento = "Error",
                Error = "SQL timeout simulado",
                Payload = "{}",
                Procesado = false,
                FechaRecepcionUtc = NowUtc.AddHours(-3)
            });

            await context.SaveChangesAsync();

            var report = await CreateService(context).RunAsync();

            Assert.Equal(1, report.StuckEventsAlerted);
        }

        [Fact]
        public async Task Run_Twice_CooldownPreventsDuplicateAlerts()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(1, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = data.RecurringPlanId,
                FechaInicio = NowUtc.AddMonths(-1),
                FechaFin = NowUtc.AddDays(-3),
                FechaProximoCobroUtc = NowUtc.AddDays(-3)
            });

            await context.SaveChangesAsync();

            var service = CreateService(context);
            var first = await service.RunAsync();
            var second = await service.RunAsync();

            Assert.Equal(1, first.OverdueRenewalsAlerted);
            Assert.Equal(0, second.OverdueRenewalsAlerted);
            Assert.Equal(1, second.AlertsSuppressedByCooldown);
            Assert.Equal(1, await context.PlatformAuditLogs
                .CountAsync(log => log.Action == PlatformAuditActions.BillingReconciliationAlert));
        }

        [Fact]
        public async Task Run_AutoRepairDisabled_OrphanPaymentAlertsInstead()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var data = CalculatorCatalog.Find(1, BillingCycle.Monthly);
            var plan = SeedTenantAndPlan(context, tenantId, data);

            SeedPayment(context, tenantId, plan.Id, data.RecurringPlanId, EstadoPagoProveedor.Confirmado, NowUtc.AddHours(-6));
            await context.SaveChangesAsync();

            var report = await CreateService(context, options => options.AutoRepairEnabled = false).RunAsync();

            Assert.Equal(0, report.OrphanPaymentsRepaired);
            Assert.Equal(1, report.OrphanPaymentsAlerted);
            Assert.Empty(await context.Suscripciones.IgnoreQueryFilters().ToListAsync());
        }

        // ── Helpers ──

        private static Plan SeedTenantAndPlan(ApplicationDbContext context, Guid tenantId, CalculatorPlanData data)
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Recon", Activo = true });
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = data.Code,
                Nombre = $"LuxuryCloud {data.Workers} {data.Cycle}",
                PrecioMensual = data.Charge,
                MonthlyEquivalentAmount = data.MonthlyEquivalent,
                BillingCycle = data.Cycle,
                Moneda = "CRC",
                MaxFuncionarios = data.Workers,
                Activo = true
            };
            context.Planes.Add(plan);
            return plan;
        }

        private static PagoSuscripcion SeedPayment(
            ApplicationDbContext context,
            Guid tenantId,
            Guid planId,
            int recurringPlanId,
            EstadoPagoProveedor estado,
            DateTime? confirmadoUtc)
        {
            var payment = new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = estado,
                ReferenciaInterna = $"LXA-{Guid.NewGuid():N}"[..20],
                ProviderReference = Guid.NewGuid().ToString("N"),
                ProviderTransactionId = estado == EstadoPagoProveedor.Confirmado ? $"TX-{Guid.NewGuid():N}"[..16] : null,
                ProviderSubscriberId = $"sub-{Guid.NewGuid():N}"[..16],
                TilopayRecurringPlanId = recurringPlanId,
                ClienteEmail = "owner@test.local",
                Monto = 8000m,
                Moneda = "CRC",
                FechaCreacionUtc = confirmadoUtc?.AddMinutes(-5) ?? NowUtc.AddHours(-1),
                FechaConfirmacionUtc = confirmadoUtc,
                FechaActualizacionUtc = confirmadoUtc ?? NowUtc.AddHours(-1)
            };

            context.PagosSuscripcion.Add(payment);
            return payment;
        }

        private static BillingReconciliationService CreateService(
            ApplicationDbContext context,
            Action<BillingReconciliationOptions>? configure = null)
        {
            var repeatOptions = CalculatorCatalog.BuildRepeatOptions();
            var options = new BillingReconciliationOptions();
            configure?.Invoke(options);

            var cache = new MemoryCache(new MemoryCacheOptions());
            var subscriptionService = new SuscripcionService(
                context,
                cache,
                new TenantCommercialAccessCache(cache),
                new FixedBusinessDateTimeProvider(),
                Options.Create(repeatOptions),
                NullLogger<SuscripcionService>.Instance);

            return new BillingReconciliationService(
                context,
                subscriptionService,
                new TenantExecutionContextAccessor(),
                new FixedBusinessDateTimeProvider(),
                Options.Create(repeatOptions),
                Options.Create(options),
                NullLogger<BillingReconciliationService>.Instance);
        }

        private static (ApplicationDbContext Context, IDisposable Connection) CreateSystemContext()
        {
            var tenantProvider = new TestTenantProvider();
            return TestDbContextFactory.CreateSqliteContext(tenantProvider);
        }
    }
}
