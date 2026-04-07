using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using System.Net;
using LuxuryApp.Models.Saas;

namespace LuxuryApp.Services.SaaS
{
    public class StripeService
    {
        private readonly ILogger<StripeService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
        private readonly IAsyncPolicy _policyWrap;
        private readonly OpcionesStripe _options;

        public StripeService(
            ILogger<StripeService> logger,
            IOptions<OpcionesStripe> options)
        {
            _logger = logger;
            _options = options.Value;

            // 🔥 Retry inteligente (solo errores transientes)
            _retryPolicy = Policy
                .Handle<StripeException>(IsTransientStripeError)
                .WaitAndRetryAsync(
                    3,
                    retry => TimeSpan.FromSeconds(2 * retry),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception,
                            "Retry {Retry} Stripe | Delay: {Delay}s",
                            retryCount, timeSpan.TotalSeconds);
                    });

            // 🔥 Circuit Breaker (protección contra caídas de Stripe)
            _circuitBreaker = Policy
                .Handle<StripeException>(IsTransientStripeError)
                .CircuitBreakerAsync(
                    5,
                    TimeSpan.FromSeconds(30),
                    onBreak: (ex, ts) =>
                    {
                        _logger.LogError(ex, "Circuit OPEN por {Seconds}s", ts.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit RESET");
                    });

            _policyWrap = Policy.WrapAsync(_retryPolicy, _circuitBreaker);
        }

        private bool IsTransientStripeError(StripeException ex)
        {
            return ex.HttpStatusCode == HttpStatusCode.RequestTimeout ||
                   ex.HttpStatusCode == HttpStatusCode.BadGateway ||
                   ex.HttpStatusCode == HttpStatusCode.ServiceUnavailable;
        }

