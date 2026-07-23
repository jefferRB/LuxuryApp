using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Billing;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// Ciclo de vida de cancelación del ADD-ON de WhatsApp (Fase 4): Strategy B en cambio de paquete,
    /// cancelación saliente verificada (un 200 nunca basta), backoff cuando el proveedor no confirma,
    /// modo manual-safe con API apagado, cascada del plan base, recuperación de incidentes del add-on
    /// SIN contaminar el base, y métricas separadas en Mission Control. NUNCA se toca el plan base.
    /// </summary>
    public class WhatsAppAddonCancellationLifecycleTests
    {
        private const int Wa400PlanId = 5831;
        private const int Wa800PlanId = 5832;

        // ── R1: Strategy B — cambiar de paquete deja el suscriptor anterior pendiente ──

        [Fact]
        public async Task ActivarRecurrente_UpgradeWA400ToWA800_StashesOldSubscriberForCancellation()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider { TenantId = tenantId });
            using var _ = context;
            using var __ = connection;

            await SeedTenantAndBaseAsync(context, tenantId);
            var plan400 = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);
            var plan800 = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp800, 800);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan400, Wa400PlanId, "sub-001", "txn-1");
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan800, Wa800PlanId, "sub-002", "txn-2");

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);

            Assert.Equal(PlanCodes.WhatsApp800, addon.AddonCode);
            Assert.Equal("sub-002", addon.ProviderSubscriptionId);
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, addon.ProviderCancellation);
            Assert.Equal("sub-001", addon.PendingCancellationProviderSubscriptionId);
            Assert.Equal(Wa400PlanId, addon.PendingCancellationTilopayRecurringPlanId);
        }

        [Fact]
        public async Task ActivarRecurrente_SamePlanRenewal_DoesNotStashPendingCancellation()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider { TenantId = tenantId });
            using var _ = context;
            using var __ = connection;

            await SeedTenantAndBaseAsync(context, tenantId);
            var plan400 = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan400, Wa400PlanId, "sub-001", "txn-1");
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan400, Wa400PlanId, "sub-001", "txn-2");

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);

            Assert.Equal(ProviderCancellationState.NotRequired, addon.ProviderCancellation);
            Assert.Null(addon.PendingCancellationProviderSubscriptionId);
        }

        // ── R4: cancelación saliente verificada (un 200 nunca basta) ──

        [Fact]
        public async Task TryCancelPending_VerifiedInactive_MarksCancelledAndClearsPending()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            var admin = new FakeAddonAdmin();
            admin.AddSubscriber(Wa400PlanId, "sub-001"); // el viejo existe y está activo
            await SeedAddonAsync(context, tenantId,
                current: "sub-002", currentPlanId: Wa800PlanId,
                pending: "sub-001", pendingPlanId: Wa400PlanId);

            var manager = CreateManager(context, accessor, admin);
            var result = await manager.TryCancelPendingAddonSubscriberAsync(tenantId);

            Assert.True(result.ProviderCalled);
            Assert.True(result.Cancelled);
            Assert.Contains("sub-001", admin.DeletedSubscriberIds);

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.Equal(ProviderCancellationState.Cancelled, addon.ProviderCancellation);
            Assert.Null(addon.PendingCancellationProviderSubscriptionId);
            Assert.NotNull(addon.ProviderCancelledAtUtc);
            // El suscriptor ACTUAL (nuevo) no se tocó.
            Assert.Equal("sub-002", addon.ProviderSubscriptionId);
            Assert.Equal(EstadoSuscripcion.Activa, addon.Estado);

            Assert.Equal(1, await CountAuditAsync(context, PlatformAuditActions.AddonUpgradeOldSubscriberCancellationCompleted));
        }

        [Fact]
        public async Task TryCancelPending_ProviderStillActiveAfter200_KeepsPendingAndBacksOff()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            var admin = new FakeAddonAdmin { ActuallyRemoveOnDelete = false }; // 200 pero NO cancela de verdad
            admin.AddSubscriber(Wa400PlanId, "sub-001");
            await SeedAddonAsync(context, tenantId,
                current: "sub-002", currentPlanId: Wa800PlanId,
                pending: "sub-001", pendingPlanId: Wa400PlanId);

            var manager = CreateManager(context, accessor, admin);
            var result = await manager.TryCancelPendingAddonSubscriberAsync(tenantId);

            Assert.True(result.ProviderCalled);
            Assert.False(result.Cancelled);

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, addon.ProviderCancellation);
            Assert.Equal("sub-001", addon.PendingCancellationProviderSubscriptionId);
            Assert.Equal(1, addon.ProviderCancellationAttemptCount);
            Assert.NotNull(addon.ProviderCancellationNextRetryUtc);

            Assert.Equal(1, await CountAuditAsync(context, PlatformAuditActions.AddonUpgradeOldSubscriberCancellationFailed));
        }

        [Fact]
        public async Task TryCancelPending_AdminDisabled_NotCalled_DoesNotConsumeBudget()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            var admin = new FakeAddonAdmin { IsEnabled = false };
            await SeedAddonAsync(context, tenantId,
                current: "sub-002", currentPlanId: Wa800PlanId,
                pending: "sub-001", pendingPlanId: Wa400PlanId);

            var manager = CreateManager(context, accessor, admin);
            var result = await manager.TryCancelPendingAddonSubscriberAsync(tenantId);

            Assert.False(result.ProviderCalled);

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, addon.ProviderCancellation);
            Assert.Equal(0, addon.ProviderCancellationAttemptCount);
        }

        [Fact]
        public async Task RequestAddonCancellation_VerifiedBaja_SchedulesCancelAtPeriodEnd_KeepsAccessUntilEnd()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            var admin = new FakeAddonAdmin();
            admin.AddSubscriber(Wa800PlanId, "sub-002");
            await SeedAddonAsync(context, tenantId, current: "sub-002", currentPlanId: Wa800PlanId);

            var manager = CreateManager(context, accessor, admin);
            var result = await manager.RequestAddonCancellationAtPeriodEndAsync(tenantId, "user-1", "user@test.cr", "no lo uso");

            Assert.True(result.Succeeded);
            Assert.Contains("sub-002", admin.DeletedSubscriberIds);

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.True(addon.CancelAtPeriodEnd);
            Assert.Equal(ProviderCancellationState.Cancelled, addon.ProviderCancellation);
            // El uso sigue hasta FechaFin (no se corta de una): sigue Activa.
            Assert.Equal(EstadoSuscripcion.Activa, addon.Estado);
        }

        // ── R2: cascada base → add-on (pendiente aun con API apagado) ──

        [Fact]
        public async Task CascadeCancellation_AdminDisabled_MarksAddonPendingAndCancelAtPeriodEnd()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            var admin = new FakeAddonAdmin { IsEnabled = false };
            await SeedAddonAsync(context, tenantId, current: "sub-002", currentPlanId: Wa800PlanId);

            var manager = CreateManager(context, accessor, admin);
            await manager.ScheduleAddonCancellationForBaseCancellationAsync(tenantId, "user-1", "canceló el SaaS", immediate: false);

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.True(addon.CancelAtPeriodEnd);
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, addon.ProviderCancellation);
            Assert.Equal("sub-002", addon.PendingCancellationProviderSubscriptionId);
            Assert.Equal(EstadoSuscripcion.Activa, addon.Estado); // no se corta hasta FechaFin
        }

        [Fact]
        public async Task CascadeCancellation_Immediate_CutsAddonAndCancelsSubscriber()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            var admin = new FakeAddonAdmin();
            admin.AddSubscriber(Wa800PlanId, "sub-002");
            await SeedAddonAsync(context, tenantId, current: "sub-002", currentPlanId: Wa800PlanId);

            var manager = CreateManager(context, accessor, admin);
            await manager.ScheduleAddonCancellationForBaseCancellationAsync(tenantId, "platform", "cancelación inmediata", immediate: true);

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.Equal(EstadoSuscripcion.Cancelada, addon.Estado);
            Assert.Equal(ProviderCancellationState.Cancelled, addon.ProviderCancellation);
            Assert.Contains("sub-002", admin.DeletedSubscriberIds);
        }

        // ── R7: recuperación de incidentes del add-on SIN contaminar el base ──

        [Fact]
        public async Task RecoveryAddon_FailureOpensAddonIncident_ThenSuccessResolves_BaseUntouched()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection, accessor) = CreateContext(tenantId);
            using var _ = context;
            using var __ = connection;

            await SeedTenantAndBaseAsync(context, tenantId);
            await SeedAddonAsync(context, tenantId, current: "sub-001", currentPlanId: Wa400PlanId);

            var recovery = CreateRecoveryService(context, accessor);

            await recovery.RegisterFailedAddonPaymentAsync(tenantId, Wa400PlanId, "sub-001", "2", "declined");

            var addonIncidents = await context.SubscriptionPaymentIncidents.IgnoreQueryFilters()
                .Where(i => i.TenantId == tenantId && i.Scope == PaymentIncidentScope.WhatsAppAddon)
                .ToListAsync();
            Assert.Single(addonIncidents);
            Assert.Equal(PaymentIncidentStatus.Open, addonIncidents[0].Status);
            Assert.NotNull(addonIncidents[0].AddonId);

            // El base NO tiene incidentes: el fallo del add-on no lo contamina.
            var baseIncidents = await context.SubscriptionPaymentIncidents.IgnoreQueryFilters()
                .CountAsync(i => i.TenantId == tenantId && i.Scope == PaymentIncidentScope.BasePlan);
            Assert.Equal(0, baseIncidents);

            await recovery.ResolveAddonOnSuccessAsync(tenantId, Wa400PlanId);

            var resolved = await context.SubscriptionPaymentIncidents.IgnoreQueryFilters()
                .SingleAsync(i => i.TenantId == tenantId && i.Scope == PaymentIncidentScope.WhatsAppAddon);
            Assert.Equal(PaymentIncidentStatus.Resolved, resolved.Status);
        }

        [Fact]
        public async Task RegistrarPagoFallidoAddon_DoesNotMarkBaseMorosa()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider { TenantId = tenantId });
            using var _ = context;
            using var __ = connection;

            var basePlanId = await SeedTenantAndBaseAsync(context, tenantId);
            var plan400 = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan400, Wa400PlanId, "sub-001", "txn-1");
            await svc.RegistrarPagoFallidoAddonAsync(tenantId, PlanCodes.WhatsApp400, "sub-001", "txn-2", "declined");

            var addon = await context.TenantSubscriptionAddons.IgnoreQueryFilters()
                .SingleAsync(a => a.TenantId == tenantId);
            Assert.Equal(EstadoSuscripcion.Morosa, addon.Estado);

            var baseSub = await context.Suscripciones.IgnoreQueryFilters()
                .SingleAsync(s => s.TenantId == tenantId && s.PlanId == basePlanId);
            Assert.Equal(EstadoSuscripcion.Activa, baseSub.Estado);
        }

        // ── R5/R6: Mission Control (BillingHealth) separa add-ons ──

        [Fact]
        public async Task BillingHealth_AddonActiveWithoutBase_ShownInSeparateSection()
        {
            var tenantId = Guid.NewGuid();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider { TenantId = tenantId });
            using var _ = context;
            using var __ = connection;

            // Base VENCIDA + add-on activo con cancelación de suscriptor pendiente (dinero en riesgo).
            await SeedExpiredBaseAsync(context, tenantId);
            await SeedAddonAsync(context, tenantId,
                current: "sub-002", currentPlanId: Wa800PlanId,
                pending: "sub-001", pendingPlanId: Wa400PlanId);

            var svc = CreateSuscripcionService(context);
            var health = new BillingHealthService(context, svc);
            var snapshot = await health.BuildAsync();

            Assert.Equal(1, snapshot.ActiveWhatsAppAddons);
            Assert.Equal(1, snapshot.WhatsAppAddonsWithoutActiveBase);
            Assert.Equal(1, snapshot.WhatsAppAddonsPendingProviderCancellation);
            Assert.Equal(1, snapshot.WhatsAppAddonMoneyRiskCount);
            Assert.Equal(0, snapshot.WhatsAppAddonsDoubleActiveTenants);
        }

        // ── Helpers ──

        private static (ApplicationDbContext, System.Data.Common.DbConnection, TenantExecutionContextAccessor) CreateContext(Guid tenantId)
        {
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider { TenantId = tenantId });
            return (context, connection, new TenantExecutionContextAccessor());
        }

        private static SuscripcionService CreateSuscripcionService(ApplicationDbContext context)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new SuscripcionService(
                context, cache, new TenantCommercialAccessCache(cache),
                new FixedBusinessDateTimeProvider(),
                Options.Create(new TilopayRepeatOptions()),
                NullLogger<SuscripcionService>.Instance);
        }

        private static AddonSubscriptionManager CreateManager(
            ApplicationDbContext context, TenantExecutionContextAccessor accessor, FakeAddonAdmin admin)
        {
            var options = Options.Create(new OpcionesTilopayRepeatAdmin
            {
                Enabled = true,
                AutoCancelOldSubscriberOnUpgrade = true
            });
            return new AddonSubscriptionManager(
                context, admin, accessor, new FixedBusinessDateTimeProvider(),
                options, NullLogger<AddonSubscriptionManager>.Instance);
        }

        private static PaymentRecoveryService CreateRecoveryService(
            ApplicationDbContext context, TenantExecutionContextAccessor accessor)
        {
            var recoveryOptions = Options.Create(new BillingPaymentRecoveryOptions
            {
                Enabled = true,
                AutoSuspendAfterGrace = false,
                SendEmailNotifications = false,
                GraceDays = 5
            });
            return new PaymentRecoveryService(
                context, accessor, new FixedBusinessDateTimeProvider(),
                recoveryOptions, NullLogger<PaymentRecoveryService>.Instance);
        }

        private static async Task<Guid> SeedTenantAndBaseAsync(ApplicationDbContext context, Guid tenantId)
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Addon", Activo = true });
            var planId = Guid.NewGuid();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.Business,
                Nombre = "Business",
                Moneda = "CRC",
                PrecioMensual = 20000m,
                MaxFuncionarios = 7,
                Activo = true
            });
            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                CodigoPlan = PlanCodes.Business,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                FechaInicio = DateTime.UtcNow.AddDays(-3),
                FechaFin = DateTime.UtcNow.AddDays(27),
                FechaProximoCobroUtc = DateTime.UtcNow.AddDays(27),
                FechaUltimaActualizacionUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return planId;
        }

        private static async Task SeedExpiredBaseAsync(ApplicationDbContext context, Guid tenantId)
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Addon Sin Base", Activo = true });
            var planId = Guid.NewGuid();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.Business,
                Nombre = "Business",
                Moneda = "CRC",
                PrecioMensual = 20000m,
                MaxFuncionarios = 7,
                Activo = true
            });
            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                CodigoPlan = PlanCodes.Business,
                Estado = EstadoSuscripcion.Vencida,
                Proveedor = PaymentProviderType.Tilopay,
                FechaInicio = DateTime.UtcNow.AddDays(-35),
                FechaFin = DateTime.UtcNow.AddDays(-5),
                FechaProximoCobroUtc = DateTime.UtcNow.AddDays(-5),
                FechaUltimaActualizacionUtc = DateTime.UtcNow.AddDays(-5)
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        private static async Task<Plan> SeedWhatsAppPlanAsync(ApplicationDbContext context, string code, int monthlyLimit)
        {
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = code,
                Nombre = $"WhatsApp {code}",
                Moneda = "CRC",
                PrecioMensual = 6000m,
                LimiteMensajesMensual = monthlyLimit,
                Activo = true
            };
            context.Planes.Add(plan);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return plan;
        }

        private static async Task SeedAddonAsync(
            ApplicationDbContext context,
            Guid tenantId,
            string current,
            int currentPlanId,
            string? pending = null,
            int? pendingPlanId = null)
        {
            var nowUtc = DateTime.UtcNow;

            // FK: el add-on referencia Tenant (cascade) y Plan (restrict). Garantizar ambos.
            if (!await context.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
            {
                context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Addon", Activo = true });
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            var addonCode = currentPlanId == Wa800PlanId ? PlanCodes.WhatsApp800 : PlanCodes.WhatsApp400;
            var addonPlanId = await context.Planes.IgnoreQueryFilters()
                .Where(p => p.Codigo == addonCode)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync();
            if (addonPlanId is null)
            {
                var plan = await SeedWhatsAppPlanAsync(context, addonCode, currentPlanId == Wa800PlanId ? 800 : 400);
                addonPlanId = plan.Id;
            }

            context.TenantSubscriptionAddons.Add(new TenantSubscriptionAddon
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = addonPlanId.Value,
                AddonCode = addonCode,
                Estado = EstadoSuscripcion.Activa,
                TilopayRecurringPlanId = currentPlanId,
                ProviderSubscriptionId = current,
                MonthlyMessageLimit = currentPlanId == Wa800PlanId ? 800 : 400,
                FechaInicio = nowUtc.AddDays(-2),
                FechaFin = nowUtc.AddDays(28),
                FechaProximoCobroUtc = nowUtc.AddDays(28),
                ProviderCancellation = pending is null
                    ? ProviderCancellationState.NotRequired
                    : ProviderCancellationState.PendingManualCancellation,
                PendingCancellationProviderSubscriptionId = pending,
                PendingCancellationTilopayRecurringPlanId = pendingPlanId,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        private static Task<int> CountAuditAsync(ApplicationDbContext context, string action) =>
            context.PlatformAuditLogs.CountAsync(l => l.Action == action);

        /// <summary>
        /// Fake del cliente admin de TiloPay con un store en memoria de suscriptores por plan.
        /// Delete/EditStatus(Deleted) los quita del store (baja verificable) salvo que
        /// <see cref="ActuallyRemoveOnDelete"/>=false (simula "200 pero sigue Activo").
        /// </summary>
        private sealed class FakeAddonAdmin : ITilopayRepeatAdminService
        {
            public bool IsEnabled { get; set; } = true;
            public bool ActuallyRemoveOnDelete { get; set; } = true;
            public List<string> DeletedSubscriberIds { get; } = new();
            private readonly Dictionary<int, List<TilopaySubscriber>> _byPlan = new();

            public void AddSubscriber(int planId, string subscriberId, string status = "Active")
            {
                if (!_byPlan.TryGetValue(planId, out var list))
                {
                    list = new List<TilopaySubscriber>();
                    _byPlan[planId] = list;
                }
                list.Add(new TilopaySubscriber { SubscriberId = subscriberId, Status = status });
            }

            public Task<IReadOnlyList<TilopaySubscriber>> GetSuscriptorRepeatAsync(int tilopayPlanId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<TilopaySubscriber>>(
                    _byPlan.TryGetValue(tilopayPlanId, out var list) ? list.ToList() : new List<TilopaySubscriber>());

            public Task<TilopayAdminOperationResult> DeleteSubscriberAsync(string subscriberId, CancellationToken cancellationToken = default)
            {
                DeletedSubscriberIds.Add(subscriberId);
                if (ActuallyRemoveOnDelete)
                {
                    RemoveEverywhere(subscriberId);
                }
                return Task.FromResult(TilopayAdminOperationResult.Ok("deleted"));
            }

            public Task<TilopayAdminOperationResult> EditSubscriberStatusAsync(string subscriberId, TilopaySubscriberStatus status, CancellationToken cancellationToken = default)
            {
                if (status == TilopaySubscriberStatus.Deleted && ActuallyRemoveOnDelete)
                {
                    RemoveEverywhere(subscriberId);
                }
                return Task.FromResult(TilopayAdminOperationResult.Ok("edited"));
            }

            private void RemoveEverywhere(string subscriberId)
            {
                foreach (var list in _byPlan.Values)
                {
                    list.RemoveAll(s => string.Equals(s.SubscriberId, subscriberId, StringComparison.OrdinalIgnoreCase));
                }
            }

            public Task<SubscriberResolutionResult> ResolveSubscriberAsync(int tilopayPlanId, string? email, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();

            public Task<TilopayAdminOperationResult> GetRecurrentUrlAsync(int tilopayPlanId, string? email, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();

            public Task<TargetSubscriberAssessment> AssessTargetSubscribersAsync(int tilopayPlanId, string? email, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();

            public Task<TilopayAdminOperationResult> PauseSubscriberAsync(string subscriberId, CancellationToken cancellationToken = default) =>
                Task.FromResult(TilopayAdminOperationResult.Ok("paused"));

            public Task<TilopayAdminOperationResult> ReactivateSubscriberAsync(string subscriberId, CancellationToken cancellationToken = default) =>
                Task.FromResult(TilopayAdminOperationResult.Ok("reactivated"));
        }
    }
}
