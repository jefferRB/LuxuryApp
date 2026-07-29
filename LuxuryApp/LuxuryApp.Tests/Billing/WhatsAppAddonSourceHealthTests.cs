using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Billing;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// BillingHealth clasifica los add-ons por FUENTE: solo el recurrente pagado activo sin provider
    /// sub cuenta como riesgo de dinero; manuales vigentes son informativos; manuales vencidos y legacy
    /// son operativos/informativos. (rule 10)
    /// </summary>
    public class WhatsAppAddonSourceHealthTests
    {
        private static readonly DateTime NowUtc =
            new DateTimeOffset(new DateTime(2026, 5, 26, 10, 30, 0), TimeSpan.FromHours(-6)).UtcDateTime;

        [Fact]
        public async Task Build_ClassifiesAddonsBySource_MoneyRiskOnlyProviderRecurringWithoutSub()
        {
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider());
            using var _c = context;
            using var _n = connection;

            // 1) ProviderRecurring pagado CON provider sub → efectivo, NO riesgo.
            await SeedAddonAsync(context, a =>
            {
                a.BillingSource = WhatsAppAddonBillingSource.ProviderRecurring;
                a.TilopayRecurringPlanId = 5831;
                a.ProviderSubscriptionId = "prov-sub-ok";
            });
            // 2) ProviderRecurring recurrente SIN provider sub → RIESGO DE DINERO.
            await SeedAddonAsync(context, a =>
            {
                a.BillingSource = WhatsAppAddonBillingSource.ProviderRecurring;
                a.TilopayRecurringPlanId = 5831;
                a.ProviderSubscriptionId = null;
            });
            // 3) ManualGrant indefinido → informativo, NO riesgo.
            await SeedAddonAsync(context, a =>
            {
                a.BillingSource = WhatsAppAddonBillingSource.ManualGrant;
                a.IsManualGrantIndefinite = true;
                a.FechaFin = null;
                a.ProviderSubscriptionId = null;
                a.TilopayRecurringPlanId = null;
            });
            // 4) ManualGrant VENCIDO (fila activa) → alerta operativa, NO riesgo.
            await SeedAddonAsync(context, a =>
            {
                a.BillingSource = WhatsAppAddonBillingSource.ManualGrant;
                a.IsManualGrantIndefinite = false;
                a.ManualGrantExpiresAtUtc = NowUtc.AddDays(-5);
                a.FechaFin = NowUtc.AddDays(-5);
                a.ProviderSubscriptionId = null;
                a.TilopayRecurringPlanId = null;
            });
            // 5) Legacy activo → informativo/limpieza, NO riesgo, NO efectivo.
            await SeedAddonAsync(context, a =>
            {
                a.BillingSource = WhatsAppAddonBillingSource.Legacy;
                a.ProviderSubscriptionId = null;
                a.TilopayRecurringPlanId = null;
            });

            var health = new BillingHealthService(context, CreateSuscripcionService(context));
            var snapshot = await health.BuildAsync();

            Assert.Equal(1, snapshot.PaidAddonsActiveWithoutProviderRisk);
            Assert.Equal(1, snapshot.ManualWhatsAppGrantsActive);
            Assert.Equal(1, snapshot.ManualWhatsAppGrantsExpiredStillActive);
            Assert.Equal(1, snapshot.LegacyWhatsAppAddonsActive);
            // Riesgo de dinero del add-on = SOLO el recurrente sin provider (sin pendientes ni doble activo).
            Assert.Equal(1, snapshot.WhatsAppAddonMoneyRiskCount);
            // Add-ons efectivos: provider ok + provider-risk (activo) + manual indefinido = 3.
            Assert.Equal(3, snapshot.ActiveWhatsAppAddons);
        }

        private static async Task SeedAddonAsync(ApplicationDbContext context, Action<TenantSubscriptionAddon> configure)
        {
            var tenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "T", Activo = true });
            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = "WA-" + planId.ToString("N")[..8], // Planes.Codigo es único
                Nombre = "WhatsApp 400",
                Moneda = "CRC",
                PrecioMensual = 6000m,
                LimiteMensajesMensual = 400,
                Activo = true
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var addon = new TenantSubscriptionAddon
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                AddonCode = PlanCodes.WhatsApp400,
                Estado = EstadoSuscripcion.Activa,
                MonthlyMessageLimit = 400,
                FechaInicio = NowUtc.AddDays(-2),
                FechaFin = NowUtc.AddDays(28),
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc
            };
            configure(addon);
            context.TenantSubscriptionAddons.Add(addon);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        private static SuscripcionService CreateSuscripcionService(ApplicationDbContext context)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            return new SuscripcionService(
                context,
                cache,
                new TenantCommercialAccessCache(cache),
                new FixedBusinessDateTimeProvider(),
                Options.Create(new TilopayRepeatOptions()),
                NullLogger<SuscripcionService>.Instance);
        }
    }
}