        // ================================
        // 🔹 CUSTOMER
        // ================================
        public async Task<string> CrearCustomerAsync(
            string email,
            string nombre,
            Guid tenantId,
            string? existingCustomerId,
            CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(existingCustomerId))
            {
                _logger.LogInformation("Customer ya existente | {CustomerId}", existingCustomerId);
                return existingCustomerId;
            }

            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email requerido");

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["TenantId"] = tenantId
            });

            return await _policyWrap.ExecuteAsync(async () =>
            {
                try
                {
                    using var cts = CreateTimeout(ct);

                    var service = new CustomerService();

                    var options = new CustomerCreateOptions
                    {
                        Email = email,
                        Name = nombre,
                        Metadata = new Dictionary<string, string>
                        {
                            { "TenantId", tenantId.ToString() }
                        }
                    };

                    var requestOptions = new RequestOptions
                    {
                        // 🔥 Determinístico (correcto para customer)
                        IdempotencyKey = $"customer-{tenantId}"
                    };

                    var customer = await service.CreateAsync(options, requestOptions, cts.Token);

                    if (customer == null || string.IsNullOrEmpty(customer.Id))
                        throw new Exception("Stripe no retornó customer válido");

                    _logger.LogInformation(
                        "Customer creado | Id: {CustomerId} | Email: {Email}",
                        customer.Id, email);

                    return customer.Id;
                }
                catch (StripeException ex)
                {
                    LogStripeError(ex);
                    throw;
                }
            });
        }

        // ================================
        // 🔹 CHECKOUT
        // ================================
        public async Task<string> CrearCheckoutSesionAsync(
            string customerId,
            string priceId,
            Guid tenantId,
            Guid planId,
            string successUrl,
            string cancelUrl,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(customerId))
                throw new ArgumentException("CustomerId requerido");

            if (string.IsNullOrWhiteSpace(priceId))
                throw new ArgumentException("PriceId requerido");

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["TenantId"] = tenantId,
                ["PlanId"] = planId
            });

            return await _policyWrap.ExecuteAsync(async () =>
            {
                try
                {
                    using var cts = CreateTimeout(ct);

                    var service = new SessionService();

                    var options = new SessionCreateOptions
                    {
                        Customer = customerId,
                        Mode = "subscription",

                        BillingAddressCollection = "required",
                        AllowPromotionCodes = true,

                        LineItems = new List<SessionLineItemOptions>
                        {
                            new SessionLineItemOptions
                            {
                                Price = priceId,
                                Quantity = 1
                            }
                        },

                        Metadata = new Dictionary<string, string>
                        {
                            { "TenantId", tenantId.ToString() },
                            { "PlanId", planId.ToString() }
                        },

                        SubscriptionData = new SessionSubscriptionDataOptions
                        {
                            TrialPeriodDays = _options.DiasPrueba,
                            Metadata = new Dictionary<string, string>
                            {
                                { "TenantId", tenantId.ToString() },
                                { "PlanId", planId.ToString() }
                            }
                        },

                        SuccessUrl = successUrl,
                        CancelUrl = cancelUrl
                    };

                    var requestOptions = new RequestOptions
                    {
                        // 🔥 Balance perfecto (ni fijo ni totalmente random)
                        IdempotencyKey = $"checkout-{tenantId}-{planId}-{DateTime.UtcNow:yyyyMMddHHmm}"
                    };

                    var session = await service.CreateAsync(options, requestOptions, cts.Token);

                    if (session == null || string.IsNullOrEmpty(session.Url))
                        throw new Exception("Stripe no retornó sesión válida");

                    _logger.LogInformation(
                        "Checkout creado | SessionId: {SessionId} | Status: {Status} | Customer: {Customer}",
                        session.Id,
                        session.Status,
                        session.CustomerId);

                    return session.Url;
                }
                catch (StripeException ex)
                {
                    LogStripeError(ex);
                    throw;
                }
            });
        }

        // ================================
        // 🔹 CANCELAR
        // ================================
        public async Task CancelarSuscripcionAsync(
            string subscriptionId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
                throw new ArgumentException("SubscriptionId requerido");

            await _policyWrap.ExecuteAsync(async () =>
            {
                try
                {
                    using var cts = CreateTimeout(ct);

                    var service = new SubscriptionService();

                    var sub = await service.GetAsync(subscriptionId, null, null, cts.Token);

                    if (sub.CancelAtPeriodEnd)
                    {
                        _logger.LogInformation("Ya estaba cancelada | {SubscriptionId}", subscriptionId);
                        return;
                    }

                    var options = new SubscriptionUpdateOptions
                    {
                        CancelAtPeriodEnd = true
                    };

                    var requestOptions = new RequestOptions
                    {
                        IdempotencyKey = $"cancel-{subscriptionId}"
                    };

                    await service.UpdateAsync(subscriptionId, options, requestOptions, cts.Token);

                    _logger.LogInformation("Cancelación programada | {SubscriptionId}", subscriptionId);
                }
                catch (StripeException ex)
                {
                    LogStripeError(ex);
                    throw;
                }
            });
        }

        // ================================
        // 🔹 CAMBIAR PLAN
        // ================================
        public async Task CambiarPlanAsync(
            string subscriptionId,
            string newPriceId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
                throw new ArgumentException("SubscriptionId requerido");

            if (string.IsNullOrWhiteSpace(newPriceId))
                throw new ArgumentException("NewPriceId requerido");

            await _policyWrap.ExecuteAsync(async () =>
            {
                try
                {
                    using var cts = CreateTimeout(ct);

                    var service = new SubscriptionService();

                    var subscription = await service.GetAsync(subscriptionId,
                        new SubscriptionGetOptions
                        {
                            Expand = new List<string> { "items.data.price" }
                        },
                        null,
                        cts.Token);

                    var item = subscription.Items.Data.FirstOrDefault();

                    if (item == null)
                        throw new Exception("Subscription sin items");

                    var options = new SubscriptionUpdateOptions
                    {
                        ProrationBehavior = "create_prorations",
                        Items = new List<SubscriptionItemOptions>
                        {
                            new SubscriptionItemOptions
                            {
                                Id = item.Id,
                                Price = newPriceId
                            }
                        }
                    };

                    var requestOptions = new RequestOptions
                    {
                        IdempotencyKey = $"change-{subscriptionId}-{newPriceId}"
                    };

                    await service.UpdateAsync(subscriptionId, options, requestOptions, cts.Token);

                    _logger.LogInformation(
                        "Plan actualizado | Subscription: {SubscriptionId} | NuevoPrice: {PriceId}",
                        subscriptionId, newPriceId);
                }
                catch (StripeException ex)
                {
                    LogStripeError(ex);
                    throw;
                }
            });
        }

        // ================================
        // 🔧 HELPERS
        // ================================
        private CancellationTokenSource CreateTimeout(CancellationToken ct)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TiempoEspera));
            return cts;
        }

        private void LogStripeError(StripeException ex)
        {
            _logger.LogError(ex,
                "Stripe ERROR | Type: {Type} | Code: {Code} | Message: {Message}",
                ex.StripeError?.Type,
                ex.StripeError?.Code,
                ex.StripeError?.Message);
        }
    }
}