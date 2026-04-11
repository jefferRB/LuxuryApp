using System.Security.Claims;
using LuxuryApp.Controllers;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class BillingSecurityTests
    {
        [Fact]
        public async Task CreateCheckoutAsync_ShouldRedactWebhookTokenFromAuditPayload()
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
                Nombre = "Plan Pro",
                PrecioMensual = 25,
                Moneda = "CRC",
                Activo = true
            });

            await context.SaveChangesAsync();

            var fakeProvider = new FakePaymentProvider
            {
                CheckoutResult = new PaymentCheckoutResult
                {
                    ProviderType = PaymentProviderType.Tilopay,
                    RedirectUrl = "https://pay.local/checkout/1",
                    ProviderCheckoutId = "checkout-1",
                    ProviderReference = "LXA-ABCDEF-1234567890",
                    RawResponse = "{\"ok\":true}"
                }
            };

            var service = CreatePaymentService(context, fakeProvider);

            await service.CreateCheckoutAsync(
                tenantId,
                planId,
                "Owner",
                "owner@test.local",
                "https://app.local/Billing/Exito",
                "https://app.local/Billing/Cancelado",
                "https://app.local/api/webhooks/tilopay?access_token=secret-token");

            var intento = context.PagosSuscripcion.Single();

            Assert.DoesNotContain("secret-token", intento.UltimoPayloadProveedor);
            Assert.DoesNotContain("access_token=", intento.UltimoPayloadProveedor, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ProcessTilopayWebhookAsync_ShouldRejectCheckoutIdMismatch()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;
            var tenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            const string reference = "LXA-ABCDEF-1234567890";

            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Nombre = "Tenant A",
                Activo = true
            });

            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Plan Pro",
                PrecioMensual = 40,
                Moneda = "CRC",
                Activo = true
            });

            context.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Proveedor = PaymentProviderType.Tilopay,
                ReferenciaInterna = reference,
                ProviderReference = reference,
                ProviderCheckoutId = "checkout-expected",
                Estado = EstadoPagoProveedor.Pendiente,
                Descripcion = "Checkout seguro",
                Monto = 40,
                Moneda = "CRC"
            });

            await context.SaveChangesAsync();

            var fakeProvider = new FakePaymentProvider
            {
                WebhookData = new PaymentProviderWebhookData
                {
                    ProviderType = PaymentProviderType.Tilopay,
                    EventId = "evt-1",
                    EventType = "tilopay.link.completed",
                    Reference = reference,
                    ProviderCheckoutId = "checkout-malicious",
                    ProviderTransactionId = "tx-1"
                },
                VerificationResult = new PaymentVerificationResult
                {
                    ProviderType = PaymentProviderType.Tilopay,
                    Exists = true,
                    IsSuccess = true,
                    Reference = reference,
                    ProviderOrderNumber = reference,
                    ProviderTransactionId = "tx-1",
                    Amount = 40,
                    Currency = "CRC",
                    StatusCode = "1",
                    StatusDescription = "Aprobado",
                    RawResponse = "{\"approved\":true}"
                }
            };

            var service = CreatePaymentService(context, fakeProvider);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ProcessTilopayWebhookAsync("{}", "corr-1"));

            Assert.Contains("checkout", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(context.Facturas.Any());

            var evento = context.EventosPago.Single();
            Assert.False(evento.Procesado);
            Assert.Equal("Error", evento.EstadoProcesamiento);
        }

        [Fact]
        public async Task ProcessTilopayWebhookAsync_ShouldRejectUnrecognizedReferenceBeforeVerification()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;
            var fakeProvider = new FakePaymentProvider
            {
                WebhookData = new PaymentProviderWebhookData
                {
                    ProviderType = PaymentProviderType.Tilopay,
                    EventId = "evt-1",
                    EventType = "tilopay.link.completed",
                    Reference = "provider-reference-raw"
                }
            };

            var service = CreatePaymentService(context, fakeProvider);

            await Assert.ThrowsAsync<PaymentWebhookValidationException>(() =>
                service.ProcessTilopayWebhookAsync("{}", "corr-2"));

            Assert.Equal(0, fakeProvider.VerifyCalls);
            Assert.False(context.EventosPago.Any());
        }

        [Fact]
        public async Task BillingSuccess_ShouldNotExposePaymentFromAnotherTenant()
        {
            var (context, connection) = CreateSystemContext();
            using var disposableContext = context;
            using var disposableConnection = connection;
            using var userManager = CreateUserManager(context);

            var currentTenantId = Guid.NewGuid();
            var foreignTenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            const string reference = "LXA-FOREIGN-1234567890";

            context.Tenants.AddRange(
                new Tenant
                {
                    Id = currentTenantId,
                    Nombre = "Tenant A",
                    Activo = true
                },
                new Tenant
                {
                    Id = foreignTenantId,
                    Nombre = "Tenant B",
                    Activo = true
                });

            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Plan Premium",
                PrecioMensual = 55,
                Moneda = "CRC",
                Activo = true
            });

            var currentUser = new AppUsuario
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "owner-a@test.local",
                NormalizedUserName = "OWNER-A@TEST.LOCAL",
                Email = "owner-a@test.local",
                NormalizedEmail = "OWNER-A@TEST.LOCAL",
                TenantId = currentTenantId,
                State = true
            };

            context.Users.Add(currentUser);
            context.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = foreignTenantId,
                PlanId = planId,
                Proveedor = PaymentProviderType.Tilopay,
                ReferenciaInterna = reference,
                ProviderReference = reference,
                Estado = EstadoPagoProveedor.Confirmado,
                Descripcion = "Pago de otro tenant",
                Monto = 55,
                Moneda = "CRC"
            });

            await context.SaveChangesAsync();

            var controller = new BillingController(
                NullLogger<BillingController>.Instance,
                context,
                null!,
                null!,
                userManager,
                Options.Create(new OpcionesTilopay()),
                Options.Create(new OpcionesPago()))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = BuildPrincipal(currentUser.Id, currentTenantId)
                    }
                }
            };

            var result = await controller.Exito(reference, code: null, description: null);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ResultadoCheckoutViewModel>(view.Model);
            Assert.Equal(reference, model.Referencia);
            Assert.Null(model.NombrePlan);
            Assert.Null(model.EstadoPago);
            Assert.Contains("No encontramos", model.MensajePrincipal, StringComparison.OrdinalIgnoreCase);
        }

        private static (ProyectoIdentity.Datos.ApplicationDbContext Context, IDisposable Connection) CreateSystemContext()
        {
            var tenantProvider = new TestTenantProvider();
            return TestDbContextFactory.CreateSqliteContext(tenantProvider);
        }

        private static SaaSPaymentService CreatePaymentService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            FakePaymentProvider fakeProvider)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var subscriptionService = new SuscripcionService(
                context,
                cache,
                NullLogger<SuscripcionService>.Instance);

            return new SaaSPaymentService(
                context,
                new PaymentProviderResolver(new[] { fakeProvider }),
                subscriptionService,
                Options.Create(new OpcionesPago
                {
                    ProveedorPredeterminado = PaymentProviderType.Tilopay
                }),
                Options.Create(new OpcionesTilopay
                {
                    MerchantId = "merchant-1"
                }),
                NullLogger<SaaSPaymentService>.Instance);
        }

        private static UserManager<AppUsuario> CreateUserManager(ProyectoIdentity.Datos.ApplicationDbContext context)
        {
            var store = new UserStore<AppUsuario>(context);

            return new UserManager<AppUsuario>(
                store,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<AppUsuario>(),
                Array.Empty<IUserValidator<AppUsuario>>(),
                Array.Empty<IPasswordValidator<AppUsuario>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                NullLogger<UserManager<AppUsuario>>.Instance);
        }

        private static ClaimsPrincipal BuildPrincipal(string userId, Guid tenantId) =>
            new(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim("tenant_id", tenantId.ToString())
                },
                "TestAuth"));

        private sealed class FakePaymentProvider : IPaymentProvider
        {
            public PaymentProviderType ProviderType => PaymentProviderType.Tilopay;

            public PaymentCheckoutResult CheckoutResult { get; set; } = new()
            {
                ProviderType = PaymentProviderType.Tilopay,
                RedirectUrl = "https://pay.local/default"
            };

            public PaymentProviderWebhookData WebhookData { get; set; } = new()
            {
                ProviderType = PaymentProviderType.Tilopay,
                EventId = "evt-default",
                EventType = "tilopay.link.completed",
                Reference = "LXA-ABCDEF-1234567890"
            };

            public PaymentVerificationResult VerificationResult { get; set; } = new()
            {
                ProviderType = PaymentProviderType.Tilopay,
                Exists = true,
                IsSuccess = true,
                Reference = "LXA-ABCDEF-1234567890",
                ProviderOrderNumber = "LXA-ABCDEF-1234567890",
                Amount = 10,
                Currency = "CRC"
            };

            public int VerifyCalls { get; private set; }

            public Task<PaymentCheckoutResult> CreateCheckoutAsync(
                PaymentCheckoutRequest request,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(CheckoutResult);

            public PaymentProviderWebhookData ParseWebhook(string payload) => WebhookData;

            public Task<PaymentVerificationResult> VerifyPaymentAsync(
                PaymentVerificationRequest request,
                CancellationToken cancellationToken = default)
            {
                VerifyCalls++;
                return Task.FromResult(VerificationResult);
            }
        }
    }
}
