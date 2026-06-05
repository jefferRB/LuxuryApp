using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class RecurringCheckoutConfigurationTests
    {
        [Fact]
        public async Task CreateRecurringCheckoutAsync_ShouldRequireHostedLinkKeyForConfiguredPlan()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();

            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Nombre = "Tenant A",
                Activo = true
            });

            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.Basic,
                Nombre = "Basico",
                PrecioMensual = 8000,
                Moneda = "CRC",
                Activo = true
            });

            await context.SaveChangesAsync();

            var service = CreatePaymentService(
                context,
                new TilopayRepeatOptions
                {
                    Enabled = true,
                    UseHostedLinks = true,
                    UseRecurringCheckoutForPublicPlans = true,
                    Basic = new TilopayRepeatPlanOption
                    {
                        TilopayPlanId = 5828,
                        Code = PlanCodes.Basic
                    }
                },
                new OpcionesTilopay
                {
                    WebhookAccessToken = "token-seguro"
                });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateRecurringCheckoutAsync(
                    tenantId,
                    planId,
                    "Owner",
                    "owner@test.local"));

            Assert.Contains("TilopayRepeat:Basic:CheckoutUrl", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task CreateRecurringCheckoutAsync_ShouldAppendCorrelationToHostedLink()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();

            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Nombre = "Tenant A",
                Activo = true
            });

            context.Planes.Add(new Plan
            {
                Id = planId,
                Codigo = PlanCodes.TestRecurring,
                Nombre = "Test recurrente",
                PrecioMensual = 1000,
                Moneda = "CRC",
                Activo = true,
                EsPlanValidacion = true
            });

            await context.SaveChangesAsync();

            var service = CreatePaymentService(
                context,
                new TilopayRepeatOptions
                {
                    Enabled = true,
                    UseHostedLinks = true,
                    EnableTestRecurringPlan = true,
                    TestRecurring = new TilopayRepeatPlanOption
                    {
                        TilopayPlanId = 5834,
                        Code = PlanCodes.TestRecurring,
                        CheckoutUrl = "https://tp.cr/l/test-link?plan=5834",
                        IsValidation = true
                    }
                },
                new OpcionesTilopay
                {
                    WebhookAccessToken = "token-seguro"
                });

            var result = await service.CreateRecurringCheckoutAsync(
                tenantId,
                planId,
                "Owner",
                "owner@test.local");

            Assert.StartsWith("https://tp.cr/l/test-link?plan=5834&", result.RedirectUrl, StringComparison.Ordinal);
            var uri = new Uri(result.RedirectUrl, UriKind.Absolute);
            var query = QueryHelpers.ParseQuery(uri.Query);

            Assert.True(query.ContainsKey("lc_ref"));
            Assert.Equal(PlanCodes.TestRecurring, query["lc_plan"].ToString());
            Assert.Equal("owner@test.local", query["lc_email"].ToString());
        }

        private static (ProyectoIdentity.Datos.ApplicationDbContext Context, IDisposable Connection) CreateSystemContext()
        {
            var tenantProvider = new TestTenantProvider();
            return TestDbContextFactory.CreateSqliteContext(tenantProvider);
        }

        private static SaaSPaymentService CreatePaymentService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            TilopayRepeatOptions repeatOptions,
            OpcionesTilopay tilopayOptions)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var subscriptionService = new SuscripcionService(
                context,
                cache,
                new TenantCommercialAccessCache(cache),
                new FixedBusinessDateTimeProvider(),
                Options.Create(repeatOptions),
                NullLogger<SuscripcionService>.Instance);

            return new SaaSPaymentService(
                context,
                new PaymentProviderResolver(Array.Empty<IPaymentProvider>()),
                subscriptionService,
                Options.Create(new OpcionesPago
                {
                    ProveedorPredeterminado = PaymentProviderType.Tilopay
                }),
                Options.Create(tilopayOptions),
                Options.Create(repeatOptions),
                NullLogger<SaaSPaymentService>.Instance);
        }
    }
}
