using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.TenantIsolation
{
    /// <summary>
    /// Valida la lógica de negocio de ActivarAddonWhatsAppRecurrenteAsync bajo escenarios de webhook
    /// (sin usuario autenticado). Los block predicates de SQL Server RLS no aplican en SQLite,
    /// por lo que estos tests cubren la capa de lógica y EF Core guards.
    /// La corrección del SESSION_CONTEXT (BeginScope en ApproveRecurringPaymentAsync) asegura que
    /// el BLOCK PREDICATE de TenantWhatsAppSettings se satisfaga en producción.
    /// </summary>
    public class RecurringWhatsAppAddonActivationTests
    {
        // ─── WA400 / WA800 / WA1200 con límites correctos ────────────────────────

        [Theory]
        [InlineData(PlanCodes.WhatsApp400, 400, 15)]
        [InlineData(PlanCodes.WhatsApp800, 800, 30)]
        [InlineData(PlanCodes.WhatsApp1200, 1200, 45)]
        public async Task ActivarRecurrente_CreatesAddonWithCorrectLimits(
            string addonCode, int expectedMonthlyLimit, int expectedDailyLimit)
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, addonCode, expectedMonthlyLimit);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(
                tenantId, plan, tilopayRecurringPlanId: 5831, providerSubscriberId: "sub-001",
                providerTransactionId: "5126802");

            var addon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(addon);
            Assert.Equal(EstadoSuscripcion.Activa, addon!.Estado);
            Assert.Equal(addonCode, addon.AddonCode);
            Assert.Equal(expectedMonthlyLimit, addon.MonthlyMessageLimit);

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(expectedDailyLimit, settings!.DailyMessageLimit);
        }

        // ─── TenantId correcto en TenantWhatsAppSettings ─────────────────────────

        [Fact]
        public async Task ActivarRecurrente_CreatesTenantWhatsAppSettingsWithCorrectTenantId()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(
                tenantId, plan, tilopayRecurringPlanId: 5831, providerSubscriberId: "sub-001",
                providerTransactionId: "5126802");

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(tenantId, settings!.TenantId);
            Assert.True(settings.IsEnabled);
            Assert.True(settings.SendConfirmationOnCreate);
            Assert.True(settings.SendReminderThreeHoursBefore);
        }

        // ─── Settings existentes: preserva preferencias del usuario ──────────────

        [Fact]
        public async Task ActivarRecurrente_ExistingSettings_PreservesUserPreferences()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            // Seed existing settings with custom timezone
            context.TenantWhatsAppSettings.Add(new TenantWhatsAppSettings
            {
                TenantId = tenantId,
                IsEnabled = true,
                SendConfirmationOnCreate = false,
                SendReminderThreeHoursBefore = false,
                TimeZoneId = "America/New_York",
                DailyMessageLimit = 5,
                Notes = "custom-note"
            });
            await context.SaveChangesAsync();

            var svc = CreateSuscripcionService(context);
            // Calling again with same plan: existing addon → isSameAddonPlan=false (no previous addon)
            // so settings.IsEnabled/Send* get overridden to true. TimeZoneId is preserved.
            await svc.ActivarAddonWhatsAppRecurrenteAsync(
                tenantId, plan, tilopayRecurringPlanId: 5831, providerSubscriberId: "sub-001",
                providerTransactionId: "5126802");

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(tenantId, settings!.TenantId);
            // TimeZoneId should be preserved (it was "America/New_York", not empty)
            Assert.Equal("America/New_York", settings.TimeZoneId);
            // DailyMessageLimit gets updated to match the addon code
            Assert.Equal(15, settings.DailyMessageLimit); // WA400 = 15
        }

        // ─── Sin usuario autenticado: simula contexto de webhook ─────────────────

        [Fact]
        public async Task ActivarRecurrente_WithoutAuthenticatedUser_CreatesAddonWithCorrectTenantId()
        {
            var tenantId = Guid.NewGuid();

            // Simulamos contexto de webhook: sin usuario autenticado → HasTenant() = false.
            // EF Core usa EnsureSystemTenant que permite el INSERT cuando entity.TenantId != Guid.Empty.
            // En SQL Server, ApproveRecurringPaymentAsync llama BeginScope(tenantId) para que
            // SESSION_CONTEXT quede correcto y el BLOCK PREDICATE de TenantWhatsAppSettings no rechace.
            // SQLite no tiene block predicates, por lo que este test valida la capa EF Core.
            var noAuthProvider = new TestTenantProvider { TenantId = Guid.Empty };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(noAuthProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            // Tenant (no es ITenantEntity) → no aplica guard, INSERT directo.
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Barberia Luxury Cloud" });
            await context.SaveChangesAsync();

            // Plan (no es ITenantEntity) → INSERT directo.
            var waPlan = new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = PlanCodes.WhatsApp400,
                Nombre = "WhatsApp 400",
                Moneda = "CRC",
                PrecioMensual = 6000m,
                LimiteMensajesMensual = 400,
                Activo = true
            };
            context.Planes.Add(waPlan);
            await context.SaveChangesAsync();

            // Ejecutar sin usuario autenticado (HasTenant()=false):
            // EnsureSystemTenant verifica que entity.TenantId != Guid.Empty → pasa.
            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(
                tenantId, waPlan, tilopayRecurringPlanId: 5831, providerSubscriberId: "sub-001",
                providerTransactionId: "5126802");

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(tenantId, settings!.TenantId);
            Assert.True(settings.IsEnabled);

            var addon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(addon);
            Assert.Equal(tenantId, addon!.TenantId);
            Assert.Equal(EstadoSuscripcion.Activa, addon.Estado);
        }

        // ─── Idempotencia: duplicate repeat_payment_success ──────────────────────

        [Fact]
        public async Task ActivarRecurrente_SecondCallSamePlan_DoesNotDuplicateAddon()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(
                tenantId, plan, 5831, "sub-001", "txn-001");

            await svc.ActivarAddonWhatsAppRecurrenteAsync(
                tenantId, plan, 5831, "sub-001", "txn-002");

            var addons = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .ToListAsync();

            Assert.Single(addons);
            Assert.Equal(EstadoSuscripcion.Activa, addons[0].Estado);
        }

        [Fact]
        public async Task ActivarRecurrente_SecondCallSamePlan_DoesNotDuplicateTenantWhatsAppSettings()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-001");
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-002");

            var settingsList = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId)
                .ToListAsync();

            Assert.Single(settingsList);
        }

        [Fact]
        public async Task ActivarRecurrente_SecondCallSamePlan_ExtendsBillingPeriod()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-001");

            var firstAddon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(firstAddon);
            var firstFechaFin = firstAddon!.FechaFin;

            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-002");

            var secondAddon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(secondAddon);
            // La segunda activación del mismo plan extiende la vigencia (shouldExtendExistingPeriod=true).
            Assert.True(secondAddon!.FechaFin > firstFechaFin,
                "La segunda activación del mismo add-on activo debe extender la vigencia.");
        }

        // ─── repeat_registration después de payment_success no degrada el addon ──

        [Fact]
        public async Task ActivarRecurrente_AfterActivation_AddonRemainsActive()
        {
            // Simula: repeat_payment_success activa el addon, luego se llama de nuevo
            // (como si llegara otro webhook de registro). El addon debe permanecer Activa.
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-001");

            // Segunda llamada (simula re-activación desde otro webhook)
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-002");

            var addon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(addon);
            Assert.Equal(EstadoSuscripcion.Activa, addon!.Estado);
        }

        // ─── Plan base no se modifica ─────────────────────────────────────────────

        [Fact]
        public async Task ActivarRecurrente_DoesNotTouchBasePlan()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            var basePlanId = await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-001");

            var baseSub = await context.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.PlanId == basePlanId);

            Assert.NotNull(baseSub);
            Assert.Equal(EstadoSuscripcion.Activa, baseSub!.Estado);
            Assert.Equal(PlanCodes.Business, baseSub.CodigoPlan);
        }

        [Fact]
        public async Task ActivarRecurrente_DoesNotChangeMaxFuncionarios()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId, maxFuncionarios: 10);
            var plan = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan, 5831, "sub-001", "txn-001");

            var basePlan = await context.Suscripciones
                .IgnoreQueryFilters()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId && s.CodigoPlan == PlanCodes.Business)
                .Select(s => s.Plan)
                .FirstOrDefaultAsync();

            Assert.NotNull(basePlan);
            Assert.Equal(10, basePlan!.MaxFuncionarios);
        }

        // ─── WA400 → WA800 upgrade: un solo addon, límites del nuevo plan ────────

        [Fact]
        public async Task ActivarRecurrente_UpgradeFromWA400ToWA800_ReplacesAddon()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBusinessSubscriptionAsync(context, tenantId);
            var plan400 = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);
            var plan800 = await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp800, 800);

            var svc = CreateSuscripcionService(context);
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan400, 5831, "sub-001", "txn-001");
            await svc.ActivarAddonWhatsAppRecurrenteAsync(tenantId, plan800, 5832, "sub-002", "txn-002");

            var addons = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .ToListAsync();

            Assert.Single(addons);
            Assert.Equal(PlanCodes.WhatsApp800, addons[0].AddonCode);
            Assert.Equal(800, addons[0].MonthlyMessageLimit);
            Assert.Equal(EstadoSuscripcion.Activa, addons[0].Estado);

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(30, settings!.DailyMessageLimit); // WA800 = 30
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static SuscripcionService CreateSuscripcionService(
            ProyectoIdentity.Datos.ApplicationDbContext context)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var accessCache = new TenantCommercialAccessCache(cache);
            var businessDateTimeProvider = new FixedBusinessDateTimeProvider();
            return new SuscripcionService(
                context,
                cache,
                accessCache,
                businessDateTimeProvider,
                Options.Create(new TilopayRepeatOptions()),
                NullLogger<SuscripcionService>.Instance);
        }

        private static async Task SeedTenantAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Guid tenantId)
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Recurring WA" });
            await context.SaveChangesAsync();
        }

        private static async Task<Guid> SeedActiveBusinessSubscriptionAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Guid tenantId,
            int maxFuncionarios = 10)
        {
            var planId = Guid.NewGuid();
            var nowUtc = GetFixedNowUtc();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.Business,
                Nombre = "Business",
                Moneda = "CRC",
                PrecioMensual = 20000m,
                MaxFuncionarios = maxFuncionarios,
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
                FechaInicio = nowUtc.AddDays(-3),
                FechaFin = nowUtc.AddDays(27),
                FechaProximoCobroUtc = nowUtc.AddDays(27),
                FechaUltimaActualizacionUtc = nowUtc
            });
            await context.SaveChangesAsync();
            return planId;
        }

        private static async Task<Plan> SeedWhatsAppPlanAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string addonCode,
            int monthlyLimit)
        {
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = addonCode,
                Nombre = $"WhatsApp {addonCode}",
                Moneda = "CRC",
                PrecioMensual = 6000m,
                LimiteMensajesMensual = monthlyLimit,
                Activo = true
            };
            context.Planes.Add(plan);
            await context.SaveChangesAsync();
            return plan;
        }

        private static DateTime GetFixedNowUtc() =>
            new DateTimeOffset(new DateTime(2026, 6, 10, 12, 0, 0), TimeSpan.Zero).UtcDateTime;
    }
}
