using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;

namespace LuxuryApp.Services.Tilopay
{
    public class TilopayService : IPaymentProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TilopayService> _logger;
        private readonly OpcionesTilopay _options;
        private readonly AsyncPolicyWrap<HttpResponseMessage> _safeReadPolicy;

        public TilopayService(
            HttpClient httpClient,
            IMemoryCache cache,
            IOptions<OpcionesTilopay> options,
            ILogger<TilopayService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
            _options = options.Value;

            var retry = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(response => IsTransientStatus(response.StatusCode))
                .WaitAndRetryAsync(
                    2,
                    attempt => TimeSpan.FromSeconds(attempt * 2),
                    (result, delay, attempt, _) =>
                    {
                        _logger.LogWarning(
                            "Tilopay retry {Attempt} en lectura segura. Delay {DelaySeconds}s. Status {StatusCode}",
                            attempt,
                            delay.TotalSeconds,
                            result.Result?.StatusCode);
                    });

            var circuit = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .OrResult(response => IsTransientStatus(response.StatusCode))
                .CircuitBreakerAsync(
                    5,
                    TimeSpan.FromSeconds(30),
                    onBreak: (result, delay) =>
                    {
                        _logger.LogError(
                            "Circuit breaker Tilopay abierto por {DelaySeconds}s. Status {StatusCode}",
                            delay.TotalSeconds,
                            result.Result?.StatusCode);
                    },
                    onReset: () => _logger.LogInformation("Circuit breaker Tilopay restablecido."));

            _safeReadPolicy = Policy.WrapAsync(retry, circuit);
        }

        public PaymentProviderType ProviderType => PaymentProviderType.Tilopay;

