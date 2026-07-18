using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// La pantalla /Billing/Suscripcion debe mostrar la MISMA fecha efectiva en todas sus secciones,
    /// y esa fecha debe ser la de calendario Costa Rica, no el UTC crudo de fin de día.
    ///
    /// Caso compra3: local 2026-08-15, TiloPay expire 2026-09-15 (guardado como 2026-09-16 UTC).
    /// Ninguna sección debe mostrar 16/08 (local vieja) ni 16/09 (UTC crudo): todas 15/09/2026.
    /// </summary>
    public class SubscriptionSummaryDisplayTests
    {
        // Expire "2026-09-15" como fin de día Tico en UTC.
        private static readonly DateTime ProviderExpiresUtc = new(2026, 9, 16, 5, 59, 59, DateTimeKind.Utc);

        [Fact]
        public async Task Summary_ProviderExpiryAhead_AllDateSectionsShowTenantDate()
        {
            using var ctx = CreateContext(out var connection);
            using var _ = connection;

            var tenantId = Guid.NewGuid();
            SeedSubscription(
                ctx,
                tenantId,
                localEndUtc: new DateTime(2026, 8, 15, 22, 3, 57, DateTimeKind.Utc),
                providerExpiresUtc: ProviderExpiresUtc,
                providerRaw: "2026-09-15");
            await ctx.SaveChangesAsync();

            var summary = await BuildAsync(ctx, tenantId);

            Assert.NotNull(summary);
            // Todas las secciones que se alimentan del summary:
            Assert.Equal("15/09/2026", summary!.CurrentPeriodEndDisplay); // "Vigente hasta"
            Assert.Equal("15/09/2026", summary.NextBillingDateDisplay);   // "Próximo cobro" + "Próxima renovación"

            // Y NUNCA la local vieja ni el UTC crudo.
            Assert.DoesNotContain("16/08", summary.CurrentPeriodEndDisplay);
            Assert.DoesNotContain("16/09", summary.NextBillingDateDisplay);
        }

        [Fact]
        public async Task Summary_NoProviderExpiry_ShowsLocalDateAsBefore()
        {
            using var ctx = CreateContext(out var connection);
            using var _ = connection;

            var tenantId = Guid.NewGuid();
            SeedSubscription(
                ctx,
                tenantId,
                localEndUtc: new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc),
                providerExpiresUtc: null,
                providerRaw: null);
            await ctx.SaveChangesAsync();

            var summary = await BuildAsync(ctx, tenantId);

            Assert.Equal("16/08/2026", summary!.NextBillingDateDisplay);
            Assert.Equal("16/08/2026", summary.CurrentPeriodEndDisplay);
        }

        [Fact]
        public async Task Summary_ProviderExpiry_DoesNotChangeEffectiveStatus()
        {
            using var ctx = CreateContext(out var connection);
            using var _ = connection;

            var tenantId = Guid.NewGuid();
            // Local vencida en el pasado, pero provider en el futuro: sigue con acceso (Activa).
            SeedSubscription(
                ctx,
                tenantId,
                localEndUtc: DateTime.UtcNow.AddDays(-3),
                providerExpiresUtc: DateTime.UtcNow.AddDays(28),
                providerRaw: DateTime.UtcNow.AddDays(28).ToString("yyyy-MM-dd"));
            await ctx.SaveChangesAsync();

            var summary = await BuildAsync(ctx, tenantId);

            Assert.Equal(EstadoSuscripcion.Activa, summary!.Status);
            Assert.True(summary.CanAccessApp);
        }

        private static async Task<BillingSubscriptionSummaryViewModel?> BuildAsync(ApplicationDbContext ctx, Guid tenantId)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var repeatOptions = CalculatorCatalog.BuildRepeatOptions();
            var subscriptionService = new SuscripcionService(
                ctx, cache, new TenantCommercialAccessCache(cache), new FixedBusinessDateTimeProvider(),
                Options.Create(repeatOptions), NullLogger<SuscripcionService>.Instance);

            var service = new SubscriptionSummaryService(ctx, subscriptionService, new StubWhatsAppSettings());
            return await service.BuildAsync(tenantId);
        }

        private static void SeedSubscription(
            ApplicationDbContext ctx,
            Guid tenantId,
            DateTime localEndUtc,
            DateTime? providerExpiresUtc,
            string? providerRaw)
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant compra3", Activo = true });
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = "LC_M_02",
                Nombre = "LC_M_02",
                PrecioMensual = 15000m,
                BillingCycle = BillingCycle.Monthly,
                Moneda = "CRC",
                MaxFuncionarios = 2,
                Activo = true
            };
            ctx.Planes.Add(plan);
            ctx.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                CodigoPlan = "LC_M_02",
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = 6126,
                ProviderSubscriptionId = "386117",
                FechaInicio = localEndUtc.AddMonths(-1),
                FechaFin = localEndUtc,
                FechaProximoCobroUtc = localEndUtc,
                ProviderExpiresAtUtc = providerExpiresUtc,
                ProviderExpiryRaw = providerRaw,
                ProviderExpiryLastSyncedUtc = providerExpiresUtc.HasValue ? DateTime.UtcNow : null
            });
        }

        private static ApplicationDbContext CreateContext(out Microsoft.Data.Sqlite.SqliteConnection connection)
        {
            var (ctx, conn) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider());
            connection = conn;
            return ctx;
        }

        /// <summary>Sin add-on activo, el summary nunca llama a estos métodos: basta un stub que lanza.</summary>
        private sealed class StubWhatsAppSettings : ITenantWhatsAppSettingsService
        {
            public Task<TenantWhatsAppSettingsSnapshot> GetSettingsForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("No debería llamarse sin add-on activo.");
            public Task<TenantWhatsAppSettingsSnapshot> EnsureDefaultSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException();
            public Task<bool> IsWhatsAppEnabledForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
                Task.FromResult(false);
            public Task<TenantWhatsAppSendDecision> CanSendNotificationAsync(Guid tenantId, string notificationType, long? reservedMessageLogId = null, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException();
            public Task<int> GetTodayUsageAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
                Task.FromResult(0);
            public Task<bool> HasActiveWhatsAppAddonAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
                Task.FromResult(false);
            public Task UpdateSettingsAsync(Guid tenantId, TenantWhatsAppSettingsUpdateDto dto, string? updatedByUserId, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException();
        }
    }
}
