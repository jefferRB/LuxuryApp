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

            h.Admin.DeleteResult = TilopayAdminOperationResult.Fail("TiloPay rechazó la baja");

            await h.ProviderManager.TryCancelOldSubscriberForUpgradeAsync(h.TenantId);

            var intent = await h.Db.PlanChangeIntents.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(ProviderCancellationState.PendingManualCancellation, intent.OldProviderCancellation);
            Assert.Equal(1, await h.CountAuditAsync(PlatformAuditActions.UpgradeOldProviderSubscriptionCancellationFailed));

            var health = await h.Health.BuildAsync();
            Assert.True(health.ProviderCancellationsFailedLast7d >= 1);
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
            public List<string> DeletedSubscriberIds { get; } = new();

            public Task<IReadOnlyList<TilopaySubscriber>> GetSuscriptorRepeatAsync(int tilopayPlanId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<TilopaySubscriber>>(
                    ResolutionResult.Subscriber is { } s ? new[] { s } : Array.Empty<TilopaySubscriber>());

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
                Task.FromResult(TilopayAdminOperationResult.Ok("edited"));
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
                    adminOptions);

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