        public async Task<PaymentCheckoutResult> CreateCheckoutAsync(
            PaymentCheckoutRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateApiCredentials();

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["Provider"] = ProviderType,
                ["TenantId"] = request.TenantId,
                ["PlanId"] = request.PlanId,
                ["Reference"] = request.Reference
            });

            var accessToken = await GetApiTokenAsync(cancellationToken);

            using var linkedCts = CreateTimeout(cancellationToken);
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/createLinkPayment")
            {
                Content = JsonContent.Create(new TilopayCreateLinkRequest
                {
                    key = _options.ApiKey,
                    amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    currency = request.Currency.ToUpperInvariant(),
                    reference = request.Reference,
                    type = 1,
                    description = request.Description,
                    client = request.CustomerName,
                    callback_url = request.SuccessUrl,
                    webhook_url = request.WebhookUrl
                })
            };

            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(message, linkedCts.Token);
            var raw = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Tilopay createLinkPayment devolvió error. Status {StatusCode}. Body {Body}",
                    response.StatusCode,
                    raw);

                throw new InvalidOperationException("Tilopay no pudo generar el checkout.");
            }

            var result = JsonSerializer.Deserialize<TilopayCreateLinkResponse>(raw, JsonOptions)
                ?? throw new InvalidOperationException("Tilopay devolvió una respuesta inválida al crear el checkout.");

            if (!string.Equals(result.type, "200", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(result.url))
            {
                _logger.LogError(
                    "Tilopay createLinkPayment respondió sin URL utilizable. Type {Type}. Message {Message}. Body {Body}",
                    result.type,
                    result.message,
                    raw);

                throw new InvalidOperationException("Tilopay no devolvió un checkout válido.");
            }

            _logger.LogInformation(
                "Checkout Tilopay generado correctamente. Reference {Reference}. LinkId {LinkId}",
                request.Reference,
                result.id);

            return new PaymentCheckoutResult
            {
                ProviderType = ProviderType,
                RedirectUrl = result.url,
                ProviderCheckoutId = result.id?.ToString(CultureInfo.InvariantCulture),
                ProviderReference = request.Reference,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                WebhookUrl = request.WebhookUrl,
                RawResponse = raw,
                CorrelationId = request.Reference
            };
        }

        public PaymentProviderWebhookData ParseWebhook(string payload)
        {
            var webhook = JsonSerializer.Deserialize<TilopayLinkWebhookPayload>(payload, JsonOptions)
                ?? throw new PaymentWebhookValidationException("Tilopay webhook sin payload valido.");

            var internalReference = NormalizeOptionalValue(webhook.reference) ??
                NormalizeOptionalValue(webhook.orderNumber);

            if (string.IsNullOrWhiteSpace(internalReference))
            {
                throw new PaymentWebhookValidationException("Tilopay webhook sin referencia utilizable.");
            }

            var providerOrderNumber = NormalizeProviderOrderNumber(webhook.orderNumber);
            var providerTransactionId = ReadPositiveLongAsString(webhook.tilopayOrderId);
            var providerCheckoutId = ReadPositiveLongAsString(webhook.tilopayLinkId);

            var eventId = providerTransactionId is not null
                ? $"tilopay-link-{providerTransactionId}"
                : $"tilopay-link-{providerOrderNumber ?? internalReference}";

            return new PaymentProviderWebhookData
            {
                ProviderType = ProviderType,
                EventId = eventId,
                EventType = "tilopay.link.completed",
                Reference = internalReference,
                ProviderOrderNumber = providerOrderNumber,
                StatusCode = webhook.code ?? string.Empty,
                StatusDescription = webhook.codeDescription ?? string.Empty,
                ProviderCheckoutId = providerCheckoutId,
                ProviderTransactionId = providerTransactionId,
                AuthorizationCode = webhook.auth,
                CardBrand = webhook.creditCardBrand,
                CardLast4 = webhook.last4CreditCardNumber,
                OrderHash = webhook.orderHash,
                RawPayload = payload
            };
        }

        public async Task<PaymentVerificationResult> VerifyPaymentAsync(
            PaymentVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateApiCredentials();

            var lookupReference = NormalizeOptionalValue(request.ProviderOrderNumber) ??
                NormalizeOptionalValue(request.Reference);

            if (string.IsNullOrWhiteSpace(lookupReference))
            {
                throw new ArgumentException("La referencia del pago es obligatoria.", nameof(request));
            }

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["Provider"] = ProviderType,
                ["Reference"] = request.Reference,
                ["ProviderOrderNumber"] = request.ProviderOrderNumber,
                ["LookupReference"] = lookupReference,
                ["MerchantId"] = request.MerchantId ?? _options.MerchantId
            });

            var accessToken = await GetApiTokenAsync(cancellationToken);

            using var linkedCts = CreateTimeout(cancellationToken);

            var response = await _safeReadPolicy.ExecuteAsync(async () =>
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/consult")
                {
                    Content = JsonContent.Create(new TilopayConsultRequest
                    {
                        key = _options.ApiKey,
                        orderNumber = lookupReference,
                        merchantId = NormalizeOptionalValue(request.MerchantId) ??
                            NormalizeOptionalValue(_options.MerchantId)
                    })
                };

                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return await _httpClient.SendAsync(message, linkedCts.Token);
            });

            var raw = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Tilopay consult devolvió error. Status {StatusCode}. Body {Body}",
                    response.StatusCode,
                    raw);

                throw new InvalidOperationException("Tilopay no permitió validar el pago.");
            }

            var consult = JsonSerializer.Deserialize<TilopayConsultResponse>(raw, JsonOptions)
                ?? throw new InvalidOperationException("Tilopay devolvió una respuesta inválida en consult.");

            var tx = consult.response?.FirstOrDefault();
            if (!string.Equals(consult.type, "200", StringComparison.OrdinalIgnoreCase) || tx is null)
            {
                _logger.LogWarning(
                    "Tilopay consult no encontró transacción para la referencia {Reference}. Body {Body}",
                    request.Reference,
                    raw);

                return new PaymentVerificationResult
                {
                    ProviderType = ProviderType,
                    Exists = false,
                    Reference = request.Reference,
                    ProviderOrderNumber = NormalizeProviderOrderNumber(request.ProviderOrderNumber) ?? lookupReference,
                    RawResponse = raw
                };
            }

            var amount = decimal.TryParse(tx.amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedAmount)
                ? parsedAmount
                : 0m;

            var normalizedProviderOrderNumber =
                NormalizeProviderOrderNumber(tx.orderNumber) ??
                NormalizeProviderOrderNumber(request.ProviderOrderNumber) ??
                lookupReference;

            return new PaymentVerificationResult
            {
                ProviderType = ProviderType,
                Exists = true,
                IsSuccess = string.Equals(tx.code, "1", StringComparison.OrdinalIgnoreCase),
                Reference = request.Reference,
                ProviderOrderNumber = normalizedProviderOrderNumber,
                StatusCode = tx.code ?? string.Empty,
                StatusDescription = tx.response ?? string.Empty,
                ProviderTransactionId = tx.id_tilopay?.ToString(CultureInfo.InvariantCulture),
                AuthorizationCode = tx.auth,
                Amount = amount,
                Currency = tx.currency ?? string.Empty,
                ProviderProcessedAtUtc = ParseProviderProcessedAtUtc(tx.date),
                RawResponse = raw
            };
        }

        private async Task<string> GetApiTokenAsync(CancellationToken cancellationToken)
        {
            ValidateApiCredentials();

            var cacheKey = $"tilopay_api_token::{_options.ApiUser}";

            if (_cache.TryGetValue<string>(cacheKey, out var cachedToken) && !string.IsNullOrWhiteSpace(cachedToken))
            {
                return cachedToken;
            }

            using var linkedCts = CreateTimeout(cancellationToken);

            var response = await _safeReadPolicy.ExecuteAsync(() =>
                _httpClient.PostAsJsonAsync(
                    "api/v1/login",
                    new TilopayLoginRequest
                    {
                        apiuser = _options.ApiUser,
                        password = _options.ApiPassword
                    },
                    linkedCts.Token));

            var raw = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Tilopay login devolvió error. Status {StatusCode}. Body {Body}",
                    response.StatusCode,
                    raw);

                throw new InvalidOperationException("No fue posible autenticarse contra Tilopay.");
            }

            var login = JsonSerializer.Deserialize<TilopayLoginResponse>(raw, JsonOptions)
                ?? throw new InvalidOperationException("Tilopay devolvió una respuesta inválida al autenticarse.");

            if (string.IsNullOrWhiteSpace(login.access_token))
            {
                throw new InvalidOperationException("Tilopay no devolvió access_token.");
            }

            var expiresSeconds = ParseExpiresInSeconds(login.expires_in);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(300, expiresSeconds - 300));

            _cache.Set(cacheKey, login.access_token, expiresAt);

            return login.access_token;
        }

        private static bool IsTransientStatus(HttpStatusCode statusCode) =>
            statusCode == HttpStatusCode.RequestTimeout ||
            statusCode == HttpStatusCode.BadGateway ||
            statusCode == HttpStatusCode.ServiceUnavailable ||
            statusCode == HttpStatusCode.GatewayTimeout ||
            (int)statusCode >= 500;

        private static int ParseExpiresInSeconds(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 3600;
        }

        private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
            return cts;
        }

        private void ValidateApiCredentials()
        {
            if (string.IsNullOrWhiteSpace(_options.ApiUser) ||
                string.IsNullOrWhiteSpace(_options.ApiPassword) ||
                string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new PaymentProviderConfigurationException(
                    "Tilopay no esta configurado. Debe definir Tilopay:ApiUser, Tilopay:ApiPassword y Tilopay:ApiKey.");
            }
        }

        private static string? NormalizeOptionalValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : value.Trim();
        }

        private static string? NormalizeProviderOrderNumber(string? value)
        {
            var normalized = NormalizeOptionalValue(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            var separatorIndex = normalized.LastIndexOf('-');
            if (separatorIndex > 0 && separatorIndex < normalized.Length - 1)
            {
                var suffix = normalized[(separatorIndex + 1)..];
                if (suffix.Contains('_', StringComparison.Ordinal))
                {
                    return suffix;
                }
            }

            return normalized;
        }

        private static string? ReadPositiveLongAsString(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric) && numeric > 0)
            {
                return numeric.ToString(CultureInfo.InvariantCulture);
            }

            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                parsed > 0)
            {
                return parsed.ToString(CultureInfo.InvariantCulture);
            }

            return null;
        }

        private static DateTime? ParseProviderProcessedAtUtc(string? providerDate)
        {
            if (string.IsNullOrWhiteSpace(providerDate))
            {
                return null;
            }

            if (DateTime.TryParseExact(
                providerDate,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return null;
        }

        private sealed class TilopayLoginRequest
        {
            public string apiuser { get; set; } = string.Empty;
            public string password { get; set; } = string.Empty;
        }

        private sealed class TilopayLoginResponse
        {
            public string access_token { get; set; } = string.Empty;
            public JsonElement expires_in { get; set; }
        }

        private sealed class TilopayCreateLinkRequest
        {
            public string key { get; set; } = string.Empty;
            public string amount { get; set; } = string.Empty;
            public string currency { get; set; } = string.Empty;
            public string reference { get; set; } = string.Empty;
            public int type { get; set; }
            public string description { get; set; } = string.Empty;
            public string client { get; set; } = string.Empty;
            public string callback_url { get; set; } = string.Empty;
            public string webhook_url { get; set; } = string.Empty;
        }

        private sealed class TilopayCreateLinkResponse
        {
            public string type { get; set; } = string.Empty;
            public string message { get; set; } = string.Empty;
            public string url { get; set; } = string.Empty;
            public int? id { get; set; }
        }

        private sealed class TilopayLinkWebhookPayload
        {
            public string? code { get; set; }
            public string? codeDescription { get; set; }
            public string? auth { get; set; }
            public JsonElement tilopayLinkId { get; set; }
            public string? orderNumber { get; set; }
            public string? reference { get; set; }
            public JsonElement tilopayOrderId { get; set; }
            public string? creditCardToken { get; set; }
            public string? creditCardBrand { get; set; }
            public string? last4CreditCardNumber { get; set; }
            public string? linkDescription { get; set; }
            public string? orderHash { get; set; }
        }

        private sealed class TilopayConsultRequest
        {
            public string key { get; set; } = string.Empty;
            public string orderNumber { get; set; } = string.Empty;
            public string? merchantId { get; set; }
        }

        private sealed class TilopayConsultResponse
        {
            public string type { get; set; } = string.Empty;
            public string? message { get; set; }
            public List<TilopayConsultItem>? response { get; set; }
        }

        private sealed class TilopayConsultItem
        {
            public long? id_tilopay { get; set; }
            public string? orderNumber { get; set; }
            public string? amount { get; set; }
            public string? currency { get; set; }
            public string? code { get; set; }
            public string? response { get; set; }
            public string? auth { get; set; }
            public string? date { get; set; }
        }
    }
}
