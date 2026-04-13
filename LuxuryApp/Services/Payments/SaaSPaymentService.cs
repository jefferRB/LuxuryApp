using System.Text.Json;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Payments
{
    public class SaaSPaymentService
    {
        private readonly ApplicationDbContext _db;
        private readonly PaymentProviderResolver _providerResolver;
        private readonly SuscripcionService _suscripcionService;
        private readonly ILogger<SaaSPaymentService> _logger;
        private readonly OpcionesPago _paymentOptions;
        private readonly OpcionesTilopay _tilopayOptions;

        public SaaSPaymentService(
            ApplicationDbContext db,
            PaymentProviderResolver providerResolver,
            SuscripcionService suscripcionService,
            IOptions<OpcionesPago> paymentOptions,
            IOptions<OpcionesTilopay> tilopayOptions,
            ILogger<SaaSPaymentService> logger)
        {
            _db = db;
            _providerResolver = providerResolver;
            _suscripcionService = suscripcionService;
            _logger = logger;
            _paymentOptions = paymentOptions.Value;
            _tilopayOptions = tilopayOptions.Value;
        }

        public async Task<PaymentCheckoutResult> CreateCheckoutAsync(
            Guid tenantId,
            Guid planId,
            string customerName,
            string customerEmail,
            string successUrl,
            string cancelUrl,
            string webhookUrl,
            CancellationToken cancellationToken = default)
        {
            var providerType = _paymentOptions.ProveedorPredeterminado;
            var provider = _providerResolver.Get(providerType);

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId && t.Activo, cancellationToken)
                ?? throw new InvalidOperationException("Tenant no encontrado o inactivo.");

            var plan = await _db.Planes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId && p.Activo, cancellationToken)
                ?? throw new InvalidOperationException("Plan no encontrado o inactivo.");

            var reference = GenerateReference(tenantId);
            var existingSubscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsTracking()
                .SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["PlanId"] = planId,
                ["Provider"] = providerType,
                ["Reference"] = reference
            });

            var intento = new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Proveedor = providerType,
                Estado = EstadoPagoProveedor.Pendiente,
                ReferenciaInterna = reference,
                ProviderReference = reference,
                ClienteNombre = string.IsNullOrWhiteSpace(customerName) ? tenant.Nombre : customerName,
                ClienteEmail = customerEmail,
                Descripcion = $"Suscripción plan {plan.Nombre}",
                Monto = plan.PrecioMensual,
                Moneda = string.IsNullOrWhiteSpace(plan.Moneda) ? "CRC" : plan.Moneda.ToUpperInvariant(),
                FechaCreacionUtc = DateTime.UtcNow,
                FechaActualizacionUtc = DateTime.UtcNow
            };

            _db.PagosSuscripcion.Add(intento);

            if (existingSubscription is null)
            {
                _db.Suscripciones.Add(new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PlanId = planId,
                    Proveedor = providerType,
                    ProviderReference = reference,
                    Estado = EstadoSuscripcion.Pendiente,
                    FechaInicio = DateTime.UtcNow,
                    FechaUltimaActualizacionUtc = DateTime.UtcNow,
                    MotivoEstado = "Checkout iniciado; pendiente de confirmacion del proveedor."
                });
            }
            else if (existingSubscription.Estado != EstadoSuscripcion.Activa &&
                     existingSubscription.Estado != EstadoSuscripcion.Trial)
            {
                existingSubscription.PlanId = planId;
                existingSubscription.Proveedor = providerType;
                existingSubscription.ProviderReference = reference;
                existingSubscription.Estado = EstadoSuscripcion.Pendiente;
                existingSubscription.FechaUltimaActualizacionUtc = DateTime.UtcNow;
                existingSubscription.MotivoEstado = "Checkout iniciado; pendiente de confirmacion del proveedor.";
            }

            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var checkout = await provider.CreateCheckoutAsync(
                    new PaymentCheckoutRequest
                    {
                        TenantId = tenantId,
                        PlanId = planId,
                        ProviderType = providerType,
                        Reference = reference,
                        Amount = intento.Monto,
                        Currency = intento.Moneda,
                        Description = intento.Descripcion,
                        CustomerName = intento.ClienteNombre ?? tenant.Nombre,
                        CustomerEmail = customerEmail,
                        SuccessUrl = successUrl,
                        CancelUrl = cancelUrl,
                        WebhookUrl = webhookUrl
                    },
                    cancellationToken);

                intento.ProviderCheckoutId = checkout.ProviderCheckoutId;
                intento.ProviderReference = checkout.ProviderReference ?? reference;
                intento.CheckoutUrl = checkout.RedirectUrl;
                intento.ProviderResultCode = "CHECKOUT_CREATED";
                intento.ProviderResultMessage = "Checkout creado correctamente.";
                intento.UltimoPayloadProveedor = BuildCheckoutAuditPayload(
                    successUrl,
                    cancelUrl,
                    SanitizeSensitiveUrl(webhookUrl),
                    checkout);
                intento.FechaActualizacionUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Checkout preparado correctamente para el tenant {TenantId} y plan {PlanId}.",
                    tenantId,
                    planId);

                return checkout;
            }
            catch (Exception ex)
            {
                intento.ProviderResultCode = "CHECKOUT_ERROR";
                intento.ProviderResultMessage = Trim(ex.Message, 300);
                intento.UltimoPayloadProveedor = BuildCheckoutErrorAuditPayload(
                    successUrl,
                    cancelUrl,
                    SanitizeSensitiveUrl(webhookUrl),
                    ex.Message);
                intento.FechaActualizacionUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogError(ex, "Error creando checkout para la referencia {Reference}.", reference);
                throw;
            }
        }

        public async Task<PaymentWebhookProcessingResult> ProcessTilopayWebhookAsync(
            string payload,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            var provider = _providerResolver.Get(PaymentProviderType.Tilopay);
            var webhook = provider.ParseWebhook(payload);

            if (!IsRecognizedInternalReference(webhook.Reference))
            {
                throw new PaymentWebhookValidationException(
                    "Tilopay webhook con referencia que no fue emitida por el sistema.");
            }

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["Provider"] = webhook.ProviderType,
                ["EventId"] = webhook.EventId,
                ["Reference"] = webhook.Reference,
                ["ProviderOrderNumber"] = webhook.ProviderOrderNumber,
                ["ProviderCheckoutId"] = webhook.ProviderCheckoutId,
                ["CorrelationId"] = correlationId
            });

            var existingEvent = await _db.EventosPago
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    evento => evento.Proveedor == webhook.ProviderType &&
                              evento.ProveedorEventId == webhook.EventId,
                    cancellationToken);

            if (existingEvent is not null && IsTerminal(existingEvent))
            {
                _logger.LogWarning(
                    "Webhook duplicado ignorado. EventId {EventId}. Estado {Estado}.",
                    existingEvent.ProveedorEventId,
                    existingEvent.EstadoProcesamiento);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsDuplicate = true,
                    IsProcessed = true,
                    Message = "Evento duplicado"
                };
            }

            var intento = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    pago => pago.Proveedor == webhook.ProviderType &&
                            (pago.ReferenciaInterna == webhook.Reference ||
                             pago.ProviderReference == webhook.Reference),
                    cancellationToken);

            if (intento is null && !string.IsNullOrWhiteSpace(webhook.ProviderCheckoutId))
            {
                intento = await _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        pago => pago.Proveedor == webhook.ProviderType &&
                                pago.ProviderCheckoutId == webhook.ProviderCheckoutId,
                        cancellationToken);
            }

            var resolvedProviderReference = ResolveProviderReference(webhook);

            var evento = existingEvent;
            if (evento is null)
            {
                evento = new EventoPago
                {
                    Id = Guid.NewGuid(),
                    Proveedor = webhook.ProviderType,
                    TenantId = intento?.TenantId,
                    PlanId = intento?.PlanId,
                    PagoSuscripcionId = intento?.Id,
                    ProveedorEventId = webhook.EventId,
                    Tipo = webhook.EventType,
                    ReferenciaExterna = resolvedProviderReference,
                    ProviderTransactionId = webhook.ProviderTransactionId,
                    CorrelationId = correlationId,
                    Payload = payload,
                    EstadoProcesamiento = "Recibido",
                    FechaRecepcionUtc = DateTime.UtcNow,
                    Procesado = false
                };

                _db.EventosPago.Add(evento);

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    _db.Entry(evento).State = EntityState.Detached;

                    existingEvent = await _db.EventosPago
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(
                            current => current.Proveedor == webhook.ProviderType &&
                                       current.ProveedorEventId == webhook.EventId,
                            cancellationToken);

                    if (existingEvent is null)
                    {
                        throw;
                    }

                    if (IsTerminal(existingEvent))
                    {
                        _logger.LogWarning("Webhook duplicado detectado por restricción única.");

                        return new PaymentWebhookProcessingResult
                        {
                            EventId = webhook.EventId,
                            Reference = webhook.Reference,
                            IsDuplicate = true,
                            IsProcessed = true,
                            Message = "Evento duplicado"
                        };
                    }

                    evento = existingEvent;
                }
            }

            evento.TenantId = intento?.TenantId ?? evento.TenantId;
            evento.PlanId = intento?.PlanId ?? evento.PlanId;
            evento.PagoSuscripcionId = intento?.Id ?? evento.PagoSuscripcionId;
            evento.Tipo = webhook.EventType;
            evento.ReferenciaExterna = resolvedProviderReference;
            evento.ProviderTransactionId = webhook.ProviderTransactionId ?? evento.ProviderTransactionId;
            evento.CorrelationId = correlationId;
            evento.Payload = payload;
            evento.EstadoProcesamiento = "Recibido";
            evento.FechaRecepcionUtc = DateTime.UtcNow;
            evento.FechaProcesamientoUtc = null;
            evento.Procesado = false;
            evento.Error = null;

            await _db.SaveChangesAsync(cancellationToken);

            if (intento is null)
            {
                evento.EstadoProcesamiento = "SinRelacion";
                evento.Error = "No existe un intento de pago asociado a la referencia recibida.";
                evento.FechaProcesamientoUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogError("Webhook recibido sin intento de pago asociado.");

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = false,
                    Message = "No existe intento de pago asociado."
                };
            }

            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;

            try
            {
                var verification = await provider.VerifyPaymentAsync(
                    new PaymentVerificationRequest
                    {
                        ProviderType = webhook.ProviderType,
                        Reference = webhook.Reference,
                        ProviderOrderNumber = webhook.ProviderOrderNumber,
                        MerchantId = string.IsNullOrWhiteSpace(_tilopayOptions.MerchantId)
                            ? null
                            : _tilopayOptions.MerchantId
                    },
                    cancellationToken);

                if (!verification.Exists)
                {
                    throw new InvalidOperationException(
                        $"Tilopay no devolvio una transaccion verificable para la referencia '{webhook.Reference}'.");
                }

                EnsureVerificationMatchesAttempt(intento, webhook, verification);

                transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

                intento.ProviderCheckoutId = webhook.ProviderCheckoutId ?? intento.ProviderCheckoutId;
                intento.ProviderTransactionId = verification.ProviderTransactionId ?? webhook.ProviderTransactionId ?? intento.ProviderTransactionId;
                intento.ProviderReference = resolvedProviderReference;
                intento.ProviderAuthorizationCode = verification.AuthorizationCode ?? webhook.AuthorizationCode;
                intento.ProviderCardBrand = webhook.CardBrand ?? intento.ProviderCardBrand;
                intento.ProviderCardLast4 = webhook.CardLast4 ?? intento.ProviderCardLast4;
                intento.ProviderResultCode = verification.StatusCode;
                intento.ProviderResultMessage = verification.StatusDescription;
                intento.Monto = verification.Amount > 0 ? verification.Amount : intento.Monto;
                intento.Moneda = string.IsNullOrWhiteSpace(verification.Currency) ? intento.Moneda : verification.Currency;
                intento.UltimoPayloadProveedor = verification.RawResponse;
                intento.FechaActualizacionUtc = DateTime.UtcNow;

                if (verification.IsSuccess)
                {
                    intento.Estado = EstadoPagoProveedor.Confirmado;
                    intento.FechaConfirmacionUtc = DateTime.UtcNow;

                    await _suscripcionService.ActivarSuscripcionAsync(
                        intento.TenantId,
                        intento.PlanId,
                        intento.Proveedor,
                        providerCustomerId: null,
                        providerSubscriptionId: null,
                        providerPaymentLinkId: intento.ProviderCheckoutId,
                        providerTransactionId: intento.ProviderTransactionId,
                        providerReference: intento.ProviderReference,
                        trialEnd: null,
                        motivo: "Pago validado por webhook Tilopay.",
                        cancellationToken: cancellationToken);

                    await _suscripcionService.RegistrarPagoConfirmadoAsync(
                        intento.TenantId,
                        intento.PlanId,
                        intento.Id,
                        intento.Proveedor,
                        intento.ProviderReference ?? intento.ReferenciaInterna,
                        intento.ProviderTransactionId,
                        intento.ProviderAuthorizationCode,
                        intento.Monto,
                        intento.Moneda,
                        "Pago confirmado por Tilopay.",
                        cancellationToken);
                }
                else
                {
                    intento.Estado = MapFailedStatus(verification.StatusCode);

                    await _suscripcionService.RegistrarPagoFallidoAsync(
                        intento.TenantId,
                        intento.PlanId,
                        intento.Id,
                        intento.Proveedor,
                        intento.ProviderReference ?? intento.ReferenciaInterna,
                        intento.ProviderTransactionId,
                        intento.Monto,
                        intento.Moneda,
                        $"Pago no aprobado por Tilopay. Código {verification.StatusCode}.",
                        cancellationToken);
                }

                await SetLastProviderEventAsync(intento.TenantId, webhook.EventId, cancellationToken);

                evento.TenantId = intento.TenantId;
                evento.PlanId = intento.PlanId;
                evento.PagoSuscripcionId = intento.Id;
                evento.ProviderTransactionId = intento.ProviderTransactionId;
                evento.EstadoProcesamiento = "Procesado";
                evento.Procesado = true;
                evento.FechaProcesamientoUtc = DateTime.UtcNow;
                evento.Error = null;

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Webhook Tilopay procesado correctamente. Estado final {Estado}.",
                    intento.Estado);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = true,
                    Message = "Webhook procesado correctamente.",
                    EstadoPago = intento.Estado
                };
            }
            catch (Exception ex)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                evento.EstadoProcesamiento = "Error";
                evento.Error = Trim(ex.Message, 500);
                evento.FechaProcesamientoUtc = DateTime.UtcNow;

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "No fue posible persistir el error del evento de pago.");
                }

                _logger.LogError(ex, "Error procesando webhook Tilopay.");
                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        public async Task RegisterRejectedWebhookAsync(
            PaymentProviderType provider,
            string payload,
            string reason,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            var evento = new EventoPago
            {
                Id = Guid.NewGuid(),
                Proveedor = provider,
                ProveedorEventId = $"invalid-{Guid.NewGuid():N}",
                Tipo = "provider.webhook.invalid",
                CorrelationId = correlationId,
                Payload = payload,
                Procesado = false,
                EstadoProcesamiento = "Rechazado",
                FechaRecepcionUtc = DateTime.UtcNow,
                FechaProcesamientoUtc = DateTime.UtcNow,
                Error = Trim(reason, 500)
            };

            _db.EventosPago.Add(evento);
            await _db.SaveChangesAsync(cancellationToken);
        }

        private static string BuildCheckoutAuditPayload(
            string successUrl,
            string cancelUrl,
            string webhookUrl,
            PaymentCheckoutResult checkout) =>
            JsonSerializer.Serialize(new
            {
                phase = "checkout_created",
                successUrl,
                cancelUrl,
                webhookUrl,
                redirectUrl = checkout.RedirectUrl,
                providerCheckoutId = checkout.ProviderCheckoutId,
                providerReference = checkout.ProviderReference,
                providerOrderNumber = checkout.ProviderOrderNumber,
                rawResponse = checkout.RawResponse
            });

        private static string BuildCheckoutErrorAuditPayload(
            string successUrl,
            string cancelUrl,
            string webhookUrl,
            string errorMessage) =>
            JsonSerializer.Serialize(new
            {
                phase = "checkout_error",
                successUrl,
                cancelUrl,
                webhookUrl,
                error = errorMessage
            });

        private static string GenerateReference(Guid tenantId)
        {
            var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            return $"LXA-{tenantId.ToString("N")[..6].ToUpperInvariant()}-{suffix}";
        }

        private static bool IsRecognizedInternalReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return false;
            }

            var parts = reference.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !string.Equals(parts[0], "LXA", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return parts[1].Length == 6 &&
                   parts[2].Length == 10 &&
                   parts[1].All(char.IsAsciiHexDigit) &&
                   parts[2].All(char.IsAsciiHexDigit);
        }

        private static void EnsureVerificationMatchesAttempt(
            PagoSuscripcion intento,
            PaymentProviderWebhookData webhook,
            PaymentVerificationResult verification)
        {
            if (!string.IsNullOrWhiteSpace(verification.Reference) &&
                !string.Equals(verification.Reference, intento.ReferenciaInterna, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(verification.Reference, intento.ProviderReference, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"La verificacion del proveedor no coincide con la referencia interna del intento '{intento.ReferenciaInterna}'.");
            }

            var normalizedWebhookOrderNumber = NormalizeProviderOrderNumber(webhook.ProviderOrderNumber);
            var normalizedVerificationOrderNumber = NormalizeProviderOrderNumber(verification.ProviderOrderNumber);

            if (!string.IsNullOrWhiteSpace(normalizedWebhookOrderNumber) &&
                !string.IsNullOrWhiteSpace(normalizedVerificationOrderNumber) &&
                !string.Equals(normalizedWebhookOrderNumber, normalizedVerificationOrderNumber, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La verificacion del proveedor no coincide con el orderNumber reportado por Tilopay.");
            }

            if (!string.IsNullOrWhiteSpace(webhook.ProviderCheckoutId) &&
                !string.IsNullOrWhiteSpace(intento.ProviderCheckoutId) &&
                !string.Equals(webhook.ProviderCheckoutId, intento.ProviderCheckoutId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El webhook recibido no coincide con el checkout emitido para este tenant.");
            }

            if (!string.IsNullOrWhiteSpace(verification.ProviderTransactionId) &&
                !string.IsNullOrWhiteSpace(intento.ProviderTransactionId) &&
                !string.Equals(verification.ProviderTransactionId, intento.ProviderTransactionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("La transaccion verificada no coincide con la transaccion previamente asociada al intento.");
            }

            if (verification.Amount > 0m && Math.Abs(verification.Amount - intento.Monto) > 0.01m)
            {
                throw new InvalidOperationException(
                    $"El monto verificado ({verification.Amount}) no coincide con el monto esperado ({intento.Monto}).");
            }

            if (!string.IsNullOrWhiteSpace(verification.Currency) &&
                !string.Equals(verification.Currency, intento.Moneda, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"La moneda verificada ({verification.Currency}) no coincide con la moneda esperada ({intento.Moneda}).");
            }
        }

        private static string ResolveProviderReference(PaymentProviderWebhookData webhook) =>
            !string.IsNullOrWhiteSpace(webhook.ProviderOrderNumber)
                ? webhook.ProviderOrderNumber
                : webhook.Reference;

        private static string? NormalizeProviderOrderNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            var separatorIndex = trimmed.LastIndexOf('-');
            if (separatorIndex > 0 && separatorIndex < trimmed.Length - 1)
            {
                var suffix = trimmed[(separatorIndex + 1)..];
                if (suffix.Contains('_', StringComparison.Ordinal))
                {
                    return suffix;
                }
            }

            return trimmed;
        }

        private async Task SetLastProviderEventAsync(
            Guid tenantId,
            string eventId,
            CancellationToken cancellationToken)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            if (suscripcion is null)
            {
                return;
            }

            suscripcion.UltimoEventoProveedorId = eventId;
            suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
        }

        private static EstadoPagoProveedor MapFailedStatus(string statusCode) =>
            statusCode switch
            {
                "2" => EstadoPagoProveedor.Fallido,
                "3" => EstadoPagoProveedor.Cancelado,
                _ => EstadoPagoProveedor.Fallido
            };

        private static bool IsTerminal(EventoPago evento) =>
            evento.Procesado ||
            string.Equals(evento.EstadoProcesamiento, "Rechazado", StringComparison.OrdinalIgnoreCase);

        private static string SanitizeSensitiveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var separatorIndex = url.IndexOf('?', StringComparison.Ordinal);
                return separatorIndex >= 0 ? url[..separatorIndex] : url;
            }

            return new UriBuilder(uri)
            {
                Query = string.Empty
            }.Uri.ToString();
        }

        private static string Trim(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];
    }
}
