using LuxuryApp.Controllers.Platform;
using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Billing;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// Integración de la resolución de id_suscriptor y la gestión del suscriptor del proveedor,
    /// usando un cliente admin fake. Cubre webhook → resolución, fallback a alerta, reconciliación,
    /// blindaje de checkout, upgrade con cancelación del suscriptor viejo, autorización y masking.
    /// </summary>
    public class SubscriberResolutionIntegrationTests
    {
        private const string Email = "compra1usuario@gmail.com";

        // ── 6. Webhook repeat_registration resuelve y guarda el subscriber id ──
        [Fact]
        public async Task Webhook_Registration_ResolvesAndPersistsSubscriberId()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound(); // durante checkout no existe aún
            await h.StartCheckoutAsync();

            h.Admin.ResolutionResult = SubscriberResolutionResult.Found(
                new TilopaySubscriber { SubscriberId = "374830", Email = Email }, 1);

            await h.ProcessWebhookAsync("repeat_registration", amount: null, transactionId: "TX-REG-1");

            var payment = await h.Db.PagosSuscripcion.IgnoreQueryFilters().SingleAsync();
            var subscription = await h.Db.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal("374830", subscription.ProviderSubscriptionId);
            // El pago sigue Pendiente en el registro; el subscriber ya quedó en la suscripción.
            Assert.Equal("374830", payment.ProviderSubscriberId);
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.ProviderSubscriberResolved));
        }

        // ── 7. Webhook repeat_payment_success activa y guarda subscriber id ──
        [Fact]
        public async Task Webhook_PaymentSuccess_ActivatesAndPersistsSubscriberId()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound();
            await h.StartCheckoutAsync();

            h.Admin.ResolutionResult = SubscriberResolutionResult.Found(
                new TilopaySubscriber { SubscriberId = "374830", Email = Email }, 1);

            await h.ProcessWebhookAsync("repeat_payment_success", amount: h.Data.Charge, transactionId: "TX-OK-1");

            var subscription = await h.Db.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoSuscripcion.Activa, subscription.Estado);
            Assert.Equal("374830", subscription.ProviderSubscriptionId);

            var payment = await h.Db.PagosSuscripcion.IgnoreQueryFilters()
                .SingleAsync(p => p.Estado == EstadoPagoProveedor.Confirmado);
            Assert.Equal("374830", payment.ProviderSubscriberId);
        }

        // ── 8. Si el API falla, la compra sigue activa pero se crea alerta ──
        [Fact]
        public async Task Webhook_PaymentSuccess_ApiFailure_KeepsActivationAndAlerts()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound();
            await h.StartCheckoutAsync();

            h.Admin.ResolutionResult = SubscriberResolutionResult.Failed("timeout simulado");

            await h.ProcessWebhookAsync("repeat_payment_success", amount: h.Data.Charge, transactionId: "TX-FAIL-1");

            var subscription = await h.Db.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoSuscripcion.Activa, subscription.Estado);          // activación intacta
            Assert.Null(subscription.ProviderSubscriptionId);                     // subscriber no resuelto
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.ProviderSubscriberResolutionFailed));
        }

        // ── 9. Reconciliación posterior repara ProviderSubscriptionId faltante ──
        [Fact]
        public async Task Reconciliation_RepairsMissingSubscriberId()
        {
            using var h = await Harness.CreateAsync(workers: 1);

            // Suscripción activa + pago confirmado, ambos SIN subscriber id (estado del bug real).
            h.Db.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddMonths(1)
            });
            h.Db.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = EstadoPagoProveedor.Confirmado,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                ReferenciaInterna = "LXA-RECON-1",
                ClienteEmail = Email,
                Monto = h.Data.Charge,
                Moneda = "CRC",
                FechaCreacionUtc = DateTime.UtcNow.AddHours(-2),
                FechaConfirmacionUtc = DateTime.UtcNow.AddHours(-2)
            });
            await h.Db.SaveChangesAsync();

            h.Admin.ResolutionResult = SubscriberResolutionResult.Found(
                new TilopaySubscriber { SubscriberId = "374830", Email = Email }, 1);

            var report = await h.Reconciliation.RunAsync();

            var subscription = await h.Db.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal("374830", subscription.ProviderSubscriptionId);
            Assert.True(report.SubscriberIdsResolved >= 1);
        }

        // ── 10 (contrato real). REAL admin service + respuesta real de TiloPay → persiste 374830 ──
        [Fact]
        public async Task EndToEnd_RealTilopayResponse_PersistsSubscriberIdInPaymentAndSubscription()
        {
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider());
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var plan = new Plan
            {
                Id = Guid.NewGuid(),
                Codigo = "LC_M_01",
                Nombre = "LuxuryCloud 1 Mensual",
                PrecioMensual = 8000m,
                BillingCycle = BillingCycle.Monthly,
                Moneda = "CRC",
                MaxFuncionarios = 1,
                Activo = true
            };
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant Real", Activo = true });
            context.Planes.Add(plan);

            var paymentId = Guid.NewGuid();
            context.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = paymentId,
                TenantId = tenantId,
                PlanId = plan.Id,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = EstadoPagoProveedor.Confirmado,
                TilopayRecurringPlanId = 6119,
                ReferenciaInterna = "LXA-0B6BFF-71BBE2920A",
                ProviderTransactionId = "5328829",
                ProviderSubscriberId = null,
                ClienteEmail = "compra1usuario@gmail.com",
                Monto = 8000m,
                Moneda = "CRC",
                FechaCreacionUtc = DateTime.UtcNow.AddHours(-2),
                FechaConfirmacionUtc = DateTime.UtcNow.AddHours(-2)
            });
            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = 6119,
                ProviderSubscriptionId = null,
                FechaInicio = DateTime.UtcNow.AddDays(-1),
                FechaFin = DateTime.UtcNow.AddMonths(1)
            });
            await context.SaveChangesAsync();

            // Servicio admin REAL contra la respuesta real de getSuscriptorRepeat (array "suscriptor").
            var handler = new RealResponseHandler(RealSuscriptorResponse);
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://app.tilopay.com/") };
            var adminService = new TilopayRepeatAdminService(
                httpClient,
                new MemoryCache(new MemoryCacheOptions()),
                Options.Create(new OpcionesTilopay { ApiUser = "u", ApiPassword = "p", ApiKey = "k", BaseUrl = "https://app.tilopay.com/" }),
                Options.Create(new OpcionesTilopayRepeatAdmin { Enabled = true, ResolveRetryCount = 0 }),
                NullLogger<TilopayRepeatAdminService>.Instance);

            var resolution = new SubscriberResolutionService(
                context,
                adminService,
                new TenantExecutionContextAccessor(),
                new FixedBusinessDateTimeProvider(),
                NullLogger<SubscriberResolutionService>.Instance);

            var outcome = await resolution.TryResolveAndPersistAsync(new SubscriberResolutionContext
            {
                TenantId = tenantId,
                TilopayRecurringPlanId = 6119,
                Email = "compra1usuario@gmail.com",
                PaymentId = paymentId,
                IsAddon = false,
                Source = "reconciliation"
            });

            Assert.Equal(SubscriberPersistenceOutcome.Resolved, outcome);

            var pago = await context.PagosSuscripcion.IgnoreQueryFilters().SingleAsync();
            Assert.Equal("374830", pago.ProviderSubscriberId);

            var sub = await context.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal("374830", sub.ProviderSubscriptionId);
        }

        private const string RealSuscriptorResponse =
            """
            {
              "type": "success",
              "message": "ok",
              "suscriptor": [
                {
                  "id": 374830,
                  "name": "Jefferson",
                  "lastname": "Rojas",
                  "email": "compra1usuario@gmail.com",
                  "modality": "LC_M_01",
                  "amount": "8000.00",
                  "expire": "2026-08-07",
                  "coupon": "",
                  "status": "Active",
                  "create": "2026-07-07 19:26:00"
                }
              ]
            }
            """;

        private sealed class RealResponseHandler : System.Net.Http.HttpMessageHandler
        {
            private readonly string _suscriptorBody;
            public RealResponseHandler(string suscriptorBody) => _suscriptorBody = suscriptorBody;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var body = path.EndsWith("login", StringComparison.OrdinalIgnoreCase)
                    ? """{ "access_token": "fake-token", "expires_in": 3600 }"""
                    : _suscriptorBody;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                });
            }
        }

        // ── 10. Checkout bloquea hosted link nuevo si ya existe suscriptor ──
        [Fact]
        public async Task Checkout_WithExistingSubscriber_RoutesToRecurrentUrlWithoutNewHostedLink()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.Found(
                new TilopaySubscriber { SubscriberId = "374830", Email = Email }, 1);
            h.Admin.RecurrentUrl = TilopayAdminOperationResult.Ok("ok", "https://tp.cr/l/recurrent-link");

            var checkout = await h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email);

            Assert.Equal("https://tp.cr/l/recurrent-link", checkout.RedirectUrl);
            // No se creó un intento de pago nuevo (habría duplicado el suscriptor).
            Assert.Equal(0, await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync());
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.CheckoutBlockedExistingProviderSubscriber));
        }

        [Fact]
        public async Task Checkout_WithNoExistingSubscriber_CreatesHostedLinkNormally()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound();

            var checkout = await h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email);

            Assert.Contains("tp.cr", checkout.RedirectUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync());
        }

        // ── Fail-CLOSED matrix: API admin caído decide según señal local ──

        [Fact]
        public async Task Checkout_ApiDown_CleanFirstPurchase_AllowsHostedLink()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.Failed("API caído");

            var checkout = await h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email);

            // Sin ninguna señal local: primera compra limpia → hosted link normal.
            Assert.Contains("tp.cr", checkout.RedirectUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await h.CountAuditAsync(PlatformAuditActions.CheckoutBlockedProviderVerificationUnavailable));
        }

        [Theory]
        [InlineData(EstadoPagoProveedor.Fallido)]
        [InlineData(EstadoPagoProveedor.Pendiente)]
        [InlineData(EstadoPagoProveedor.ManualReview)]
        [InlineData(EstadoPagoProveedor.Confirmado)]           // confirmado sin ProviderSubscriberId
        public async Task Checkout_ApiDown_WithPaymentSignal_Blocks(EstadoPagoProveedor estado)
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.Failed("API caído");

            h.Db.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = estado,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                ReferenciaInterna = "LXA-SIGNAL-1",
                ClienteEmail = Email,
                Monto = h.Data.Charge,
                Moneda = "CRC",
                ProviderSubscriberId = null, // el caso "confirmado sin subscriber" es explícito
                FechaCreacionUtc = DateTime.UtcNow.AddMinutes(-10),
                FechaConfirmacionUtc = estado == EstadoPagoProveedor.Confirmado ? DateTime.UtcNow.AddMinutes(-10) : null
            });
            await h.Db.SaveChangesAsync();

            await Assert.ThrowsAsync<RecurringCheckoutBlockedException>(() =>
                h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email));

            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.CheckoutBlockedProviderVerificationUnavailable));
            // No se creó un intento NUEVO (sigue solo el sembrado).
            Assert.Equal(1, await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync());
        }

        [Theory]
        [InlineData(EstadoSuscripcion.Activa)]
        [InlineData(EstadoSuscripcion.Morosa)]
        public async Task Checkout_ApiDown_WithSubscriptionSignal_Blocks(EstadoSuscripcion estado)
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.Failed("API caído");

            h.Db.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Estado = estado,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                FechaInicio = DateTime.UtcNow.AddDays(-10),
                FechaFin = DateTime.UtcNow.AddDays(20)
            });
            await h.Db.SaveChangesAsync();

            await Assert.ThrowsAsync<RecurringCheckoutBlockedException>(() =>
                h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email));

            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.CheckoutBlockedProviderVerificationUnavailable));
        }

        [Fact]
        public async Task Checkout_ApiDown_WithExistingProviderSubscriptionId_Blocks()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.Failed("API caído");

            h.Db.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Estado = EstadoSuscripcion.Cancelada, // aunque esté cancelada, el subscriber previo importa
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                ProviderSubscriptionId = "374830",
                FechaInicio = DateTime.UtcNow.AddDays(-30)
            });
            await h.Db.SaveChangesAsync();

            await Assert.ThrowsAsync<RecurringCheckoutBlockedException>(() =>
                h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email));

            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.CheckoutBlockedProviderVerificationUnavailable));
        }

        [Fact]
        public async Task Checkout_AmbiguousSubscribers_Blocks()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.Ambiguous(2, "dos suscriptores por email");

            await Assert.ThrowsAsync<RecurringCheckoutBlockedException>(() =>
                h.Payments.CreateRecurringCheckoutAsync(h.TenantId, h.PlanId, "Owner", Email));

            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.CheckoutBlockedExistingProviderSubscriber));
            Assert.Equal(0, await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync());
        }

        // ── 13. Upgrade cancela el suscriptor viejo tras activar el nuevo ──
        [Fact]
        public async Task UpgradeCancellation_DeletesOldSubscriberAndAudits()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-111", newSubscriber: "NEW-222");
            await h.Db.SaveChangesAsync();

            h.Admin.DeleteResult = TilopayAdminOperationResult.Ok("eliminado");

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.Cancelled, intent.OldProviderCancellation);
            Assert.Contains("OLD-111", h.Admin.DeletedSubscriberIds);
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.UpgradeOldProviderSubscriptionCancellationCompleted));
        }

        // ── 14. Si la cancelación del proveedor falla, BillingHealth queda en alerta ──
        [Fact]
        public async Task UpgradeCancellation_Failure_LeavesPendingAndHealthAlert()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-111", newSubscriber: "NEW-222");
            await h.Db.SaveChangesAsync();

            // Delete y edit fallan, y la verificación muestra al viejo TODAVÍA activo:
            // fallo real (no idempotente), debe quedar pendiente.
            h.Admin.DeleteResult = TilopayAdminOperationResult.Fail("TiloPay rechazó la baja");
            h.Admin.EditResult = TilopayAdminOperationResult.Fail("edit también rechazado");
            h.Admin.GetSubscribers.Add(new TilopaySubscriber { SubscriberId = "OLD-111", Status = "Active" });

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, intent.OldProviderCancellation);
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.UpgradeOldProviderSubscriptionCancellationFailed));

            var health = await h.Health.BuildAsync();
            Assert.True(health.ProviderCancellationsFailedLast7d >= 1);
        }

        // ── Verificación OBLIGATORIA post-baja: un 200 de TiloPay no basta ──
        [Fact]
        public async Task UpgradeCancellation_DeleteReturns200ButStillActive_NotCancelled_AuditsVerificationFailed()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-111", newSubscriber: "NEW-222");
            await h.Db.SaveChangesAsync();

            // TiloPay dice 200 al delete... pero getSuscriptorRepeat muestra al viejo TODAVÍA Active.
            h.Admin.DeleteResult = TilopayAdminOperationResult.Ok("ok");
            h.Admin.GetSubscribers.Add(new TilopaySubscriber { SubscriberId = "OLD-111", Status = "Active" });

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, intent.OldProviderCancellation);
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.PlanChangeOldSubscriberCancellationVerificationFailed));

            var health = await h.Health.BuildAsync();
            Assert.True(health.ProviderCancellationsFailedLast7d >= 1);
        }

        [Fact]
        public async Task UpgradeCancellation_Delete200AndVerifyAbsent_MarksCancelled()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-111", newSubscriber: "NEW-222");
            await h.Db.SaveChangesAsync();

            h.Admin.DeleteResult = TilopayAdminOperationResult.Ok("ok");
            // GetSubscribers vacío: verificación confirma ausente.

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.Cancelled, intent.OldProviderCancellation);
        }

        [Fact]
        public async Task UpgradeCancellation_EditFallback200AndVerifyInactive_MarksCancelled()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-111", newSubscriber: "NEW-222");
            await h.Db.SaveChangesAsync();

            // delete falla; edit status=4 devuelve 200; verificación: el viejo quedó status 4.
            h.Admin.DeleteResult = TilopayAdminOperationResult.Fail("delete rechazado");
            h.Admin.EditResult = TilopayAdminOperationResult.Ok("edited");
            h.Admin.GetSubscribers.Add(new TilopaySubscriber { SubscriberId = "OLD-111", Status = "4" });

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.Cancelled, intent.OldProviderCancellation);
        }

        // ── Recuperación: viejo cancelado externamente pero DB local incompleta ──
        [Fact]
        public async Task Reconciliation_OldCancelledExternallyButDbIncomplete_ConvergesWithoutDoubleCharge()
        {
            using var h = await Harness.CreateAsync(workers: 1);

            // Estado tras un crash: el nuevo plan YA quedó aplicado localmente (suscripción activa)
            // y el viejo YA fue eliminado en TiloPay, pero el intent quedó PendingManualCancellation.
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-GONE", newSubscriber: "NEW-999");
            h.Db.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                CodigoPlan = h.Data.Code,
                Estado = EstadoSuscripcion.Activa,
                Proveedor = PaymentProviderType.Tilopay,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                ProviderSubscriptionId = "NEW-999",
                FechaInicio = DateTime.UtcNow.AddHours(-1),
                FechaFin = DateTime.UtcNow.AddMonths(1)
            });
            await h.Db.SaveChangesAsync();

            // TiloPay: delete falla ("no existe") y la verificación confirma que el viejo no está.
            h.Admin.DeleteResult = TilopayAdminOperationResult.Fail("no existe");
            h.Admin.EditResult = TilopayAdminOperationResult.Fail("no existe");

            var paymentsBefore = await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync();

            await h.Reconciliation.RunAsync();

            // Converge: intent Cancelled; suscripción local intacta en el plan nuevo; sin pagos nuevos.
            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.Cancelled, intent.OldProviderCancellation);

            var subscription = await h.Db.Suscripciones.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(EstadoSuscripcion.Activa, subscription.Estado);
            Assert.Equal("NEW-999", subscription.ProviderSubscriptionId);

            Assert.Equal(paymentsBefore, await h.Db.PagosSuscripcion.IgnoreQueryFilters().CountAsync());
        }

        // ── Pendings legacy (previos al hardening) ──
        [Fact]
        public async Task LegacyPending_NewCheckoutExpiresIt_NoUnboundedDuplicates()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound();

            // Pending legacy: viejo (fuera de la ventana de reuso de 30 min), sin txid ni subscriber.
            h.Db.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = EstadoPagoProveedor.Pendiente,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                ReferenciaInterna = "LXA-LEGACY-1",
                ProviderTransactionId = null,
                ProviderSubscriberId = null,
                ClienteEmail = Email,
                Monto = h.Data.Charge,
                Moneda = "CRC",
                CheckoutUrl = "https://tp.cr/l/legacy",
                FechaCreacionUtc = DateTime.UtcNow.AddDays(-2)
            });
            await h.Db.SaveChangesAsync();

            await h.StartCheckoutAsync();

            var attempts = await h.Db.PagosSuscripcion.IgnoreQueryFilters().ToListAsync();
            // El legacy quedó Expirado (no elegible) y solo hay UN pendiente abierto (el nuevo).
            Assert.Equal(EstadoPagoProveedor.Expirado, attempts.Single(p => p.ReferenciaInterna == "LXA-LEGACY-1").Estado);
            Assert.Equal(1, attempts.Count(p => p.Estado == EstadoPagoProveedor.Pendiente));
        }

        [Fact]
        public async Task WebhookWithNewLcRef_ConfirmsOnlyNewAttempt_NotLegacyPending()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound();

            // Legacy pending con OTRO email (no lo expira el checkout nuevo) y sin lc_ref propio.
            var legacyId = Guid.NewGuid();
            h.Db.PagosSuscripcion.Add(new PagoSuscripcion
            {
                Id = legacyId,
                TenantId = h.TenantId,
                PlanId = h.PlanId,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = EstadoPagoProveedor.Pendiente,
                TilopayRecurringPlanId = h.Data.RecurringPlanId,
                ReferenciaInterna = "LXA-LEGACY-2",
                ClienteEmail = "otro-legacy@test.local",
                Monto = h.Data.Charge,
                Moneda = "CRC",
                FechaCreacionUtc = DateTime.UtcNow.AddDays(-2)
            });
            await h.Db.SaveChangesAsync();

            await h.StartCheckoutAsync();
            // El webhook llega con el lc_ref del checkout NUEVO (correlación exacta por referencia).
            await h.ProcessWebhookAsync("repeat_payment_success", amount: h.Data.Charge, transactionId: "TX-NEW-REF-1");

            var attempts = await h.Db.PagosSuscripcion.IgnoreQueryFilters().ToListAsync();
            // El lc_ref nuevo confirmó SOLO el intento nuevo; el legacy no se tocó.
            Assert.Equal(EstadoPagoProveedor.Pendiente, attempts.Single(p => p.Id == legacyId).Estado);
            Assert.Equal(1, attempts.Count(p => p.Estado == EstadoPagoProveedor.Confirmado));
            Assert.NotEqual(legacyId, attempts.Single(p => p.Estado == EstadoPagoProveedor.Confirmado).Id);
        }

        [Fact]
        public async Task WebhookWithoutLcRef_TwoOpenPendings_GoesToManualReview_NotMisapplied()
        {
            using var h = await Harness.CreateAsync(workers: 1);

            // Dos pendings abiertos del MISMO plan y email (legacy + reciente), sembrados directo.
            foreach (var reference in new[] { "LXA-AMB-1", "LXA-AMB-2" })
            {
                h.Db.PagosSuscripcion.Add(new PagoSuscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = h.TenantId,
                    PlanId = h.PlanId,
                    Proveedor = PaymentProviderType.Tilopay,
                    Estado = EstadoPagoProveedor.Pendiente,
                    TilopayRecurringPlanId = h.Data.RecurringPlanId,
                    ReferenciaInterna = reference,
                    ClienteEmail = Email,
                    Monto = h.Data.Charge,
                    Moneda = "CRC",
                    FechaCreacionUtc = DateTime.UtcNow.AddHours(-3)
                });
            }
            await h.Db.SaveChangesAsync();

            // Webhook de éxito SIN lc_ref: ambiguo entre los dos pendings → revisión manual,
            // nunca aplicar a ciegas al equivocado.
            h.Provider.WebhookData = new PaymentProviderWebhookData
            {
                ProviderType = PaymentProviderType.Tilopay,
                EventType = "tilopay.repeat.notification",
                Reference = string.Empty,
                RecurringPlanId = h.Data.RecurringPlanId,
                CustomerEmail = Email,
                Amount = h.Data.Charge,
                Currency = "CRC",
                ProviderTransactionId = "TX-AMBIGUO-1",
                IsRecurring = true
            };

            var result = await h.Payments.ProcessTilopayWebhookAsync("{\"x\":1}", "corr-amb", "repeat_payment_success");

            Assert.False(result.IsProcessed);

            var attempts = await h.Db.PagosSuscripcion.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(0, attempts.Count(p => p.Estado == EstadoPagoProveedor.Confirmado));
            Assert.Empty(await h.Db.Suscripciones.IgnoreQueryFilters().Where(s => s.Estado == EstadoSuscripcion.Activa).ToListAsync());
        }

        // ── Idempotencia: si el viejo ya no está activo, la cancelación se considera exitosa ──
        [Fact]
        public async Task UpgradeCancellation_DeleteFailsButSubscriberAlreadyGone_IsTreatedAsSuccess()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-GONE", newSubscriber: "NEW-222");
            await h.Db.SaveChangesAsync();

            // delete y edit fallan, PERO getSuscriptorRepeat no devuelve al viejo → ya no cobrable.
            h.Admin.DeleteResult = TilopayAdminOperationResult.Fail("no existe");
            h.Admin.EditResult = TilopayAdminOperationResult.Fail("no existe");
            // GetSubscribers vacío: el viejo ya no aparece.

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.Cancelled, intent.OldProviderCancellation);
        }

        // ── Cambio de plan: la reconciliación reintenta la cancelación del suscriptor viejo ──
        [Fact]
        public async Task Reconciliation_RetriesPendingOldSubscriberCancellation_AndAudits()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-382770", newSubscriber: "NEW-393000");
            await h.Db.SaveChangesAsync();

            // En el webhook la cancelación había fallado (quedó PendingManualCancellation).
            // Ahora TiloPay ya responde OK: la reconciliación debe cancelar el viejo.
            h.Admin.DeleteResult = TilopayAdminOperationResult.Ok("eliminado");

            var report = await h.Reconciliation.RunAsync();

            Assert.Contains("OLD-382770", h.Admin.DeletedSubscriberIds);
            Assert.True(report.OldSubscriberCancellationsRetried >= 1);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.Cancelled, intent.OldProviderCancellation);
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.PlanChangeOldSubscriberCancellationRetried));
        }

        [Fact]
        public async Task Reconciliation_OldCancellationStillFails_StaysPending_AndHealthFlagsRisk()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-382770", newSubscriber: "NEW-393000");
            await h.Db.SaveChangesAsync();

            h.Admin.DeleteResult = TilopayAdminOperationResult.Fail("TiloPay sigue rechazando la baja");
            h.Admin.EditResult = TilopayAdminOperationResult.Fail("edit también rechazado");
            h.Admin.GetSubscribers.Add(new TilopaySubscriber { SubscriberId = "OLD-382770", Status = "Active" });

            await h.Reconciliation.RunAsync();

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, intent.OldProviderCancellation);

            // Health debe exponer el riesgo de doble cobro (cambio aplicado, viejo no cancelado).
            var health = await h.Health.BuildAsync();
            Assert.True(health.PlanChangeManualReviewCount >= 1);
        }

        [Fact]
        public async Task Reconciliation_TwoTenantsWithPendingCancellations_NoTenantMixAndBothRetried()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            SeedAppliedUpgradeIntent(h, oldSubscriber: "OLD-A", newSubscriber: "NEW-A");
            await h.Db.SaveChangesAsync();

            // Segundo tenant con su propio intent aplicado pendiente de cancelar (mismo DbContext).
            var tenantB = Guid.NewGuid();
            h.Db.Tenants.Add(new Tenant { Id = tenantB, Nombre = "Tenant B", Activo = true });
            h.Db.PlanChangeIntents.Add(new PlanChangeIntent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantB,
                FromPlanCode = "LC_M_02",
                FromProviderSubscriptionId = "OLD-B",
                ToPlanId = h.PlanId,
                ToPlanCode = "LC_M_03",
                ToWorkerCount = 3,
                ToBillingCycle = BillingCycle.Monthly,
                ToTilopayRecurringPlanId = 6127,
                Estado = PlanChangeIntentState.Applied,
                OldProviderCancellation = ProviderCancellationState.PendingManualCancellation,
                NewProviderSubscriptionId = "NEW-B",
                AppliedAtUtc = DateTime.UtcNow
            });
            await h.Db.SaveChangesAsync();

            h.Admin.DeleteResult = TilopayAdminOperationResult.Ok("eliminado");

            // No debe lanzar "contexto de sistema intentando mezclar tenants".
            var report = await h.Reconciliation.RunAsync();

            Assert.Contains("OLD-A", h.Admin.DeletedSubscriberIds);
            Assert.Contains("OLD-B", h.Admin.DeletedSubscriberIds);
            Assert.True(report.OldSubscriberCancellationsRetried >= 2);
        }

        // ── 15. Los controllers admin exigen política de super admin ──
        [Fact]
        public void ProviderSubscriptionController_RequiresPlatformSuperAdminPolicy()
        {
            AssertPlatformPolicy(typeof(PlatformProviderSubscriptionController));
            AssertPlatformPolicy(typeof(PlatformBillingHealthController));
        }

        // ── 16. La auditoría enmascara el id_suscriptor (no guarda el id completo en claro) ──
        [Fact]
        public async Task ResolvedAudit_MasksSubscriberId()
        {
            using var h = await Harness.CreateAsync(workers: 1);
            h.Admin.ResolutionResult = SubscriberResolutionResult.NotFound();
            await h.StartCheckoutAsync();
            h.Admin.ResolutionResult = SubscriberResolutionResult.Found(
                new TilopaySubscriber { SubscriberId = "374830", Email = Email }, 1);

            await h.ProcessWebhookAsync("repeat_payment_success", amount: h.Data.Charge, transactionId: "TX-MASK-1");

            var resolvedLog = await h.Db.PlatformAuditLogs
                .FirstAsync(l => l.Action == PlatformAuditActions.ProviderSubscriberResolved);

            // El id completo NUNCA aparece en claro; solo el sufijo enmascarado.
            Assert.DoesNotContain("374830", resolvedLog.Reason ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("***4830", resolvedLog.Reason ?? string.Empty, StringComparison.Ordinal);
        }

        // ── Helpers ──

        private static void AssertPlatformPolicy(Type controllerType)
        {
            var attribute = controllerType
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .FirstOrDefault();

            Assert.NotNull(attribute);
            Assert.Equal(PlatformAuthorizationPolicies.PlatformSuperAdmin, attribute!.Policy);
        }

        private static void SeedAppliedUpgradeIntent(Harness h, string oldSubscriber, string newSubscriber)
        {
            h.Db.PlanChangeIntents.Add(new PlanChangeIntent
            {
                Id = Guid.NewGuid(),
                TenantId = h.TenantId,
                FromPlanCode = "LC_M_01",
                FromTilopayRecurringPlanId = 6119,
                FromProviderSubscriptionId = oldSubscriber,
                ToPlanId = h.PlanId,
                ToPlanCode = "LC_M_02",
                ToWorkerCount = 2,
                ToBillingCycle = BillingCycle.Monthly,
                ToTilopayRecurringPlanId = 6126,
                Estado = PlanChangeIntentState.Applied,
                OldProviderCancellation = ProviderCancellationState.PendingManualCancellation,
                NewProviderSubscriptionId = newSubscriber,
                AppliedAtUtc = DateTime.UtcNow
            });
        }

        /// <summary>Cliente admin fake, programable y con contadores de invocación.</summary>
        private sealed class FakeAdmin : ITilopayRepeatAdminService
        {
            public bool IsEnabled { get; set; } = true;
            public SubscriberResolutionResult ResolutionResult { get; set; } = SubscriberResolutionResult.NotFound();
            public TilopayAdminOperationResult RecurrentUrl { get; set; } = TilopayAdminOperationResult.Ok("ok", "https://tp.cr/l/recurrent-link");
            public TilopayAdminOperationResult DeleteResult { get; set; } = TilopayAdminOperationResult.Ok("ok");
            public TilopayAdminOperationResult EditResult { get; set; } = TilopayAdminOperationResult.Ok("edited");
            /// <summary>Lo que devuelve getSuscriptorRepeat (usado por la verificación de idempotencia).</summary>
            public List<TilopaySubscriber> GetSubscribers { get; } = new();
            public List<string> DeletedSubscriberIds { get; } = new();

            public Task<IReadOnlyList<TilopaySubscriber>> GetSuscriptorRepeatAsync(int tilopayPlanId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<TilopaySubscriber>>(GetSubscribers.ToList());

            public Task<SubscriberResolutionResult> ResolveSubscriberAsync(int tilopayPlanId, string? email, CancellationToken cancellationToken = default) =>
                Task.FromResult(ResolutionResult);

            public Task<TilopayAdminOperationResult> GetRecurrentUrlAsync(int tilopayPlanId, string? email, CancellationToken cancellationToken = default) =>
                Task.FromResult(RecurrentUrl);

            public Task<TilopayAdminOperationResult> PauseSubscriberAsync(string subscriberId, CancellationToken cancellationToken = default) =>
                Task.FromResult(TilopayAdminOperationResult.Ok("paused"));

            public Task<TilopayAdminOperationResult> ReactivateSubscriberAsync(string subscriberId, CancellationToken cancellationToken = default) =>
                Task.FromResult(TilopayAdminOperationResult.Ok("reactivated"));

            public Task<TilopayAdminOperationResult> DeleteSubscriberAsync(string subscriberId, CancellationToken cancellationToken = default)
            {
                DeletedSubscriberIds.Add(subscriberId);
                return Task.FromResult(DeleteResult);
            }

            public Task<TilopayAdminOperationResult> EditSubscriberStatusAsync(string subscriberId, TilopaySubscriberStatus status, CancellationToken cancellationToken = default) =>
                Task.FromResult(EditResult);
        }

        private sealed class FakeProvider : IPaymentProvider
        {
            public PaymentProviderType ProviderType => PaymentProviderType.Tilopay;
            public PaymentProviderWebhookData WebhookData { get; set; } = new() { ProviderType = PaymentProviderType.Tilopay, IsRecurring = true };

            public Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken = default) =>
                Task.FromResult(new PaymentCheckoutResult { ProviderType = PaymentProviderType.Tilopay, RedirectUrl = request.SuccessUrl });

            public PaymentProviderWebhookData ParseWebhook(string payload) => WebhookData;

            public Task<PaymentVerificationResult> VerifyPaymentAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default) =>
                Task.FromResult(new PaymentVerificationResult { ProviderType = PaymentProviderType.Tilopay, Exists = true, IsSuccess = true, Reference = request.Reference });
        }

        private sealed class Harness : IDisposable
        {
            private readonly IDisposable _connection;
            public ApplicationDbContext Db { get; private init; } = null!;
            public FakeAdmin Admin { get; } = new();
            public FakeProvider Provider { get; } = new();
            public SaaSPaymentService Payments { get; private set; } = null!;
            public BillingReconciliationService Reconciliation { get; private set; } = null!;
            public ProviderSubscriptionManager ProviderManager { get; private set; } = null!;
            public BillingHealthService Health { get; private set; } = null!;
            public Guid TenantId { get; } = Guid.NewGuid();
            public Guid PlanId { get; private set; }
            public CalculatorPlanData Data { get; private set; } = null!;

            private string _reference = string.Empty;

            private Harness(IDisposable connection) => _connection = connection;

            public static async Task<Harness> CreateAsync(int workers)
            {
                var (context, connection) = TestDbContextFactory.CreateSqliteContext(new TestTenantProvider());
                var h = new Harness(connection) { Db = context };
                h.Data = CalculatorCatalog.Find(workers, BillingCycle.Monthly);

                var repeatOptions = CalculatorCatalog.BuildRepeatOptions();
                var adminOptions = Options.Create(new OpcionesTilopayRepeatAdmin { Enabled = true, BlockDuplicateCheckout = true, AutoCancelOldSubscriberOnUpgrade = true });
                // Reloj anclado a "ahora" para que las marcas de auditoría caigan dentro de las
                // ventanas de tiempo reales (UtcNow) que usa BillingHealthService. Kind=Unspecified
                // porque FixedBusinessDateTimeProvider aplica un offset -6 al DateTime recibido.
                var clock = new FixedBusinessDateTimeProvider(
                    DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified));
                var tenantAccessor = new TenantExecutionContextAccessor();
                var cache = new MemoryCache(new MemoryCacheOptions());
                var subscriptionService = new SuscripcionService(context, cache, new TenantCommercialAccessCache(cache), clock, Options.Create(repeatOptions), NullLogger<SuscripcionService>.Instance);

                var resolutionService = new SubscriberResolutionService(context, h.Admin, tenantAccessor, clock, NullLogger<SubscriberResolutionService>.Instance);
                h.ProviderManager = new ProviderSubscriptionManager(context, h.Admin, tenantAccessor, clock, adminOptions, NullLogger<ProviderSubscriptionManager>.Instance);

                h.Payments = new SaaSPaymentService(
                    context,
                    new PaymentProviderResolver(new IPaymentProvider[] { h.Provider }),
                    subscriptionService,
                    tenantAccessor,
                    Options.Create(new OpcionesPago { ProveedorPredeterminado = PaymentProviderType.Tilopay }),
                    Options.Create(new OpcionesTilopay { MerchantId = "m", WebhookAccessToken = "t" }),
                    Options.Create(repeatOptions),
                    NullLogger<SaaSPaymentService>.Instance,
                    environment: null,
                    planChangeService: new PlanChangeService(context, NullLogger<PlanChangeService>.Instance),
                    subscriberResolutionService: resolutionService,
                    tilopayRepeatAdminService: h.Admin,
                    tilopayRepeatAdminOptions: adminOptions,
                    providerSubscriptionManager: h.ProviderManager);

                h.Reconciliation = new BillingReconciliationService(
                    context,
                    subscriptionService,
                    tenantAccessor,
                    clock,
                    Options.Create(repeatOptions),
                    Options.Create(new BillingReconciliationOptions()),
                    NullLogger<BillingReconciliationService>.Instance,
                    resolutionService,
                    adminOptions,
                    h.ProviderManager);

                h.Health = new BillingHealthService(context, subscriptionService);

                context.Tenants.Add(new Tenant { Id = h.TenantId, Nombre = "Tenant Sub", Activo = true });
                var plan = new Plan
                {
                    Id = Guid.NewGuid(),
                    Codigo = h.Data.Code,
                    Nombre = "Plan sub",
                    PrecioMensual = h.Data.Charge,
                    MonthlyEquivalentAmount = h.Data.MonthlyEquivalent,
                    BillingCycle = h.Data.Cycle,
                    Moneda = "CRC",
                    MaxFuncionarios = h.Data.Workers,
                    Activo = true
                };
                context.Planes.Add(plan);
                h.PlanId = plan.Id;
                await context.SaveChangesAsync();

                return h;
            }

            public async Task StartCheckoutAsync()
            {
                var checkout = await Payments.CreateRecurringCheckoutAsync(TenantId, PlanId, "Owner", Email);
                _reference = checkout.ProviderReference ?? string.Empty;
            }

            public Task ProcessWebhookAsync(string eventType, decimal? amount, string transactionId)
            {
                Provider.WebhookData = new PaymentProviderWebhookData
                {
                    ProviderType = PaymentProviderType.Tilopay,
                    EventType = eventType == "repeat_registration" ? "tilopay.repeat.notification" : "tilopay.repeat.notification",
                    Reference = _reference,
                    RecurringPlanId = Data.RecurringPlanId,
                    CustomerEmail = Email,
                    Amount = amount,
                    Currency = "CRC",
                    ProviderTransactionId = transactionId,
                    IsRecurring = true
                };

                return Payments.ProcessTilopayWebhookAsync("{\"x\":1}", $"corr-{transactionId}", eventType);
            }

            public Task<int> CountAuditAsync(string action) =>
                Db.PlatformAuditLogs.CountAsync(l => l.Action == action);

            public void Dispose()
            {
                Db.Dispose();
                _connection.Dispose();
            }
        }
    }
}
