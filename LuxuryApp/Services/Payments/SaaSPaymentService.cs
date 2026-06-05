using System.Text.Json;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using Microsoft.AspNetCore.WebUtilities;
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
        private readonly TilopayRepeatOptions _tilopayRepeatOptions;

        public SaaSPaymentService(
            ApplicationDbContext db,
            PaymentProviderResolver providerResolver,
            SuscripcionService suscripcionService,
            IOptions<OpcionesPago> paymentOptions,
            IOptions<OpcionesTilopay> tilopayOptions,
            IOptions<TilopayRepeatOptions> tilopayRepeatOptions,
            ILogger<SaaSPaymentService> logger)
        {
            _db = db;
            _providerResolver = providerResolver;
            _suscripcionService = suscripcionService;
            _logger = logger;
            _paymentOptions = paymentOptions.Value;
            _tilopayOptions = tilopayOptions.Value;
            _tilopayRepeatOptions = tilopayRepeatOptions.Value;
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

        public async Task<PaymentCheckoutResult> CreateRecurringCheckoutAsync(
            Guid tenantId,
            Guid planId,
            string customerName,
            string customerEmail,
            CancellationToken cancellationToken = default)
        {
            if (!_tilopayRepeatOptions.Enabled)
            {
                throw new InvalidOperationException("Tilopay Repeat no esta habilitado en este entorno.");
            }

            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId && t.Activo, cancellationToken)
                ?? throw new InvalidOperationException("Tenant no encontrado o inactivo.");

            var plan = await _db.Planes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId && p.Activo, cancellationToken)
                ?? throw new InvalidOperationException("Plan no encontrado o inactivo.");

            var repeatRegistration = _tilopayRepeatOptions.FindRegistrationByCode(plan.Codigo);
            if (repeatRegistration is null)
            {
                throw new InvalidOperationException(
                    $"No existe mapping Tilopay Repeat para PlanCode '{plan.Codigo ?? plan.Nombre}'.");
            }

            if (!_tilopayRepeatOptions.UseHostedLinks)
            {
                throw new InvalidOperationException(
                    "Tilopay Repeat solo esta implementado con hosted links en este entorno. Activa TilopayRepeat:UseHostedLinks.");
            }

            if (string.IsNullOrWhiteSpace(_tilopayOptions.WebhookAccessToken))
            {
                throw new InvalidOperationException(
                    "Falta configuracion obligatoria: Tilopay:WebhookAccessToken.");
            }

            if (string.IsNullOrWhiteSpace(repeatRegistration.Plan.CheckoutUrl))
            {
                throw new InvalidOperationException(
                    $"Falta configuracion de checkout recurrente para PlanCode '{repeatRegistration.Plan.Code}'. Key esperada: TilopayRepeat:{repeatRegistration.SectionKey}:CheckoutUrl.");
            }

            var reference = GenerateReference(tenantId);
            var correlationToken = GenerateCorrelationToken();
            var redirectUrl = BuildRecurringCheckoutUrl(
                repeatRegistration.Plan.CheckoutUrl,
                correlationToken,
                customerEmail,
                repeatRegistration.Plan.Code);

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["PlanId"] = planId,
                ["RecurringPlanId"] = repeatRegistration.Plan.TilopayPlanId,
                ["PlanCode"] = repeatRegistration.Plan.Code,
                ["CorrelationToken"] = correlationToken
            });

            var intento = new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = EstadoPagoProveedor.Pendiente,
                ReferenciaInterna = reference,
                ProviderReference = correlationToken,
                TilopayRecurringPlanId = repeatRegistration.Plan.TilopayPlanId,
                CorrelationToken = correlationToken,
                ClienteNombre = string.IsNullOrWhiteSpace(customerName) ? tenant.Nombre : customerName,
                ClienteEmail = customerEmail,
                Descripcion = repeatRegistration.Plan.IsAddon
                    ? $"Add-on recurrente {plan.Nombre}"
                    : $"Suscripcion recurrente {plan.Nombre}",
                Monto = plan.PrecioMensual,
                Moneda = string.IsNullOrWhiteSpace(plan.Moneda) ? "CRC" : plan.Moneda.ToUpperInvariant(),
                CheckoutUrl = redirectUrl,
                ProviderResultCode = "RECURRING_PENDING",
                ProviderResultMessage = "Signup recurrente creado y pendiente de aprobacion por webhook.",
                UltimoPayloadProveedor = JsonSerializer.Serialize(new
                {
                    phase = "recurring_signup_created",
                    redirectUrl,
                    repeatPlanCode = repeatRegistration.Plan.Code,
                    repeatPlanId = repeatRegistration.Plan.TilopayPlanId,
                    isAddon = repeatRegistration.Plan.IsAddon
                }),
                FechaCreacionUtc = DateTime.UtcNow,
                FechaActualizacionUtc = DateTime.UtcNow
            };

            _db.PagosSuscripcion.Add(intento);

            if (!repeatRegistration.Plan.IsAddon)
            {
                var existingSubscription = await _db.Suscripciones
                    .IgnoreQueryFilters()
                    .AsTracking()
                    .SingleOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

                if (existingSubscription is null)
                {
                    _db.Suscripciones.Add(new Suscripcion
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PlanId = planId,
                        CodigoPlan = plan.Codigo ?? plan.Nombre,
                        Proveedor = PaymentProviderType.Tilopay,
                        ProviderReference = correlationToken,
                        Estado = EstadoSuscripcion.Pendiente,
                        FechaInicio = DateTime.UtcNow,
                        FechaUltimaActualizacionUtc = DateTime.UtcNow,
                        TilopayRecurringPlanId = repeatRegistration.Plan.TilopayPlanId,
                        PrecioMensual = plan.PrecioMensual,
                        MonedaFacturacion = plan.Moneda,
                        MaxFuncionarios = plan.MaxFuncionarios,
                        MotivoEstado = "Signup recurrente iniciado; pendiente de confirmacion por webhook."
                    });
                }
                else if (existingSubscription.Estado != EstadoSuscripcion.Activa &&
                         existingSubscription.Estado != EstadoSuscripcion.Trial &&
                         existingSubscription.Estado != EstadoSuscripcion.Morosa)
                {
                    existingSubscription.PlanId = planId;
                    existingSubscription.CodigoPlan = plan.Codigo ?? plan.Nombre;
                    existingSubscription.Proveedor = PaymentProviderType.Tilopay;
                    existingSubscription.ProviderReference = correlationToken;
                    existingSubscription.Estado = EstadoSuscripcion.Pendiente;
                    existingSubscription.TilopayRecurringPlanId = repeatRegistration.Plan.TilopayPlanId;
                    existingSubscription.PrecioMensual = plan.PrecioMensual;
                    existingSubscription.MonedaFacturacion = plan.Moneda;
                    existingSubscription.MaxFuncionarios = plan.MaxFuncionarios;
                    existingSubscription.FechaUltimaActualizacionUtc = DateTime.UtcNow;
                    existingSubscription.MotivoEstado = "Signup recurrente iniciado; pendiente de confirmacion por webhook.";
                }
            }

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Signup recurrente preparado correctamente. TenantId {TenantId}. PlanCode {PlanCode}. TilopayPlanId {TilopayPlanId}.",
                tenantId,
                repeatRegistration.Plan.Code,
                repeatRegistration.Plan.TilopayPlanId);

            return new PaymentCheckoutResult
            {
                ProviderType = PaymentProviderType.Tilopay,
                RedirectUrl = redirectUrl,
                ProviderReference = correlationToken,
                RawResponse = "{\"mode\":\"tilopay-repeat\"}",
                CorrelationId = correlationToken
            };
        }

        public async Task<PaymentWebhookProcessingResult> ProcessTilopayWebhookAsync(
            string payload,
            string? correlationId,
            CancellationToken cancellationToken = default)
        {
            var provider = _providerResolver.Get(PaymentProviderType.Tilopay);
            var webhook = provider.ParseWebhook(payload);

            if (webhook.IsRecurring)
            {
                return await ProcessTilopayRecurringWebhookAsync(
                    webhook,
                    payload,
                    correlationId,
                    cancellationToken);
            }

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
                    Payload = RedactSensitivePayload(payload),
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
            evento.Payload = RedactSensitivePayload(payload);
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

        private async Task<PaymentWebhookProcessingResult> ProcessTilopayRecurringWebhookAsync(
            PaymentProviderWebhookData webhook,
            string payload,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["Provider"] = webhook.ProviderType,
                ["EventId"] = webhook.EventId,
                ["RecurringPlanId"] = webhook.RecurringPlanId,
                ["ProviderSubscriberId"] = webhook.ProviderSubscriberId,
                ["Reference"] = webhook.Reference,
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
                    "Webhook recurrente duplicado ignorado. EventId {EventId}. Estado {Estado}.",
                    existingEvent.ProveedorEventId,
                    existingEvent.EstadoProcesamiento);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsDuplicate = true,
                    IsProcessed = true,
                    Message = "Evento recurrente duplicado"
                };
            }

            var resolvedPlan = webhook.RecurringPlanId.HasValue
                ? _tilopayRepeatOptions.FindByRecurringPlanId(webhook.RecurringPlanId.Value)
                : null;

            var internalPlan = resolvedPlan is null
                ? null
                : await _db.Planes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        plan => plan.Activo &&
                                plan.Codigo != null &&
                                plan.Codigo == resolvedPlan.Code,
                        cancellationToken);

            var correlation = await ResolveRecurringCorrelationAsync(
                webhook,
                internalPlan?.Id,
                cancellationToken);

            var evento = existingEvent ?? new EventoPago
            {
                Id = Guid.NewGuid(),
                Proveedor = webhook.ProviderType,
                ProveedorEventId = webhook.EventId
            };

            if (existingEvent is null)
            {
                _db.EventosPago.Add(evento);
            }

            evento.TenantId = correlation.TenantId;
            evento.PlanId = correlation.PlanId;
            evento.PagoSuscripcionId = correlation.PaymentAttempt?.Id;
            evento.Tipo = webhook.EventType;
            evento.ReferenciaExterna = ResolveProviderReference(webhook);
            evento.ProviderTransactionId = webhook.ProviderTransactionId;
            evento.TilopayRecurringPlanId = webhook.RecurringPlanId;
            evento.ProviderSubscriberId = webhook.ProviderSubscriberId;
            evento.Monto = webhook.Amount;
            evento.Moneda = webhook.Currency;
            evento.CorrelationId = correlationId;
            evento.Payload = RedactSensitivePayload(payload);
            evento.EstadoProcesamiento = "Recibido";
            evento.FechaRecepcionUtc = DateTime.UtcNow;
            evento.FechaProcesamientoUtc = null;
            evento.Procesado = false;
            evento.Error = null;

            await _db.SaveChangesAsync(cancellationToken);

            if (resolvedPlan is null || internalPlan is null)
            {
                await MarkEventForManualReviewAsync(
                    evento,
                    $"No existe un plan recurrente interno asociado al plan Tilopay {webhook.RecurringPlanId?.ToString() ?? "sin id"}.",
                    cancellationToken);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = false,
                    Message = "Webhook recurrente pendiente de revision manual."
                };
            }

            if (correlation.RequiresManualReview)
            {
                await MarkEventForManualReviewAsync(
                    evento,
                    correlation.ManualReviewReason ?? "No fue posible correlacionar el webhook recurrente con un tenant de forma segura.",
                    cancellationToken);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = false,
                    Message = "Webhook recurrente pendiente de revision manual."
                };
            }

            var tenantId = correlation.TenantId ?? throw new InvalidOperationException(
                "La correlacion del webhook recurrente no produjo un TenantId valido.");

            var intento = correlation.PaymentAttempt ??
                await EnsureRecurringPaymentAttemptAsync(
                    tenantId,
                    internalPlan,
                    webhook,
                    resolvedPlan,
                    cancellationToken);

            try
            {
                intento.TilopayRecurringPlanId = webhook.RecurringPlanId;
                intento.ProviderSubscriberId = webhook.ProviderSubscriberId ?? intento.ProviderSubscriberId;
                intento.ProviderTransactionId = webhook.ProviderTransactionId ?? intento.ProviderTransactionId;
                intento.ProviderReference = ResolveProviderReference(webhook) ?? intento.ProviderReference;
                intento.ProviderResultCode = webhook.StatusCode;
                intento.ProviderResultMessage = webhook.StatusDescription;
                intento.Monto = webhook.Amount ?? intento.Monto;
                intento.Moneda = string.IsNullOrWhiteSpace(webhook.Currency) ? intento.Moneda : webhook.Currency!;
                intento.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                intento.FechaActualizacionUtc = DateTime.UtcNow;

                if (IsRecurringApproved(webhook))
                {
                    intento.Estado = EstadoPagoProveedor.Confirmado;
                    intento.FechaConfirmacionUtc = DateTime.UtcNow;

                    if (resolvedPlan.IsAddon)
                    {
                        await _suscripcionService.ActivarAddonWhatsAppRecurrenteAsync(
                            tenantId,
                            internalPlan,
                            resolvedPlan.TilopayPlanId,
                            webhook.ProviderSubscriberId,
                            webhook.ProviderTransactionId,
                            motivo: "Pago recurrente WhatsApp aprobado por webhook Tilopay.",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _suscripcionService.ActivarSuscripcionRecurrenteAsync(
                            tenantId,
                            internalPlan,
                            resolvedPlan.TilopayPlanId,
                            webhook.ProviderSubscriberId,
                            webhook.ProviderTransactionId,
                            intento.ProviderReference,
                            motivo: "Pago recurrente aprobado por webhook Tilopay.",
                            cancellationToken: cancellationToken);
                    }

                    await EnsureInvoiceAsync(
                        tenantId,
                        internalPlan.Id,
                        intento,
                        webhook,
                        cancellationToken);
                }
                else if (IsRecurringCancelled(webhook))
                {
                    intento.Estado = EstadoPagoProveedor.Cancelado;

                    await _suscripcionService.MarcarSuscripcionCanceladaRecurrenteAsync(
                        tenantId,
                        webhook.ProviderSubscriberId,
                        resolvedPlan.IsAddon,
                        motivo: "Cancelacion recibida desde webhook recurrente Tilopay.",
                        cancellationToken: cancellationToken);
                }
                else
                {
                    intento.Estado = MapFailedStatus(webhook.StatusCode);

                    if (resolvedPlan.IsAddon)
                    {
                        await _suscripcionService.RegistrarPagoFallidoAddonAsync(
                            tenantId,
                            resolvedPlan.Code,
                            webhook.ProviderSubscriberId,
                            webhook.ProviderTransactionId,
                            motivo: $"Pago recurrente WhatsApp no aprobado. Codigo {webhook.StatusCode}.",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _suscripcionService.RegistrarPagoFallidoAsync(
                            tenantId,
                            internalPlan.Id,
                            intento.Id,
                            PaymentProviderType.Tilopay,
                            intento.ProviderReference ?? intento.ReferenciaInterna,
                            webhook.ProviderTransactionId,
                            intento.Monto,
                            intento.Moneda,
                            $"Pago recurrente no aprobado. Codigo {webhook.StatusCode}.",
                            cancellationToken);
                    }
                }

                evento.TenantId = correlation.TenantId;
                evento.PlanId = internalPlan.Id;
                evento.PagoSuscripcionId = intento.Id;
                evento.ProviderTransactionId = intento.ProviderTransactionId;
                evento.EstadoProcesamiento = "Procesado";
                evento.Procesado = true;
                evento.FechaProcesamientoUtc = DateTime.UtcNow;
                evento.Error = null;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Webhook recurrente Tilopay procesado correctamente. TenantId {TenantId}. PlanCode {PlanCode}. EstadoPago {EstadoPago}.",
                    correlation.TenantId,
                    resolvedPlan.Code,
                    intento.Estado);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = true,
                    Message = "Webhook recurrente procesado correctamente.",
                    EstadoPago = intento.Estado
                };
            }
            catch (Exception ex)
            {
                evento.EstadoProcesamiento = "Error";
                evento.Error = Trim(ex.Message, 500);
                evento.FechaProcesamientoUtc = DateTime.UtcNow;

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "No fue posible persistir el error del evento recurrente.");
                }

                _logger.LogError(ex, "Error procesando webhook recurrente Tilopay.");
                throw;
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
                Payload = RedactSensitivePayload(payload),
                Procesado = false,
                EstadoProcesamiento = "Rechazado",
                FechaRecepcionUtc = DateTime.UtcNow,
                FechaProcesamientoUtc = DateTime.UtcNow,
                Error = Trim(reason, 500)
            };

            _db.EventosPago.Add(evento);
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task<RecurringCorrelationResolution> ResolveRecurringCorrelationAsync(
            PaymentProviderWebhookData webhook,
            Guid? planId,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(webhook.Reference))
            {
                var byReference = await _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .Where(p =>
                        p.Proveedor == PaymentProviderType.Tilopay &&
                        (p.CorrelationToken == webhook.Reference ||
                         p.ProviderReference == webhook.Reference ||
                         p.ReferenciaInterna == webhook.Reference))
                    .OrderByDescending(p => p.FechaCreacionUtc)
                    .Take(2)
                    .ToListAsync(cancellationToken);

                if (byReference.Count == 1)
                {
                    var payment = byReference[0];
                    return new RecurringCorrelationResolution(
                        payment.TenantId,
                        payment.PlanId,
                        PaymentAttempt: payment,
                        RequiresManualReview: false,
                        ManualReviewReason: null);
                }

                if (byReference.Count > 1)
                {
                    return RecurringCorrelationResolution.Manual(
                        "El webhook recurrente coincide con mas de un intento local usando la referencia devuelta por Tilopay.");
                }
            }

            if (!string.IsNullOrWhiteSpace(webhook.ProviderSubscriberId))
            {
                var baseSubscription = await _db.Suscripciones
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(subscription => subscription.ProviderSubscriptionId == webhook.ProviderSubscriberId)
                    .Select(subscription => new { subscription.TenantId, subscription.PlanId })
                    .FirstOrDefaultAsync(cancellationToken);

                var addonSubscription = await _db.TenantSubscriptionAddons
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(addon => addon.ProviderSubscriptionId == webhook.ProviderSubscriberId)
                    .Select(addon => new { addon.TenantId, addon.PlanId })
                    .FirstOrDefaultAsync(cancellationToken);

                if (baseSubscription is not null && addonSubscription is not null)
                {
                    return RecurringCorrelationResolution.Manual(
                        "El subscriberId del proveedor coincide con una suscripcion base y un add-on local.");
                }

                if (baseSubscription is not null)
                {
                    return new RecurringCorrelationResolution(
                        baseSubscription.TenantId,
                        baseSubscription.PlanId,
                        PaymentAttempt: null,
                        RequiresManualReview: false,
                        ManualReviewReason: null);
                }

                if (addonSubscription is not null)
                {
                    return new RecurringCorrelationResolution(
                        addonSubscription.TenantId,
                        addonSubscription.PlanId,
                        PaymentAttempt: null,
                        RequiresManualReview: false,
                        ManualReviewReason: null);
                }
            }

            if (webhook.RecurringPlanId.HasValue)
            {
                var lookupWindowUtc = DateTime.UtcNow.AddHours(-48);
                var pendingAttempts = _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .Where(p =>
                        p.Proveedor == PaymentProviderType.Tilopay &&
                        p.TilopayRecurringPlanId == webhook.RecurringPlanId &&
                        p.Estado == EstadoPagoProveedor.Pendiente &&
                        p.FechaCreacionUtc >= lookupWindowUtc);

                if (!string.IsNullOrWhiteSpace(webhook.CustomerEmail))
                {
                    pendingAttempts = pendingAttempts.Where(p => p.ClienteEmail == webhook.CustomerEmail);
                }

                if (planId.HasValue)
                {
                    pendingAttempts = pendingAttempts.Where(p => p.PlanId == planId.Value);
                }

                var candidates = await pendingAttempts
                    .OrderByDescending(p => p.FechaCreacionUtc)
                    .Take(2)
                    .ToListAsync(cancellationToken);

                if (candidates.Count == 1)
                {
                    var payment = candidates[0];
                    return new RecurringCorrelationResolution(
                        payment.TenantId,
                        payment.PlanId,
                        PaymentAttempt: payment,
                        RequiresManualReview: false,
                        ManualReviewReason: null);
                }

                if (candidates.Count > 1)
                {
                    return RecurringCorrelationResolution.Manual(
                        "El webhook recurrente coincide con multiples signups pendientes del mismo plan y requiere revision manual.");
                }
            }

            return RecurringCorrelationResolution.Manual(
                "Tilopay no envio datos suficientes para correlacionar el webhook recurrente con un tenant.");
        }

        private async Task<PagoSuscripcion> EnsureRecurringPaymentAttemptAsync(
            Guid tenantId,
            Plan plan,
            PaymentProviderWebhookData webhook,
            TilopayRepeatPlanOption repeatPlan,
            CancellationToken cancellationToken)
        {
            var reference = !string.IsNullOrWhiteSpace(webhook.Reference) && IsRecognizedInternalReference(webhook.Reference)
                ? webhook.Reference
                : GenerateReference(tenantId);

            var intento = new PagoSuscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Proveedor = PaymentProviderType.Tilopay,
                Estado = EstadoPagoProveedor.Pendiente,
                ReferenciaInterna = reference,
                ProviderReference = ResolveProviderReference(webhook),
                TilopayRecurringPlanId = repeatPlan.TilopayPlanId,
                ProviderSubscriberId = webhook.ProviderSubscriberId,
                ProviderTransactionId = webhook.ProviderTransactionId,
                ClienteEmail = webhook.CustomerEmail,
                Descripcion = repeatPlan.IsAddon
                    ? $"Renovacion recurrente add-on {plan.Nombre}"
                    : $"Renovacion recurrente {plan.Nombre}",
                Monto = webhook.Amount ?? plan.PrecioMensual,
                Moneda = string.IsNullOrWhiteSpace(webhook.Currency)
                    ? (string.IsNullOrWhiteSpace(plan.Moneda) ? "CRC" : plan.Moneda)
                    : webhook.Currency!,
                FechaCreacionUtc = DateTime.UtcNow,
                FechaActualizacionUtc = DateTime.UtcNow
            };

            _db.PagosSuscripcion.Add(intento);
            await _db.SaveChangesAsync(cancellationToken);
            return intento;
        }

        private async Task EnsureInvoiceAsync(
            Guid tenantId,
            Guid planId,
            PagoSuscripcion intento,
            PaymentProviderWebhookData webhook,
            CancellationToken cancellationToken)
        {
            var providerReference = intento.ProviderReference ?? intento.ReferenciaInterna;
            var facturaExiste = await _db.Facturas
                .IgnoreQueryFilters()
                .AnyAsync(
                    factura => factura.Proveedor == PaymentProviderType.Tilopay &&
                    (
                        (!string.IsNullOrWhiteSpace(intento.ProviderTransactionId) &&
                         factura.ProviderTransactionId == intento.ProviderTransactionId) ||
                        factura.ProviderReference == providerReference
                    ),
                    cancellationToken);

            if (facturaExiste)
            {
                return;
            }

            var subscriptionId = await _db.Suscripciones
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            _db.Facturas.Add(new Factura
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SuscripcionId = subscriptionId,
                PagoSuscripcionId = intento.Id,
                Proveedor = PaymentProviderType.Tilopay,
                ProviderInvoiceId = providerReference,
                ProviderTransactionId = intento.ProviderTransactionId,
                ProviderReference = providerReference,
                Monto = intento.Monto,
                Moneda = intento.Moneda,
                Estado = "Pagado",
                Fecha = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkEventForManualReviewAsync(
            EventoPago evento,
            string reason,
            CancellationToken cancellationToken)
        {
            evento.EstadoProcesamiento = "PendingManualReview";
            evento.Error = Trim(reason, 500);
            evento.FechaProcesamientoUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Webhook Tilopay recurrente requiere revision manual. EventId {EventId}. Reason {Reason}.",
                evento.ProveedorEventId,
                reason);
        }

        private static bool IsRecurringApproved(PaymentProviderWebhookData webhook)
        {
            if (string.Equals(webhook.StatusCode, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var combined = $"{webhook.EventType} {webhook.StatusCode} {webhook.StatusDescription}";
            return ContainsAny(
                combined,
                "approved",
                "aprob",
                "paid",
                "payment_success",
                "success",
                "completed",
                "active");
        }

        private static bool IsRecurringCancelled(PaymentProviderWebhookData webhook)
        {
            var combined = $"{webhook.EventType} {webhook.StatusCode} {webhook.StatusDescription}";
            return ContainsAny(
                combined,
                "cancel",
                "canceled",
                "cancelled",
                "baja",
                "deleted",
                "inactive");
        }

        private static bool ContainsAny(string input, params string[] candidates) =>
            candidates.Any(candidate => input.Contains(candidate, StringComparison.OrdinalIgnoreCase));

        private string BuildRecurringCheckoutUrl(
            string baseUrl,
            string correlationToken,
            string customerEmail,
            string planCode)
        {
            var queryItems = new Dictionary<string, string?>
            {
                ["lc_ref"] = correlationToken,
                ["lc_plan"] = planCode
            };

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                queryItems["lc_email"] = customerEmail;
            }

            return QueryHelpers.AddQueryString(baseUrl, queryItems!);
        }

        private static string GenerateCorrelationToken() => Guid.NewGuid().ToString("N").ToUpperInvariant();

        private static string RedactSensitivePayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                return JsonSerializer.Serialize(RedactJsonElement(document.RootElement));
            }
            catch (JsonException)
            {
                return payload
                    .Replace("creditCardToken", "creditCardToken_REDACTED", StringComparison.OrdinalIgnoreCase)
                    .Replace("WebhookAccessToken", "WebhookAccessToken_REDACTED", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static object? RedactJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => IsSensitiveProperty(property.Name)
                        ? "***redacted***"
                        : RedactJsonElement(property.Value)),
                JsonValueKind.Array => element.EnumerateArray().Select(RedactJsonElement).ToArray(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static bool IsSensitiveProperty(string propertyName) =>
            propertyName.Contains("cvv", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("cvc", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("pan", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("cardnumber", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("creditcardnumber", StringComparison.OrdinalIgnoreCase);

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

        private sealed record RecurringCorrelationResolution(
            Guid? TenantId,
            Guid? PlanId,
            PagoSuscripcion? PaymentAttempt,
            bool RequiresManualReview,
            string? ManualReviewReason)
        {
            public static RecurringCorrelationResolution Manual(string reason) =>
                new(
                    TenantId: null,
                    PlanId: null,
                    PaymentAttempt: null,
                    RequiresManualReview: true,
                    ManualReviewReason: reason);
        }
    }
}
