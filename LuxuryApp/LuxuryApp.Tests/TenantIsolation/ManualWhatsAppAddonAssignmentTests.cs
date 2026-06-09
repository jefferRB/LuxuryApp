using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class ManualWhatsAppAddonAssignmentTests
    {
        [Theory]
        [InlineData(PlanCodes.WhatsApp400, 400)]
        [InlineData(PlanCodes.WhatsApp800, 800)]
        [InlineData(PlanCodes.WhatsApp1200, 1200)]
        public async Task AssignManual_WA_CreatesActiveAddonWithCorrectMonthlyLimit(
            string addonCode,
            int expectedMonthlyLimit)
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, addonCode, expectedMonthlyLimit);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, addonCode, "platform-admin", "Alta manual inicial",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            var addon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(addon);
            Assert.Equal(EstadoSuscripcion.Activa, addon.Estado);
            Assert.Equal(addonCode, addon.AddonCode);
            Assert.Equal(expectedMonthlyLimit, addon.MonthlyMessageLimit);
            Assert.StartsWith($"MANUAL-{addonCode}-", addon.ProviderTransactionId);
            Assert.Null(addon.TilopayRecurringPlanId);
        }

        [Fact]
        public async Task AssignManual_DoesNotTouchBasePlan()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            var basePlanId = await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp400, "platform-admin", "Prueba",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: true);

            var baseSub = await context.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.PlanId == basePlanId);

            Assert.NotNull(baseSub);
            Assert.Equal(EstadoSuscripcion.Activa, baseSub.Estado);
            Assert.Equal(PlanCodes.Basic, baseSub.CodigoPlan);
        }

        [Fact]
        public async Task AssignManual_DoesNotChangeMaxFuncionarios()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId, maxFuncionarios: 5);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp800, 800);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp800, "platform-admin", "Test",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            var basePlan = await context.Suscripciones
                .IgnoreQueryFilters()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId && s.CodigoPlan == PlanCodes.Basic)
                .Select(s => s.Plan)
                .FirstOrDefaultAsync();

            Assert.NotNull(basePlan);
            Assert.Equal(5, basePlan!.MaxFuncionarios);
        }

        [Fact]
        public async Task AssignManual_EnablesTenantWhatsAppSettings()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp400, "platform-admin", "Habilitar WA",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: true);

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.True(settings!.IsEnabled);
            Assert.True(settings.SendConfirmationOnCreate);
            Assert.True(settings.SendReminderThreeHoursBefore);
        }

        [Fact]
        public async Task AssignManual_CanSendNotificationNoLongerReturnsNoActiveAddon()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var suscripcionSvc = CreateSuscripcionService(context);
            await suscripcionSvc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp400, "platform-admin", "Test",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            var settingsSvc = CreateSettingsService(context, tenantProvider, suscripcionSvc);
            var decision = await settingsSvc.CanSendNotificationAsync(
                tenantId, WhatsAppNotificationTypes.Confirmation);

            Assert.NotEqual(WhatsAppErrorCodes.NoActiveWhatsAppAddon, decision.ErrorCode);
        }

        [Fact]
        public async Task AssignManual_UpgradeFromWA400ToWA800_ReplacesAddon()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp800, 800);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp400, "platform-admin", "Alta inicial",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp800, "platform-admin", "Upgrade a 800",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: true);

            var addons = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .ToListAsync();

            Assert.Single(addons);
            Assert.Equal(PlanCodes.WhatsApp800, addons[0].AddonCode);
            Assert.Equal(EstadoSuscripcion.Activa, addons[0].Estado);
            Assert.Equal(800, addons[0].MonthlyMessageLimit);
        }

        [Fact]
        public async Task AssignNone_CancelsAddonOnly_BasePlanUnchanged()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            var basePlanId = await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp400, "platform-admin", "Alta",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, "NONE", "platform-admin", "Revocar paquete",
                sendConfirmationOnCreate: false, sendReminderThreeHoursBefore: false);

            var addon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(addon);
            Assert.Equal(EstadoSuscripcion.Cancelada, addon!.Estado);

            var baseSub = await context.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.PlanId == basePlanId);

            Assert.NotNull(baseSub);
            Assert.Equal(EstadoSuscripcion.Activa, baseSub!.Estado);

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.False(settings!.IsEnabled);
        }

        [Fact]
        public async Task AssignManual_WithExpiredBasePlan_AddonExistsAndBasePlanUnchanged()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            var basePlanId = await SeedExpiredBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp1200, 1200);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp1200, "platform-admin", "Asignar sin plan base activo",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            var addon = await context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.TenantId == tenantId);

            Assert.NotNull(addon);
            Assert.Equal(EstadoSuscripcion.Activa, addon!.Estado);

            var baseSub = await context.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.PlanId == basePlanId);

            Assert.NotNull(baseSub);
            Assert.Equal(EstadoSuscripcion.Vencida, baseSub!.Estado);
        }

        [Theory]
        [InlineData(PlanCodes.WhatsApp400, 15)]
        [InlineData(PlanCodes.WhatsApp800, 30)]
        [InlineData(PlanCodes.WhatsApp1200, 45)]
        public async Task AssignManual_DailyLimitMatchesAddonCode(string addonCode, int expectedDailyLimit)
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var monthlyLimit = addonCode switch
            {
                PlanCodes.WhatsApp400 => 400,
                PlanCodes.WhatsApp800 => 800,
                _ => 1200
            };

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, addonCode, monthlyLimit);

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, addonCode, "platform-admin", "DailyLimit test",
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(expectedDailyLimit, settings!.DailyMessageLimit);
        }

        [Fact]
        public async Task AssignManual_ObservationStoredInWhatsAppSettingsNotes()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            await SeedTenantAsync(context, tenantId);
            await SeedActiveBaseSubscriptionAsync(context, tenantId);
            await SeedWhatsAppPlanAsync(context, PlanCodes.WhatsApp400, 400);

            const string observation = "Asignado manualmente por solicitud del cliente vía soporte #4321.";

            var svc = CreateSuscripcionService(context);
            await svc.AssignManualWhatsAppAddonAsync(
                tenantId, PlanCodes.WhatsApp400, "platform-admin", observation,
                sendConfirmationOnCreate: true, sendReminderThreeHoursBefore: false);

            var settings = await context.TenantWhatsAppSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            Assert.NotNull(settings);
            Assert.Equal(observation, settings!.Notes);
        }

        // --- Helpers ---

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

        private static TenantWhatsAppSettingsService CreateSettingsService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            TestTenantProvider tenantProvider,
            SuscripcionService suscripcionService)
        {
            var businessDateTimeProvider = new FixedBusinessDateTimeProvider();
            return new TenantWhatsAppSettingsService(
                context,
                tenantProvider,
                new StaticOptionsMonitor<MetaWhatsAppOptions>(new MetaWhatsAppOptions { Enabled = true }),
                suscripcionService,
                businessDateTimeProvider,
                NullLogger<TenantWhatsAppSettingsService>.Instance);
        }

        private static async Task SeedTenantAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Guid tenantId)
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Manual Addon" });
            await context.SaveChangesAsync();
        }

        private static async Task<Guid> SeedActiveBaseSubscriptionAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Guid tenantId,
            int maxFuncionarios = 1)
        {
            var planId = Guid.NewGuid();
            var nowUtc = GetFixedNowUtc();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.Basic,
                Nombre = "Basico",
                Moneda = "CRC",
                PrecioMensual = 8000m,
                MaxFuncionarios = maxFuncionarios,
                Activo = true
            });
            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                CodigoPlan = PlanCodes.Basic,
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

        private static async Task<Guid> SeedExpiredBaseSubscriptionAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Guid tenantId)
        {
            var planId = Guid.NewGuid();
            var nowUtc = GetFixedNowUtc();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.Basic,
                Nombre = "Basico",
                Moneda = "CRC",
                PrecioMensual = 8000m,
                MaxFuncionarios = 1,
                Activo = true
            });
            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                CodigoPlan = PlanCodes.Basic,
                Estado = EstadoSuscripcion.Vencida,
                Proveedor = PaymentProviderType.Tilopay,
                FechaInicio = nowUtc.AddDays(-35),
                FechaFin = nowUtc.AddDays(-5),
                FechaProximoCobroUtc = nowUtc.AddDays(-5),
                FechaUltimaActualizacionUtc = nowUtc.AddDays(-5)
            });
            await context.SaveChangesAsync();
            return planId;
        }

        private static async Task SeedWhatsAppPlanAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string addonCode,
            int monthlyLimit)
        {
            context.Planes.Add(new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = addonCode,
                Nombre = $"WhatsApp {addonCode}",
                Moneda = "CRC",
                PrecioMensual = 0m,
                LimiteMensajesMensual = monthlyLimit,
                Activo = true
            });
            await context.SaveChangesAsync();
        }

        private static DateTime GetFixedNowUtc() =>
            new DateTimeOffset(new DateTime(2026, 5, 26, 10, 30, 0), TimeSpan.FromHours(-6)).UtcDateTime;
    }
}
