using System.Globalization;
using System.Text.Json;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Payments
{
    public class SaaSPaymentService
    {
        private static readonly TimeSpan RecurringPendingLifetime = TimeSpan.FromHours(72);

        private readonly ApplicationDbContext _db;
        private readonly PaymentProviderResolver _providerResolver;
        private readonly SuscripcionService _suscripcionService;
        private readonly ITenantExecutionContextAccessor _tenantExecutionContextAccessor;
        private readonly ILogger<SaaSPaymentService> _logger;
        private readonly OpcionesPago _paymentOptions;
        private readonly OpcionesTilopay _tilopayOptions;
        private readonly TilopayRepeatOptions _tilopayRepeatOptions;
        private readonly IHostEnvironment? _environment;

        public SaaSPaymentService(
            ApplicationDbContext db,
            PaymentProviderResolver providerResolver,
            SuscripcionService suscripcionService,
            ITenantExecutionContextAccessor tenantExecutionContextAccessor,
            IOptions<OpcionesPago> paymentOptions,
            IOptions<OpcionesTilopay> tilopayOptions,
            IOptions<TilopayRepeatOptions> tilopayRepeatOptions,
            ILogger<SaaSPaymentService> logger,
            IHostEnvironment? environment = null)
        {
            _db = db;
            _providerResolver = providerResolver;
            _suscripcionService = suscripcionService;
            _tenantExecutionContextAccessor = tenantExecutionContextAccessor;
            _logger = logger;
            _paymentOptions = paymentOptions.Value;
            _tilopayOptions = tilopayOptions.Value;
            _tilopayRepeatOptions = tilopayRepeatOptions.Value;
            _environment = environment;
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
                throw new InvalidOperationException("Tilopay Repeat esta deshabilitado: TilopayRepeat:Enabled=false.");
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
                    $"El plan {plan.Codigo ?? plan.Nombre} no tiene mapping recurrente configurado.");
            }

            if (!_tilopayRepeatOptions.UseHostedLinks)
            {
                throw new InvalidOperationException(
                    "Tilopay Repeat requiere hosted links: TilopayRepeat:UseHostedLinks=false.");
            }

            if (string.IsNullOrWhiteSpace(_tilopayOptions.WebhookAccessToken))
            {
                throw new InvalidOperationException(
                    "Falta WebhookAccessToken: Tilopay:WebhookAccessToken.");
            }

            if (string.IsNullOrWhiteSpace(repeatRegistration.Plan.CheckoutUrl))
            {
                throw new InvalidOperationException(
                    $"Falta CheckoutUrl para {repeatRegistration.Plan.Code}: TilopayRepeat:{repeatRegistration.SectionKey}:CheckoutUrl.");
            }

            await ExpireOpenRecurringPendingAttemptsAsync(
                tenantId,
                planId,
                customerEmail,
                repeatRegistration.Plan.TilopayPlanId,
                cancellationToken);

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
                ["CorrelationToken"] = correlationToken,
                ["ExpectedFirstChargeAmount"] = repeatRegistration.Plan.ExpectedFirstChargeAmount,
                ["ExpectedCurrency"] = string.IsNullOrWhiteSpace(repeatRegistration.Plan.Currency)
                    ? "CRC"
                    : repeatRegistration.Plan.Currency.ToUpperInvariant()
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
                    expectedFirstChargeAmount = repeatRegistration.Plan.ExpectedFirstChargeAmount,
                    expectedCurrency = string.IsNullOrWhiteSpace(repeatRegistration.Plan.Currency)
                        ? "CRC"
                        : repeatRegistration.Plan.Currency.ToUpperInvariant(),
                    hostedLinkDefinesAmount = true,
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
                "Signup recurrente preparado correctamente. TenantId {TenantId}. PlanCode {PlanCode}. TilopayPlanId {TilopayPlanId}. ExpectedFirstChargeAmount {ExpectedFirstChargeAmount}. CheckoutUrl {CheckoutUrl}. CorrelationToken {CorrelationToken}. HostedLinkDefinesAmount {HostedLinkDefinesAmount}.",
                tenantId,
                repeatRegistration.Plan.Code,
                repeatRegistration.Plan.TilopayPlanId,
                repeatRegistration.Plan.ExpectedFirstChargeAmount,
                SanitizeRecurringCheckoutUrlForLog(redirectUrl),
                correlationToken,
                true);

            _logger.LogInformation(
                "Tilopay Repeat hosted link usa el monto configurado en el dashboard del proveedor. Verifica que el monto por pago inicial sea 0.00 y el monto recurrente sea {ExpectedFirstChargeAmount} {Currency} para PlanCode {PlanCode}.",
                repeatRegistration.Plan.ExpectedFirstChargeAmount,
                string.IsNullOrWhiteSpace(repeatRegistration.Plan.Currency)
                    ? "CRC"
                    : repeatRegistration.Plan.Currency.ToUpperInvariant(),
                repeatRegistration.Plan.Code);

            return new PaymentCheckoutResult
            {
                ProviderType = PaymentProviderType.Tilopay,
                RedirectUrl = redirectUrl,
                ProviderReference = correlationToken,
                RawResponse = "{\"mode\":\"tilopay-repeat\"}",
                CorrelationId = correlationToken
            };
        }

        public async Task<RecurringPaymentApprovalResult> ApproveRecurringPaymentAsync(
            RecurringPaymentApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var providerTransactionId = request.ProviderTransactionId?.Trim();
            if (string.IsNullOrWhiteSpace(providerTransactionId))
            {
                throw new InvalidOperationException("Debes indicar el transactionId o numero de orden aprobado por Tilopay.");
            }

            var intento = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .Include(payment => payment.Plan)
                .FirstOrDefaultAsync(payment => payment.Id == request.PaymentId, cancellationToken)
                ?? throw new InvalidOperationException("No existe el pending recurrente solicitado.");

            // Establecer tenant scope para que SESSION_CONTEXT quede correcto en SQL Server.
            // Es crítico para que el BLOCK PREDICATE de TenantWhatsAppSettings permita INSERT/UPDATE
            // cuando este método se invoca desde un webhook (sin usuario autenticado).
            using var tenantScope = _tenantExecutionContextAccessor.BeginScope(intento.TenantId);

            if (intento.Proveedor != PaymentProviderType.Tilopay)
            {
                throw new InvalidOperationException("La conciliacion manual solo aplica a pagos recurrentes Tilopay.");
            }

            var plan = intento.Plan ?? await _db.Planes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(currentPlan => currentPlan.Id == intento.PlanId, cancellationToken)
                ?? throw new InvalidOperationException("El plan asociado al pending recurrente ya no existe.");

            var repeatRegistration = ResolveRecurringPlanRegistration(intento, plan);
            if (repeatRegistration is null)
            {
                throw new InvalidOperationException(
                    $"No existe mapping recurrente interno para el plan {plan.Codigo ?? plan.Nombre}.");
            }

            EnsureRecurringPaymentCanBeApproved(intento);
            EnsureRecurringPaymentIsCurrent(intento);
            EnsureApprovedAmountMatchesPlan(request.ApprovedAmount, request.Currency, repeatRegistration.Plan, plan);
            await EnsureProviderTransactionIsUniqueAsync(intento.Id, providerTransactionId, cancellationToken);

            var approvedAtUtc = DateTime.UtcNow;
            var normalizedCurrency = NormalizeRecurringCurrency(request.Currency, repeatRegistration.Plan, plan);
            var providerReference = FirstNonEmpty(
                request.ProviderReference,
                intento.ProviderReference,
                intento.CorrelationToken,
                intento.ReferenciaInterna);

            intento.TilopayRecurringPlanId = repeatRegistration.Plan.TilopayPlanId;
            intento.ProviderTransactionId = providerTransactionId;
            intento.ProviderSubscriberId = FirstNonEmpty(request.ProviderSubscriberId, intento.ProviderSubscriberId);
            intento.ProviderReference = providerReference;
            intento.ProviderAuthorizationCode = FirstNonEmpty(request.ProviderAuthorizationCode, intento.ProviderAuthorizationCode);
            intento.Estado = EstadoPagoProveedor.Confirmado;
            intento.ProviderResultCode = FirstNonEmpty(
                request.ProviderResultCode,
                string.Equals(request.Source, "webhook", StringComparison.OrdinalIgnoreCase)
                    ? "WEBHOOK_APPROVED"
                    : "MANUAL_APPROVED");
            intento.ProviderResultMessage = BuildRecurringApprovalMessage(request.Source, request.Observation);
            intento.Monto = request.ApprovedAmount;
            intento.Moneda = normalizedCurrency;
            intento.UltimoPayloadProveedor = BuildRecurringApprovalAuditPayload(request, intento, repeatRegistration.Plan, plan);
            intento.FechaConfirmacionUtc = approvedAtUtc;
            intento.FechaActualizacionUtc = approvedAtUtc;

            if (repeatRegistration.Plan.IsAddon)
            {
                await _suscripcionService.ActivarAddonWhatsAppRecurrenteAsync(
                    intento.TenantId,
                    plan,
                    repeatRegistration.Plan.TilopayPlanId,
                    request.ProviderSubscriberId,
                    providerTransactionId,
                    motivo: BuildRecurringApprovalReason(request.Source, request.Observation),
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _suscripcionService.ActivarSuscripcionRecurrenteAsync(
                    intento.TenantId,
                    plan,
                    repeatRegistration.Plan.TilopayPlanId,
                    request.ProviderSubscriberId,
                    providerTransactionId,
                    providerReference,
                    motivo: BuildRecurringApprovalReason(request.Source, request.Observation),
                    cancellationToken: cancellationToken);
            }

            await EnsureInvoiceAsync(
                intento.TenantId,
                plan.Id,
                intento,
                cancellationToken);

            if (request.CreateAuditEvent)
            {
                _db.EventosPago.Add(new EventoPago
                {
                    Id = Guid.NewGuid(),
                    Proveedor = PaymentProviderType.Tilopay,
                    TenantId = intento.TenantId,
                    PlanId = plan.Id,
                    PagoSuscripcionId = intento.Id,
                    ProveedorEventId = $"manual-reconciliation-{intento.Id:N}-{Guid.NewGuid():N}",
                    Tipo = FirstNonEmpty(request.EventType, "tilopay.repeat.manual.approval")!,
                    ReferenciaExterna = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    TilopayRecurringPlanId = repeatRegistration.Plan.TilopayPlanId,
                    ProviderSubscriberId = intento.ProviderSubscriberId,
                    Monto = request.ApprovedAmount,
                    Moneda = normalizedCurrency,
                    CorrelationId = request.CorrelationId,
                    Procesado = true,
                    EstadoProcesamiento = "Procesado",
                    Payload = RedactSensitivePayload(BuildRecurringApprovalAuditPayload(request, intento, repeatRegistration.Plan, plan)),
                    FechaRecepcionUtc = approvedAtUtc,
                    FechaProcesamientoUtc = approvedAtUtc
                });
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (request.NextBillingDateUtc.HasValue)
            {
                await ApplyRecurringNextBillingDateOverrideAsync(
                    intento.TenantId,
                    repeatRegistration.Plan.IsAddon,
                    request.NextBillingDateUtc.Value,
                    cancellationToken);
            }

            var currentPeriod = repeatRegistration.Plan.IsAddon
                ? await _db.TenantSubscriptionAddons
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(addon => addon.TenantId == intento.TenantId && addon.PlanId == plan.Id)
                    .Select(addon => new
                    {
                        addon.Estado,
                        addon.FechaFin,
                        addon.FechaProximoCobroUtc
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            Suscripcion? currentSubscription = null;
            if (!repeatRegistration.Plan.IsAddon)
            {
                currentSubscription = await _db.Suscripciones
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(subscription => subscription.TenantId == intento.TenantId, cancellationToken);
            }

            return new RecurringPaymentApprovalResult
            {
                PaymentId = intento.Id,
                TenantId = intento.TenantId,
                PlanId = plan.Id,
                PlanCode = plan.Codigo ?? plan.Nombre,
                IsAddon = repeatRegistration.Plan.IsAddon,
                PaymentStatus = intento.Estado,
                SubscriptionStatus = repeatRegistration.Plan.IsAddon
                    ? currentPeriod?.Estado ?? EstadoSuscripcion.Pendiente
                    : currentSubscription?.Estado ?? EstadoSuscripcion.Pendiente,
                CurrentPeriodEndUtc = repeatRegistration.Plan.IsAddon
                    ? currentPeriod?.FechaFin
                    : currentSubscription?.FechaFin,
                NextBillingDateUtc = repeatRegistration.Plan.IsAddon
                    ? currentPeriod?.FechaProximoCobroUtc
                    : currentSubscription?.FechaProximoCobroUtc,
                ProviderTransactionId = providerTransactionId,
                ProviderSubscriberId = intento.ProviderSubscriberId
            };
        }

        public async Task<PaymentWebhookProcessingResult> ProcessTilopayWebhookAsync(
            string payload,
            string? correlationId,
            string? incomingEvent = null,
            CancellationToken cancellationToken = default)
        {
            var provider = _providerResolver.Get(PaymentProviderType.Tilopay);
            var webhook = provider.ParseWebhook(payload);
            ApplyIncomingRecurringEvent(webhook, incomingEvent);

            if (IsManagedRecurringWebhook(webhook))
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

        private bool IsManagedRecurringWebhook(PaymentProviderWebhookData webhook) =>
            webhook.IsRecurring ||
            webhook.RecurringPlanId.HasValue ||
            _tilopayRepeatOptions.FindByCode(webhook.PlanCode) is not null;

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
                ["PlanCode"] = webhook.PlanCode,
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
                : _tilopayRepeatOptions.FindByCode(webhook.PlanCode);
            var resolvedRecurringPlanId = webhook.RecurringPlanId ?? resolvedPlan?.TilopayPlanId;
            if (resolvedPlan is not null && string.IsNullOrWhiteSpace(webhook.Currency))
            {
                webhook.Currency = string.IsNullOrWhiteSpace(resolvedPlan.Currency)
                    ? "CRC"
                    : resolvedPlan.Currency.ToUpperInvariant();
            }

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
                resolvedRecurringPlanId,
                cancellationToken);

            _logger.LogInformation(
                "Correlacion webhook recurrente Tilopay. EventId {EventId}. EventType {EventType}. Reference {Reference}. PlanCode {PlanCode}. IncomingRecurringPlanId {IncomingRecurringPlanId}. ResolvedRecurringPlanId {ResolvedRecurringPlanId}. TenantId {TenantId}. PlanId {PlanId}. PaymentAttemptId {PaymentAttemptId}. IsUnmatched {IsUnmatched}. RequiresManualReview {RequiresManualReview}. Reason {Reason}.",
                webhook.EventId,
                webhook.EventType,
                webhook.Reference,
                webhook.PlanCode,
                webhook.RecurringPlanId,
                resolvedRecurringPlanId,
                correlation.TenantId,
                correlation.PlanId,
                correlation.PaymentAttempt?.Id,
                correlation.IsUnmatched,
                correlation.RequiresManualReview,
                correlation.ManualReviewReason);

            LogDevelopmentRecurringCorrelation(webhook, correlation, resolvedRecurringPlanId);

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
            evento.TilopayRecurringPlanId = resolvedRecurringPlanId;
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
                var planDescriptor = resolvedRecurringPlanId.HasValue
                    ? resolvedRecurringPlanId.Value.ToString(CultureInfo.InvariantCulture)
                    : webhook.PlanCode ?? "sin id";

                await MarkEventForManualReviewAsync(
                    evento,
                    $"No existe un plan recurrente interno asociado al plan Tilopay {planDescriptor}.",
                    cancellationToken);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = false,
                    Message = "Webhook recurrente pendiente de revision manual."
                };
            }

            if (correlation.IsUnmatched)
            {
                await MarkEventAsUnmatchedAsync(
                    evento,
                    correlation.ManualReviewReason ?? "No existe un pending recurrente asociado al webhook recibido.",
                    cancellationToken);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = false,
                    Message = "Webhook recurrente recibido sin pending asociado."
                };
            }

            if (correlation.RequiresManualReview)
            {
                if (correlation.PaymentAttempt is not null)
                {
                    correlation.PaymentAttempt.Estado = EstadoPagoProveedor.ManualReview;
                    correlation.PaymentAttempt.ProviderResultCode = "MANUAL_REVIEW";
                    correlation.PaymentAttempt.ProviderResultMessage = Trim(
                        correlation.ManualReviewReason ?? "El webhook recurrente requiere revision manual.",
                        300);
                    correlation.PaymentAttempt.FechaActualizacionUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                }

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

            var requiresPaymentAttempt = !IsRecurringRegistrationEvent(webhook.EventType) &&
                                         !IsRecurringCancellationEvent(webhook.EventType) &&
                                         !IsRecurringReactivationEvent(webhook.EventType);

            var intento = correlation.PaymentAttempt;
            if (requiresPaymentAttempt && intento is null)
            {
                intento = await EnsureRecurringPaymentAttemptAsync(
                    tenantId,
                    internalPlan,
                    webhook,
                    resolvedPlan,
                    cancellationToken);
            }

            try
            {
                if (intento is not null)
                {
                    intento.TilopayRecurringPlanId = resolvedRecurringPlanId;
                }

                if (IsRecurringRegistrationEvent(webhook.EventType))
                {
                    await HandleRecurringRegistrationAsync(
                        webhook,
                        payload,
                        tenantId,
                        internalPlan,
                        resolvedPlan,
                        intento,
                        correlationId,
                        cancellationToken);
                }
                else if (IsRecurringCancellationEvent(webhook.EventType) || IsRecurringCancelled(webhook))
                {
                    if (intento is not null)
                    {
                        intento.Estado = EstadoPagoProveedor.Cancelado;
                        intento.ProviderResultCode = FirstNonEmpty(webhook.StatusCode, "REPEAT_SUBSCRIPTION_CANCELLED");
                        intento.ProviderResultMessage = Trim(
                            FirstNonEmpty(webhook.StatusDescription, "Cancelacion recibida desde webhook recurrente Tilopay.")!,
                            300);
                        intento.ProviderTransactionId = FirstNonEmpty(
                            webhook.ProviderTransactionId,
                            webhook.ProviderOrderNumber,
                            intento.ProviderTransactionId);
                        intento.ProviderSubscriberId = FirstNonEmpty(webhook.ProviderSubscriberId, intento.ProviderSubscriberId);
                        intento.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                        intento.FechaActualizacionUtc = DateTime.UtcNow;
                    }

                    await _suscripcionService.MarcarSuscripcionCanceladaRecurrenteAsync(
                        tenantId,
                        webhook.ProviderSubscriberId,
                        resolvedPlan.IsAddon,
                        motivo: "Cancelacion recibida desde webhook recurrente Tilopay.",
                        cancellationToken: cancellationToken);

                    await ApplyRecurringExpirationOverrideAsync(
                        tenantId,
                        resolvedPlan.IsAddon,
                        webhook.ExpirationDateUtc,
                        cancellationToken);
                }
                else if (IsRecurringReactivationEvent(webhook.EventType))
                {
                    if (resolvedPlan.IsAddon)
                    {
                        var addonAutoActivation = await ValidateAddonAutomaticActivationAsync(
                            tenantId,
                            cancellationToken);

                        if (!addonAutoActivation.CanActivate)
                        {
                            await MarkEventForManualReviewAsync(
                                evento,
                                addonAutoActivation.Reason ?? "El add-on de WhatsApp requiere revision manual antes de reactivarse.",
                                cancellationToken);

                            return new PaymentWebhookProcessingResult
                            {
                                EventId = webhook.EventId,
                                Reference = webhook.Reference,
                                IsProcessed = false,
                                Message = "Webhook recurrente pendiente de revision manual."
                            };
                        }
                    }

                    await ReactivateRecurringSubscriptionAsync(
                        tenantId,
                        internalPlan,
                        resolvedPlan,
                        webhook,
                        cancellationToken);

                    if (intento is not null)
                    {
                        intento.ProviderResultCode = FirstNonEmpty(webhook.StatusCode, "REPEAT_SUBSCRIPTION_REACTIVATED");
                        intento.ProviderResultMessage = Trim(
                            FirstNonEmpty(webhook.StatusDescription, "Reactivacion recibida desde webhook recurrente Tilopay.")!,
                            300);
                        intento.ProviderTransactionId = FirstNonEmpty(
                            webhook.ProviderTransactionId,
                            webhook.ProviderOrderNumber,
                            intento.ProviderTransactionId);
                        intento.ProviderSubscriberId = FirstNonEmpty(webhook.ProviderSubscriberId, intento.ProviderSubscriberId);
                        intento.ProviderReference = FirstNonEmpty(ResolveProviderReference(webhook), intento.ProviderReference);
                        intento.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                        intento.FechaActualizacionUtc = DateTime.UtcNow;
                    }
                }
                else if (IsRecurringPaymentSuccessEvent(webhook.EventType) || IsRecurringApproved(webhook))
                {
                    var paymentAttempt = intento ?? throw new InvalidOperationException(
                        "No fue posible crear el intento recurrente requerido para aprobar el pago.");

                    if (resolvedPlan.IsAddon)
                    {
                        var addonAutoActivation = await ValidateAddonAutomaticActivationAsync(
                            tenantId,
                            cancellationToken);

                        if (!addonAutoActivation.CanActivate)
                        {
                            paymentAttempt.Estado = EstadoPagoProveedor.ManualReview;
                            paymentAttempt.ProviderResultCode = "MANUAL_REVIEW";
                            paymentAttempt.ProviderResultMessage = Trim(
                                addonAutoActivation.Reason ?? "El add-on de WhatsApp requiere revision manual antes de activarse.",
                                300);
                            paymentAttempt.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                            paymentAttempt.FechaActualizacionUtc = DateTime.UtcNow;

                            evento.TenantId = correlation.TenantId;
                            evento.PlanId = internalPlan.Id;
                            evento.PagoSuscripcionId = paymentAttempt.Id;
                            evento.ProviderTransactionId = FirstNonEmpty(
                                webhook.ProviderTransactionId,
                                webhook.ProviderOrderNumber,
                                paymentAttempt.ProviderTransactionId);

                            await MarkEventForManualReviewAsync(
                                evento,
                                addonAutoActivation.Reason ?? "El add-on de WhatsApp requiere revision manual antes de activarse.",
                                cancellationToken);

                            return new PaymentWebhookProcessingResult
                            {
                                EventId = webhook.EventId,
                                Reference = webhook.Reference,
                                IsProcessed = false,
                                Message = "Webhook recurrente pendiente de revision manual."
                            };
                        }
                    }

                    if (!webhook.Amount.HasValue || webhook.Amount.Value <= 0m)
                    {
                        await MarkEventForManualReviewAsync(
                            evento,
                            "Tilopay no envio un monto aprobado util para el pago recurrente.",
                            cancellationToken);

                        return new PaymentWebhookProcessingResult
                        {
                            EventId = webhook.EventId,
                            Reference = webhook.Reference,
                            IsProcessed = false,
                            Message = "Webhook recurrente pendiente de revision manual por monto faltante."
                        };
                    }

                    var unexpectedAmountReason = GetUnexpectedRecurringAmountReason(webhook, resolvedPlan);
                    if (unexpectedAmountReason is not null)
                    {
                        paymentAttempt.Estado = EstadoPagoProveedor.ManualReview;
                        paymentAttempt.ProviderResultCode = "MANUAL_REVIEW";
                        paymentAttempt.ProviderResultMessage = Trim(
                            $"{webhook.StatusDescription} | {unexpectedAmountReason}",
                            300);
                        paymentAttempt.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                        paymentAttempt.FechaActualizacionUtc = DateTime.UtcNow;

                        evento.TenantId = correlation.TenantId;
                        evento.PlanId = internalPlan.Id;
                        evento.PagoSuscripcionId = paymentAttempt.Id;
                        evento.ProviderTransactionId = paymentAttempt.ProviderTransactionId;

                        await MarkEventForManualReviewAsync(
                            evento,
                            unexpectedAmountReason,
                            cancellationToken);

                        return new PaymentWebhookProcessingResult
                        {
                            EventId = webhook.EventId,
                            Reference = webhook.Reference,
                            IsProcessed = false,
                            Message = "Webhook recurrente pendiente de revision manual por monto inesperado."
                        };
                    }

                    var approval = await ApproveRecurringPaymentAsync(
                        new RecurringPaymentApprovalRequest
                        {
                            PaymentId = paymentAttempt.Id,
                            ProviderTransactionId = ResolveApprovedProviderTransactionId(webhook) ?? throw new InvalidOperationException(
                                "Tilopay no envio transactionId, numero de orden ni referencia util para aprobar el pago recurrente."),
                            ApprovedAmount = webhook.Amount.Value,
                            Currency = string.IsNullOrWhiteSpace(webhook.Currency) ? paymentAttempt.Moneda : webhook.Currency!,
                            ProviderSubscriberId = webhook.ProviderSubscriberId,
                            ProviderReference = ResolveProviderReference(webhook),
                            ProviderAuthorizationCode = webhook.AuthorizationCode,
                            NextBillingDateUtc = webhook.NextBillingDateUtc,
                            Source = "webhook",
                            Observation = "Pago recurrente aprobado automaticamente desde webhook Tilopay.",
                            CorrelationId = correlationId,
                            RawPayload = payload,
                            EventType = webhook.EventType,
                            ProviderResultCode = string.IsNullOrWhiteSpace(webhook.StatusCode) ? "1" : webhook.StatusCode,
                            ProviderResultMessage = webhook.StatusDescription,
                            CreateAuditEvent = false
                        },
                        cancellationToken);

                    paymentAttempt.Estado = approval.PaymentStatus;
                }
                else
                {
                    var paymentAttempt = intento ?? throw new InvalidOperationException(
                        "No fue posible crear el intento recurrente requerido para registrar el pago fallido.");

                    paymentAttempt.Estado = MapFailedStatus(webhook.StatusCode);
                    paymentAttempt.ProviderResultCode = FirstNonEmpty(webhook.StatusCode, "REPEAT_PAYMENT_FAILED");
                    paymentAttempt.ProviderResultMessage = Trim(
                        FirstNonEmpty(webhook.StatusDescription, "Pago recurrente no aprobado.")!,
                        300);
                    paymentAttempt.ProviderTransactionId = FirstNonEmpty(
                        webhook.ProviderTransactionId,
                        webhook.ProviderOrderNumber,
                        paymentAttempt.ProviderTransactionId);
                    paymentAttempt.ProviderAuthorizationCode = FirstNonEmpty(webhook.AuthorizationCode, paymentAttempt.ProviderAuthorizationCode);
                    paymentAttempt.ProviderSubscriberId = FirstNonEmpty(webhook.ProviderSubscriberId, paymentAttempt.ProviderSubscriberId);
                    paymentAttempt.ProviderReference = FirstNonEmpty(ResolveProviderReference(webhook), paymentAttempt.ProviderReference);
                    paymentAttempt.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                    paymentAttempt.FechaActualizacionUtc = DateTime.UtcNow;

                    if (resolvedPlan.IsAddon)
                    {
                        await _suscripcionService.RegistrarPagoFallidoAddonAsync(
                            tenantId,
                            resolvedPlan.Code,
                            webhook.ProviderSubscriberId,
                            paymentAttempt.ProviderTransactionId,
                            motivo: $"Pago recurrente WhatsApp no aprobado. Evento {webhook.EventType}.",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _suscripcionService.RegistrarPagoFallidoAsync(
                            tenantId,
                            internalPlan.Id,
                            paymentAttempt.Id,
                            PaymentProviderType.Tilopay,
                            paymentAttempt.ProviderReference ?? paymentAttempt.ReferenciaInterna,
                            paymentAttempt.ProviderTransactionId,
                            paymentAttempt.Monto,
                            paymentAttempt.Moneda,
                            $"Pago recurrente no aprobado. Evento {webhook.EventType}.",
                            cancellationToken);
                    }
                }

                evento.TenantId = correlation.TenantId;
                evento.PlanId = internalPlan.Id;
                evento.PagoSuscripcionId = intento?.Id;
                evento.ProviderTransactionId = FirstNonEmpty(
                    intento?.ProviderTransactionId,
                    webhook.ProviderTransactionId,
                    webhook.ProviderOrderNumber);
                evento.EstadoProcesamiento = "Procesado";
                evento.Procesado = true;
                evento.FechaProcesamientoUtc = DateTime.UtcNow;
                evento.Error = null;

                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Webhook recurrente Tilopay procesado correctamente. EventType {EventType}. TenantId {TenantId}. PlanCode {PlanCode}. EstadoPago {EstadoPago}.",
                    webhook.EventType,
                    correlation.TenantId,
                    resolvedPlan.Code,
                    intento?.Estado);

                await LogDevelopmentRecurringOutcomeAsync(
                    tenantId,
                    internalPlan.Id,
                    webhook,
                    intento,
                    cancellationToken);

                return new PaymentWebhookProcessingResult
                {
                    EventId = webhook.EventId,
                    Reference = webhook.Reference,
                    IsProcessed = true,
                    Message = "Webhook recurrente procesado correctamente.",
                    EstadoPago = intento?.Estado
                };
            }
            catch (Exception ex)
            {
                // Desconectar entidades que no se pudieron guardar para que no bloqueen
                // el intento de persistir el estado de error del evento.
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                {
                    if (!ReferenceEquals(entry.Entity, evento))
                        entry.State = EntityState.Detached;
                }

                evento.EstadoProcesamiento = "Error";
                evento.Error = Trim(ex.Message, 500);
                evento.FechaProcesamientoUtc = DateTime.UtcNow;

                try
                {
                    await _db.SaveChangesAsync(CancellationToken.None);
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

        private void ApplyIncomingRecurringEvent(
            PaymentProviderWebhookData webhook,
            string? incomingEvent)
        {
            if (string.IsNullOrWhiteSpace(incomingEvent) &&
                !webhook.IsRecurring &&
                !webhook.RecurringPlanId.HasValue &&
                _tilopayRepeatOptions.FindByCode(webhook.PlanCode) is null)
            {
                return;
            }

            var normalizedEvent = NormalizeRecurringEventType(
                string.IsNullOrWhiteSpace(incomingEvent)
                    ? webhook.EventType
                    : incomingEvent);

            if (string.IsNullOrWhiteSpace(normalizedEvent))
            {
                return;
            }

            webhook.EventType = normalizedEvent;
            webhook.IsRecurring = true;
            webhook.EventId = BuildRecurringWebhookEventId(webhook);

            if (IsRecurringPaymentSuccessEvent(normalizedEvent) && string.IsNullOrWhiteSpace(webhook.StatusCode))
            {
                webhook.StatusCode = "1";
                webhook.StatusDescription = FirstNonEmpty(
                    webhook.StatusDescription,
                    "Pago recurrente aprobado")!;
            }
            else if (IsRecurringPaymentFailedEvent(normalizedEvent) && string.IsNullOrWhiteSpace(webhook.StatusCode))
            {
                webhook.StatusCode = "2";
                webhook.StatusDescription = FirstNonEmpty(
                    webhook.StatusDescription,
                    "Pago recurrente rechazado")!;
            }
            else if (IsRecurringCancellationEvent(normalizedEvent) && string.IsNullOrWhiteSpace(webhook.StatusCode))
            {
                webhook.StatusCode = "3";
                webhook.StatusDescription = FirstNonEmpty(
                    webhook.StatusDescription,
                    "Suscripcion recurrente cancelada")!;
            }
            else if (IsRecurringReactivationEvent(normalizedEvent) && string.IsNullOrWhiteSpace(webhook.StatusCode))
            {
                webhook.StatusCode = "1";
                webhook.StatusDescription = FirstNonEmpty(
                    webhook.StatusDescription,
                    "Suscripcion recurrente reactivada")!;
            }
        }

        private async Task HandleRecurringRegistrationAsync(
            PaymentProviderWebhookData webhook,
            string payload,
            Guid tenantId,
            Plan internalPlan,
            TilopayRepeatPlanOption resolvedPlan,
            PagoSuscripcion? intento,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            if (intento is not null)
            {
                intento.ProviderResultCode = FirstNonEmpty(webhook.StatusCode, "REPEAT_REGISTRATION");
                intento.ProviderResultMessage = Trim(
                    FirstNonEmpty(webhook.StatusDescription, "Registro recurrente recibido desde webhook Tilopay.")!,
                    300);
                intento.ProviderReference = FirstNonEmpty(ResolveProviderReference(webhook), intento.ProviderReference);
                intento.ProviderSubscriberId = FirstNonEmpty(webhook.ProviderSubscriberId, intento.ProviderSubscriberId);
                intento.ProviderAuthorizationCode = FirstNonEmpty(webhook.AuthorizationCode, intento.ProviderAuthorizationCode);
                intento.UltimoPayloadProveedor = RedactSensitivePayload(payload);
                intento.FechaActualizacionUtc = DateTime.UtcNow;
            }

            await ApplyRecurringNextBillingDateOverrideAsync(
                tenantId,
                resolvedPlan.IsAddon,
                webhook.NextBillingDateUtc,
                cancellationToken);

            if (!resolvedPlan.IsAddon)
            {
                await ApplyRecurringPlanMetadataAsync(
                    tenantId,
                    internalPlan,
                    resolvedPlan,
                    webhook.ProviderSubscriberId,
                    webhook.NextBillingDateUtc,
                    cancellationToken);
            }

            if (webhook.HasFreeTrial == true && !resolvedPlan.IsAddon)
            {
                await _suscripcionService.ActivarSuscripcionAsync(
                    tenantId,
                    internalPlan.Id,
                    PaymentProviderType.Tilopay,
                    providerCustomerId: null,
                    providerSubscriptionId: webhook.ProviderSubscriberId,
                    providerPaymentLinkId: null,
                    providerTransactionId: null,
                    providerReference: FirstNonEmpty(
                        ResolveProviderReference(webhook),
                        intento?.ProviderReference,
                        intento?.CorrelationToken,
                        intento?.ReferenciaInterna),
                    trialEnd: webhook.NextBillingDateUtc,
                    motivo: "Registro recurrente Tilopay con free trial confirmado por webhook.",
                    cancellationToken: cancellationToken);
            }

            LogDevelopmentRecurringRegistration(webhook, intento, tenantId, internalPlan.Id, correlationId);
        }

        private async Task ReactivateRecurringSubscriptionAsync(
            Guid tenantId,
            Plan internalPlan,
            TilopayRepeatPlanOption resolvedPlan,
            PaymentProviderWebhookData webhook,
            CancellationToken cancellationToken)
        {
            if (resolvedPlan.IsAddon)
            {
                await _suscripcionService.ActivarAddonWhatsAppRecurrenteAsync(
                    tenantId,
                    internalPlan,
                    resolvedPlan.TilopayPlanId,
                    webhook.ProviderSubscriberId,
                    webhook.ProviderTransactionId,
                    motivo: "Reactivacion recibida desde webhook recurrente Tilopay.",
                    cancellationToken: cancellationToken);

                await ApplyRecurringNextBillingDateOverrideAsync(
                    tenantId,
                    isAddon: true,
                    webhook.NextBillingDateUtc,
                    cancellationToken);

                return;
            }

            await _suscripcionService.ActivarSuscripcionRecurrenteAsync(
                tenantId,
                internalPlan,
                resolvedPlan.TilopayPlanId,
                webhook.ProviderSubscriberId,
                webhook.ProviderTransactionId,
                ResolveProviderReference(webhook),
                motivo: "Reactivacion recibida desde webhook recurrente Tilopay.",
                cancellationToken: cancellationToken);

            await ApplyRecurringNextBillingDateOverrideAsync(
                tenantId,
                isAddon: false,
                webhook.NextBillingDateUtc,
                cancellationToken);
        }

        private async Task ApplyRecurringPlanMetadataAsync(
            Guid tenantId,
            Plan internalPlan,
            TilopayRepeatPlanOption resolvedPlan,
            string? providerSubscriberId,
            DateTime? nextBillingDateUtc,
            CancellationToken cancellationToken)
        {
            var subscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (subscription is null)
            {
                return;
            }

            subscription.PlanId = internalPlan.Id;
            subscription.CodigoPlan = internalPlan.Codigo ?? internalPlan.Nombre;
            subscription.TilopayRecurringPlanId = resolvedPlan.TilopayPlanId;
            subscription.ProviderSubscriptionId = providerSubscriberId ?? subscription.ProviderSubscriptionId;
            subscription.PrecioMensual = internalPlan.PrecioMensual;
            subscription.MonedaFacturacion = string.IsNullOrWhiteSpace(internalPlan.Moneda) ? "CRC" : internalPlan.Moneda;
            subscription.MaxFuncionarios = internalPlan.MaxFuncionarios;
            subscription.FechaProximoCobroUtc = nextBillingDateUtc ?? subscription.FechaProximoCobroUtc;
            subscription.FechaUltimaActualizacionUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task ApplyRecurringNextBillingDateOverrideAsync(
            Guid tenantId,
            bool isAddon,
            DateTime? nextBillingDateUtc,
            CancellationToken cancellationToken)
        {
            if (!nextBillingDateUtc.HasValue)
            {
                return;
            }

            if (isAddon)
            {
                var addon = await _db.TenantSubscriptionAddons
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

                if (addon is null)
                {
                    return;
                }

                addon.FechaProximoCobroUtc = nextBillingDateUtc.Value;
                addon.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var subscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (subscription is null)
            {
                return;
            }

            subscription.FechaProximoCobroUtc = nextBillingDateUtc.Value;
            subscription.FechaUltimaActualizacionUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task ApplyRecurringExpirationOverrideAsync(
            Guid tenantId,
            bool isAddon,
            DateTime? expirationDateUtc,
            CancellationToken cancellationToken)
        {
            if (!expirationDateUtc.HasValue)
            {
                return;
            }

            if (isAddon)
            {
                var addon = await _db.TenantSubscriptionAddons
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

                if (addon is null)
                {
                    return;
                }

                addon.FechaFin = expirationDateUtc.Value;
                addon.FechaCancelacionUtc = expirationDateUtc.Value;
                addon.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var subscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (subscription is null)
            {
                return;
            }

            subscription.FechaFin = expirationDateUtc.Value;
            subscription.FechaCancelacionUtc = expirationDateUtc.Value;
            subscription.FechaUltimaActualizacionUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkEventAsUnmatchedAsync(
            EventoPago evento,
            string reason,
            CancellationToken cancellationToken)
        {
            evento.EstadoProcesamiento = "SinRelacion";
            evento.Error = Trim(reason, 500);
            evento.FechaProcesamientoUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Webhook recurrente Tilopay sin correlacion. EventId {EventId}. Reason {Reason}.",
                evento.ProveedorEventId,
                reason);
        }

        private async Task<RecurringCorrelationResolution> ResolveRecurringCorrelationAsync(
            PaymentProviderWebhookData webhook,
            Guid? planId,
            int? recurringPlanId,
            CancellationToken cancellationToken)
        {
            var isRegistrationEvent = IsRecurringRegistrationEvent(webhook.EventType);
            var isPaymentSuccessEvent = IsRecurringPaymentSuccessEvent(webhook.EventType);
            var allowOpenPendingCorrelation = isRegistrationEvent || isPaymentSuccessEvent || IsRecurringPaymentFailedEvent(webhook.EventType);
            var allowTenantLookupByEmail = !isPaymentSuccessEvent;

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
                        Status: RecurringCorrelationStatus.Matched,
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
                        Status: RecurringCorrelationStatus.Matched,
                        ManualReviewReason: null);
                }

                if (addonSubscription is not null)
                {
                    return new RecurringCorrelationResolution(
                        addonSubscription.TenantId,
                        addonSubscription.PlanId,
                        PaymentAttempt: null,
                        Status: RecurringCorrelationStatus.Matched,
                        ManualReviewReason: null);
                }
            }

            if (recurringPlanId.HasValue)
            {
                var lookupWindowUtc = DateTime.UtcNow.AddHours(-48);
                var pendingAttempts = _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .Where(p =>
                        p.Proveedor == PaymentProviderType.Tilopay &&
                        p.TilopayRecurringPlanId == recurringPlanId.Value &&
                        p.FechaCreacionUtc >= lookupWindowUtc);

                if (!string.IsNullOrWhiteSpace(webhook.CustomerEmail))
                {
                    pendingAttempts = pendingAttempts.Where(p => p.ClienteEmail == webhook.CustomerEmail);
                }

                if (planId.HasValue)
                {
                    pendingAttempts = pendingAttempts.Where(p => p.PlanId == planId.Value);
                }

                if (webhook.Amount.HasValue)
                {
                    var amount = webhook.Amount.Value;
                    pendingAttempts = pendingAttempts.Where(p => p.Monto >= amount - 0.01m && p.Monto <= amount + 0.01m);
                }

                if (allowOpenPendingCorrelation)
                {
                    pendingAttempts = pendingAttempts.Where(p => p.Estado == EstadoPagoProveedor.Pendiente);
                }

                var candidates = await pendingAttempts
                    .OrderByDescending(p => p.FechaCreacionUtc)
                    .Take(3)
                    .ToListAsync(cancellationToken);

                if (candidates.Count == 1)
                {
                    var payment = candidates[0];
                    return new RecurringCorrelationResolution(
                        payment.TenantId,
                        payment.PlanId,
                        PaymentAttempt: allowOpenPendingCorrelation ? payment : null,
                        Status: RecurringCorrelationStatus.Matched,
                        ManualReviewReason: null);
                }

                if (candidates.Count > 1)
                {
                    if (!allowOpenPendingCorrelation)
                    {
                        var tenantPlanPairs = candidates
                            .Select(payment => new { payment.TenantId, payment.PlanId })
                            .Distinct()
                            .ToList();

                        if (tenantPlanPairs.Count == 1)
                        {
                            return new RecurringCorrelationResolution(
                                tenantPlanPairs[0].TenantId,
                                tenantPlanPairs[0].PlanId,
                                PaymentAttempt: null,
                                Status: RecurringCorrelationStatus.Matched,
                                ManualReviewReason: null);
                        }
                    }

                    return RecurringCorrelationResolution.Manual(
                        "El webhook recurrente coincide con multiples signups pendientes del mismo plan y requiere revision manual.");
                }
            }

            if (isPaymentSuccessEvent &&
                recurringPlanId.HasValue &&
                webhook.Amount.HasValue &&
                webhook.Amount.Value > 0m)
            {
                var lookupWindowUtc = DateTime.UtcNow.AddHours(-48);
                var pendingAttemptsIgnoringAmount = _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .Where(p =>
                        p.Proveedor == PaymentProviderType.Tilopay &&
                        p.TilopayRecurringPlanId == recurringPlanId.Value &&
                        p.FechaCreacionUtc >= lookupWindowUtc &&
                        p.Estado == EstadoPagoProveedor.Pendiente);

                if (!string.IsNullOrWhiteSpace(webhook.CustomerEmail))
                {
                    pendingAttemptsIgnoringAmount = pendingAttemptsIgnoringAmount.Where(p => p.ClienteEmail == webhook.CustomerEmail);
                }

                if (planId.HasValue)
                {
                    pendingAttemptsIgnoringAmount = pendingAttemptsIgnoringAmount.Where(p => p.PlanId == planId.Value);
                }

                var candidatesIgnoringAmount = await pendingAttemptsIgnoringAmount
                    .OrderByDescending(p => p.FechaCreacionUtc)
                    .Take(3)
                    .ToListAsync(cancellationToken);

                if (candidatesIgnoringAmount.Count == 1)
                {
                    var payment = candidatesIgnoringAmount[0];
                    var currency = FirstNonEmpty(webhook.Currency, payment.Moneda, "CRC")!;

                    return new RecurringCorrelationResolution(
                        payment.TenantId,
                        payment.PlanId,
                        payment,
                        RecurringCorrelationStatus.ManualReview,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "El webhook recurrente llego con monto {0:0.00} {1} pero el pending abierto usa {2:0.00} {1}. Se requiere revision manual antes de activar.",
                            webhook.Amount.Value,
                            currency,
                            payment.Monto));
                }

                if (candidatesIgnoringAmount.Count > 1)
                {
                    return RecurringCorrelationResolution.Manual(
                        "El webhook recurrente no coincide por monto y hay multiples pendientes compatibles. Se requiere revision manual.");
                }
            }

            if (isPaymentSuccessEvent && recurringPlanId.HasValue)
            {
                var lookupWindowUtc = DateTime.UtcNow.AddHours(-48);
                var pendingAttemptsIgnoringEmail = _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .Where(p =>
                        p.Proveedor == PaymentProviderType.Tilopay &&
                        p.TilopayRecurringPlanId == recurringPlanId.Value &&
                        p.FechaCreacionUtc >= lookupWindowUtc &&
                        p.Estado == EstadoPagoProveedor.Pendiente);

                if (planId.HasValue)
                {
                    pendingAttemptsIgnoringEmail = pendingAttemptsIgnoringEmail.Where(p => p.PlanId == planId.Value);
                }

                if (webhook.Amount.HasValue)
                {
                    var amount = webhook.Amount.Value;
                    pendingAttemptsIgnoringEmail = pendingAttemptsIgnoringEmail.Where(
                        p => p.Monto >= amount - 0.01m && p.Monto <= amount + 0.01m);
                }

                var candidatesIgnoringEmail = await pendingAttemptsIgnoringEmail
                    .OrderByDescending(p => p.FechaCreacionUtc)
                    .Take(3)
                    .ToListAsync(cancellationToken);

                if (candidatesIgnoringEmail.Count == 1)
                {
                    var payment = candidatesIgnoringEmail[0];
                    return new RecurringCorrelationResolution(
                        payment.TenantId,
                        payment.PlanId,
                        payment,
                        RecurringCorrelationStatus.ManualReview,
                        $"El webhook recurrente llego con el correo {webhook.CustomerEmail ?? "(sin correo)"} pero el pending abierto usa {payment.ClienteEmail ?? "(sin correo)"}. Se requiere revision manual antes de activar.");
                }

                if (candidatesIgnoringEmail.Count > 1)
                {
                    return RecurringCorrelationResolution.Manual(
                        "El webhook recurrente no coincide por correo y hay multiples pendientes compatibles. Se requiere revision manual.");
                }
            }

            if (allowTenantLookupByEmail &&
                recurringPlanId.HasValue &&
                planId.HasValue &&
                !string.IsNullOrWhiteSpace(webhook.CustomerEmail))
            {
                var candidateTenantIds = await _db.AppUsuario
                    .IgnoreQueryFilters()
                    .Where(user => user.TenantId != Guid.Empty && user.Email == webhook.CustomerEmail)
                    .Select(user => user.TenantId)
                    .Distinct()
                    .Take(3)
                    .ToListAsync(cancellationToken);

                if (candidateTenantIds.Count == 1)
                {
                    var tenantId = candidateTenantIds[0];

                    var hasBaseSubscription = await _db.Suscripciones
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .AnyAsync(subscription =>
                            subscription.TenantId == tenantId &&
                            subscription.PlanId == planId.Value &&
                            subscription.TilopayRecurringPlanId == recurringPlanId.Value,
                            cancellationToken);

                    if (hasBaseSubscription)
                    {
                        return new RecurringCorrelationResolution(
                            tenantId,
                            planId.Value,
                            PaymentAttempt: null,
                            Status: RecurringCorrelationStatus.Matched,
                            ManualReviewReason: null);
                    }

                    var hasAddonSubscription = await _db.TenantSubscriptionAddons
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .AnyAsync(addon =>
                            addon.TenantId == tenantId &&
                            addon.PlanId == planId.Value &&
                            addon.TilopayRecurringPlanId == recurringPlanId.Value,
                            cancellationToken);

                    if (hasAddonSubscription)
                    {
                        return new RecurringCorrelationResolution(
                            tenantId,
                            planId.Value,
                            PaymentAttempt: null,
                            Status: RecurringCorrelationStatus.Matched,
                            ManualReviewReason: null);
                    }
                }

                if (candidateTenantIds.Count > 1)
                {
                    return RecurringCorrelationResolution.Manual(
                        "El webhook recurrente coincide con multiples tenants para el mismo correo y requiere revision manual.");
                }
            }

            if (isPaymentSuccessEvent)
            {
                return RecurringCorrelationResolution.Unmatched(
                    "No existe un pending recurrente vigente para el plan y correo recibidos.");
            }

            return RecurringCorrelationResolution.Unmatched(
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

        private async Task<AddonAutomaticActivationValidation> ValidateAddonAutomaticActivationAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            var baseSubscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(subscription => subscription.Plan)
                .Where(subscription => subscription.TenantId == tenantId)
                .OrderByDescending(subscription => subscription.FechaUltimaActualizacionUtc ?? subscription.FechaInicio)
                .ThenByDescending(subscription => subscription.FechaInicio)
                .FirstOrDefaultAsync(cancellationToken);

            if (baseSubscription is null)
            {
                return AddonAutomaticActivationValidation.Blocked(
                    "El tenant no tiene un plan base activo. Se requiere revision manual antes de activar el add-on de WhatsApp.");
            }

            var basePlanCode = baseSubscription.CodigoPlan ?? baseSubscription.Plan?.Codigo;
            if (!string.IsNullOrWhiteSpace(basePlanCode) &&
                PlanCodes.WhatsAppAddons.Contains(basePlanCode, StringComparer.OrdinalIgnoreCase))
            {
                return AddonAutomaticActivationValidation.Blocked(
                    "La suscripcion actual del tenant no corresponde a un plan base valido. Se requiere revision manual antes de activar el add-on de WhatsApp.");
            }

            if (!_suscripcionService.CanAccessApp(baseSubscription))
            {
                var effectiveStatus = _suscripcionService.GetEffectiveStatus(baseSubscription);
                return AddonAutomaticActivationValidation.Blocked(
                    $"El tenant no tiene un plan base activo para autoactivar el add-on de WhatsApp. Estado actual: {effectiveStatus}.");
            }

            return AddonAutomaticActivationValidation.Allowed();
        }

        private async Task ExpireOpenRecurringPendingAttemptsAsync(
            Guid tenantId,
            Guid planId,
            string customerEmail,
            int recurringPlanId,
            CancellationToken cancellationToken)
        {
            var existingOpenAttempts = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .Where(payment =>
                    payment.TenantId == tenantId &&
                    payment.PlanId == planId &&
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.TilopayRecurringPlanId == recurringPlanId &&
                    payment.ClienteEmail == customerEmail &&
                    (payment.Estado == EstadoPagoProveedor.Pendiente ||
                     payment.Estado == EstadoPagoProveedor.ManualReview))
                .ToListAsync(cancellationToken);

            if (existingOpenAttempts.Count == 0)
            {
                return;
            }

            foreach (var existingAttempt in existingOpenAttempts)
            {
                existingAttempt.Estado = EstadoPagoProveedor.Expirado;
                existingAttempt.ProviderResultCode = "EXPIRED_BY_NEW_CHECKOUT";
                existingAttempt.ProviderResultMessage = "El pending recurrente fue reemplazado por un checkout mas reciente del mismo tenant, email y plan.";
                existingAttempt.FechaActualizacionUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private static bool IsOpenRecurringPaymentStatus(EstadoPagoProveedor status) =>
            status == EstadoPagoProveedor.Pendiente || status == EstadoPagoProveedor.ManualReview;

        private static bool IsTerminalRecurringPaymentStatus(EstadoPagoProveedor status) =>
            status == EstadoPagoProveedor.Confirmado ||
            status == EstadoPagoProveedor.Cancelado ||
            status == EstadoPagoProveedor.Expirado ||
            status == EstadoPagoProveedor.Fallido;

        private static string BuildRecurringApprovalMessage(string source, string? observation)
        {
            var sourceLabel = string.Equals(source, "webhook", StringComparison.OrdinalIgnoreCase)
                ? "Webhook aprobado por Tilopay."
                : "Aprobado manualmente desde conciliacion interna.";

            if (string.IsNullOrWhiteSpace(observation))
            {
                return sourceLabel;
            }

            return Trim($"{sourceLabel} Observacion: {observation.Trim()}", 300);
        }

        private static string BuildRecurringApprovalReason(string source, string? observation)
        {
            var baseReason = string.Equals(source, "webhook", StringComparison.OrdinalIgnoreCase)
                ? "Pago recurrente aprobado por webhook Tilopay."
                : "Pago recurrente aprobado manualmente desde conciliacion interna.";

            if (string.IsNullOrWhiteSpace(observation))
            {
                return baseReason;
            }

            return Trim($"{baseReason} Observacion: {observation.Trim()}", 250);
        }

        private string BuildRecurringApprovalAuditPayload(
            RecurringPaymentApprovalRequest request,
            PagoSuscripcion intento,
            TilopayRepeatPlanOption repeatPlan,
            Plan plan) =>
            JsonSerializer.Serialize(new
            {
                phase = "recurring_payment_approved",
                source = request.Source,
                actorUserId = request.ActorUserId,
                actorEmail = request.ActorEmail,
                observation = request.Observation,
                paymentId = intento.Id,
                tenantId = intento.TenantId,
                planId = plan.Id,
                planCode = plan.Codigo ?? plan.Nombre,
                recurringPlanId = repeatPlan.TilopayPlanId,
                approvedAmount = request.ApprovedAmount,
                approvedCurrency = NormalizeRecurringCurrency(request.Currency, repeatPlan, plan),
                providerTransactionId = request.ProviderTransactionId,
                providerAuthorizationCode = request.ProviderAuthorizationCode,
                providerSubscriberId = request.ProviderSubscriberId,
                providerReference = request.ProviderReference ?? intento.ProviderReference,
                nextBillingDateUtc = request.NextBillingDateUtc,
                correlationToken = intento.CorrelationToken,
                correlationId = request.CorrelationId,
                eventType = request.EventType,
                providerResultCode = request.ProviderResultCode,
                providerResultMessage = request.ProviderResultMessage,
                rawPayload = string.IsNullOrWhiteSpace(request.RawPayload)
                    ? null
                    : RedactSensitivePayload(request.RawPayload)
            });

        private TilopayRepeatPlanRegistration? ResolveRecurringPlanRegistration(
            PagoSuscripcion intento,
            Plan plan)
        {
            if (intento.TilopayRecurringPlanId.HasValue)
            {
                var byRecurringPlanId = _tilopayRepeatOptions.FindRegistrationByRecurringPlanId(intento.TilopayRecurringPlanId.Value);
                if (byRecurringPlanId is not null)
                {
                    return byRecurringPlanId;
                }
            }

            return _tilopayRepeatOptions.FindRegistrationByCode(plan.Codigo);
        }

        private static void EnsureRecurringPaymentCanBeApproved(PagoSuscripcion intento)
        {
            if (intento.Estado == EstadoPagoProveedor.Confirmado)
            {
                throw new InvalidOperationException("Este pending recurrente ya fue aprobado anteriormente.");
            }

            if (IsTerminalRecurringPaymentStatus(intento.Estado))
            {
                throw new InvalidOperationException(
                    $"El pending recurrente ya no puede aprobarse porque esta en estado {intento.Estado}.");
            }

            if (!IsOpenRecurringPaymentStatus(intento.Estado))
            {
                throw new InvalidOperationException(
                    $"El estado actual {intento.Estado} no puede pasar por conciliacion manual.");
            }
        }

        private static void EnsureRecurringPaymentIsCurrent(PagoSuscripcion intento)
        {
            if (DateTime.UtcNow - intento.FechaCreacionUtc > RecurringPendingLifetime)
            {
                throw new InvalidOperationException(
                    $"El pending recurrente ya no esta vigente para conciliacion manual. Limite: {RecurringPendingLifetime.TotalHours:0} horas.");
            }
        }

        private void EnsureApprovedAmountMatchesPlan(
            decimal approvedAmount,
            string currency,
            TilopayRepeatPlanOption repeatPlan,
            Plan plan)
        {
            if (approvedAmount <= 0m)
            {
                throw new InvalidOperationException("El monto aprobado debe ser mayor que cero.");
            }

            var expectedCurrency = NormalizeRecurringCurrency(currency, repeatPlan, plan);
            var webhook = new PaymentProviderWebhookData
            {
                Amount = approvedAmount,
                Currency = expectedCurrency
            };

            var mismatchReason = GetUnexpectedRecurringAmountReason(webhook, repeatPlan);
            if (mismatchReason is not null)
            {
                throw new InvalidOperationException(mismatchReason);
            }
        }

        private static string NormalizeRecurringCurrency(
            string? currency,
            TilopayRepeatPlanOption repeatPlan,
            Plan plan)
        {
            var normalized = FirstNonEmpty(currency, repeatPlan.Currency, plan.Moneda, "CRC")!;
            return normalized.ToUpperInvariant();
        }

        private async Task EnsureProviderTransactionIsUniqueAsync(
            Guid paymentId,
            string providerTransactionId,
            CancellationToken cancellationToken)
        {
            var duplicatedConfirmedPayment = await _db.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(payment =>
                    payment.Id != paymentId &&
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    payment.Estado == EstadoPagoProveedor.Confirmado &&
                    payment.ProviderTransactionId == providerTransactionId,
                    cancellationToken);

            if (duplicatedConfirmedPayment is not null)
            {
                throw new InvalidOperationException(
                    "Ya existe otro pago recurrente confirmado con ese transactionId o numero de orden.");
            }
        }

        private async Task EnsureInvoiceAsync(
            Guid tenantId,
            Guid planId,
            PagoSuscripcion intento,
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
            if (evento.PagoSuscripcionId.HasValue)
            {
                var intento = await _db.PagosSuscripcion
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(payment => payment.Id == evento.PagoSuscripcionId.Value, cancellationToken);

                if (intento is not null && intento.Estado == EstadoPagoProveedor.Pendiente)
                {
                    intento.Estado = EstadoPagoProveedor.ManualReview;
                    intento.ProviderResultCode = "MANUAL_REVIEW";
                    intento.ProviderResultMessage = Trim(reason, 300);
                    intento.FechaActualizacionUtc = DateTime.UtcNow;
                }
            }

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
            if (IsRecurringPaymentSuccessEvent(webhook.EventType) ||
                IsRecurringReactivationEvent(webhook.EventType))
            {
                return true;
            }

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
            if (IsRecurringCancellationEvent(webhook.EventType))
            {
                return true;
            }

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

        private string? GetUnexpectedRecurringAmountReason(
            PaymentProviderWebhookData webhook,
            TilopayRepeatPlanOption resolvedPlan)
        {
            var expectedAmount = resolvedPlan.ExpectedFirstChargeAmount;
            var expectedCurrency = string.IsNullOrWhiteSpace(resolvedPlan.Currency)
                ? "CRC"
                : resolvedPlan.Currency.ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(webhook.Currency) &&
                !string.Equals(webhook.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Moneda recibida {0} no coincide con la moneda esperada {1} para {2}.",
                    webhook.Currency,
                    expectedCurrency,
                    resolvedPlan.Code);
            }

            if (!webhook.Amount.HasValue || webhook.Amount.Value <= 0m)
            {
                return null;
            }

            if (AmountsMatch(webhook.Amount.Value, expectedAmount))
            {
                return null;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "Monto aprobado {0:0.00} {1} no coincide con el primer cobro esperado {2:0.00} {1} para {3}. Revisa el plan en Tilopay: Monto por pago inicial debe ser 0.00 y Monto de cobro recurrente debe ser {2:0.00}.",
                webhook.Amount.Value,
                expectedCurrency,
                expectedAmount,
                resolvedPlan.Code);
        }

        private static bool AmountsMatch(decimal receivedAmount, decimal expectedAmount) =>
            Math.Abs(receivedAmount - expectedAmount) <= 0.01m;

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
                return "[non-json payload omitted]";
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
            FirstNonEmpty(
                webhook.ProviderOrderNumber,
                webhook.Reference,
                webhook.ProviderTransactionId) ?? string.Empty;

        private static string? ResolveApprovedProviderTransactionId(PaymentProviderWebhookData webhook) =>
            FirstNonEmpty(
                webhook.ProviderTransactionId,
                NormalizeProviderOrderNumber(webhook.ProviderOrderNumber),
                webhook.Reference);

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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

        private static string? NormalizeRecurringEventType(string? eventType) =>
            string.IsNullOrWhiteSpace(eventType)
                ? null
                : eventType.Trim().Replace('.', '_').Replace('-', '_').ToLowerInvariant();

        private static bool IsRecurringRegistrationEvent(string? eventType) =>
            string.Equals(NormalizeRecurringEventType(eventType), "repeat_registration", StringComparison.Ordinal);

        private static bool IsRecurringPaymentSuccessEvent(string? eventType)
        {
            var normalizedEventType = NormalizeRecurringEventType(eventType);
            return string.Equals(normalizedEventType, "repeat_payment_success", StringComparison.Ordinal) ||
                   string.Equals(normalizedEventType, "repeat_payment_paid", StringComparison.Ordinal);
        }

        private static bool IsRecurringPaymentFailedEvent(string? eventType) =>
            string.Equals(NormalizeRecurringEventType(eventType), "repeat_payment_failed", StringComparison.Ordinal);

        private static bool IsRecurringCancellationEvent(string? eventType) =>
            string.Equals(NormalizeRecurringEventType(eventType), "repeat_subscription_cancelled", StringComparison.Ordinal);

        private static bool IsRecurringReactivationEvent(string? eventType) =>
            string.Equals(NormalizeRecurringEventType(eventType), "repeat_subscription_reactivated", StringComparison.Ordinal);

        private static string BuildRecurringWebhookEventId(PaymentProviderWebhookData webhook)
        {
            var normalizedEventType = NormalizeRecurringEventType(webhook.EventType) ?? "repeat_notification";
            var stableSuffix = FirstNonEmpty(
                webhook.ProviderTransactionId,
                NormalizeProviderOrderNumber(webhook.ProviderOrderNumber),
                webhook.ProviderSubscriberId,
                webhook.Reference,
                webhook.CustomerEmail,
                webhook.NextBillingDateUtc?.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                webhook.ExpirationDateUtc?.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
                ?? Guid.NewGuid().ToString("N");

            return $"tilopay-repeat-{normalizedEventType}-{webhook.RecurringPlanId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}-{stableSuffix}";
        }

        private static bool IsTerminal(EventoPago evento) =>
            evento.Procesado ||
            string.Equals(evento.EstadoProcesamiento, "Rechazado", StringComparison.OrdinalIgnoreCase);

        private void LogDevelopmentRecurringCorrelation(
            PaymentProviderWebhookData webhook,
            RecurringCorrelationResolution correlation,
            int? resolvedRecurringPlanId)
        {
            if (_environment?.IsDevelopment() != true)
            {
                return;
            }

            _logger.LogInformation(
                "Tilopay repeat correlacion Development. Event {Event}. EventId {EventId}. PlanIdTilopay {PlanIdTilopay}. Email {Email}. Amount {Amount}. OrderNumber {OrderNumber}. Auth {Auth}. PendingFound {PendingFound}. PaymentAttemptId {PaymentAttemptId}. TenantId {TenantId}. PlanId {PlanId}. CorrelationStatus {CorrelationStatus}. Reason {Reason}.",
                webhook.EventType,
                webhook.EventId,
                resolvedRecurringPlanId,
                webhook.CustomerEmail,
                webhook.Amount,
                webhook.ProviderOrderNumber,
                webhook.AuthorizationCode,
                correlation.PaymentAttempt is not null,
                correlation.PaymentAttempt?.Id,
                correlation.TenantId,
                correlation.PlanId,
                correlation.Status,
                correlation.ManualReviewReason);
        }

        private void LogDevelopmentRecurringRegistration(
            PaymentProviderWebhookData webhook,
            PagoSuscripcion? intento,
            Guid tenantId,
            Guid planId,
            string? correlationId)
        {
            if (_environment?.IsDevelopment() != true)
            {
                return;
            }

            _logger.LogInformation(
                "Tilopay repeat registro Development. Event {Event}. CorrelationId {CorrelationId}. PlanIdTilopay {PlanIdTilopay}. Email {Email}. Amount {Amount}. NextPaymentDateUtc {NextPaymentDateUtc}. FreeTrial {FreeTrial}. TenantId {TenantId}. PlanId {PlanId}. PaymentId {PaymentId}.",
                webhook.EventType,
                correlationId,
                webhook.RecurringPlanId,
                webhook.CustomerEmail,
                webhook.Amount,
                webhook.NextBillingDateUtc,
                webhook.HasFreeTrial,
                tenantId,
                planId,
                intento?.Id);
        }

        private async Task LogDevelopmentRecurringOutcomeAsync(
            Guid tenantId,
            Guid planId,
            PaymentProviderWebhookData webhook,
            PagoSuscripcion? intento,
            CancellationToken cancellationToken)
        {
            if (_environment?.IsDevelopment() != true)
            {
                return;
            }

            var subscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(current => current.TenantId == tenantId)
                .Select(current => new
                {
                    current.Id,
                    current.Estado
                })
                .FirstOrDefaultAsync(cancellationToken);

            _logger.LogInformation(
                "Tilopay repeat resultado Development. Event {Event}. PlanIdTilopay {PlanIdTilopay}. Email {Email}. Amount {Amount}. OrderNumber {OrderNumber}. Auth {Auth}. TenantId {TenantId}. PlanId {PlanId}. PaymentId {PaymentId}. SubscriptionId {SubscriptionId}. PaymentStatus {PaymentStatus}. SubscriptionStatus {SubscriptionStatus}.",
                webhook.EventType,
                webhook.RecurringPlanId,
                webhook.CustomerEmail,
                webhook.Amount,
                webhook.ProviderOrderNumber,
                webhook.AuthorizationCode,
                tenantId,
                planId,
                intento?.Id,
                subscription?.Id,
                intento?.Estado,
                subscription?.Estado);
        }

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

        private static string SanitizeRecurringCheckoutUrlForLog(string url)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return SanitizeSensitiveUrl(url);
            }

            var query = QueryHelpers.ParseQuery(uri.Query);
            if (query.Count == 0)
            {
                return uri.ToString();
            }

            var sanitizedQuery = new List<KeyValuePair<string, string?>>();
            foreach (var pair in query)
            {
                sanitizedQuery.Add(new KeyValuePair<string, string?>(
                    pair.Key,
                    string.Equals(pair.Key, "lc_email", StringComparison.OrdinalIgnoreCase)
                        ? "***redacted***"
                        : pair.Value.ToString()));
            }

            var builder = new UriBuilder(uri)
            {
                Query = QueryString.Create(sanitizedQuery).Value?.TrimStart('?') ?? string.Empty
            };

            return builder.Uri.ToString();
        }

        private static string Trim(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        private enum RecurringCorrelationStatus
        {
            Matched,
            ManualReview,
            Unmatched
        }

        private sealed record RecurringCorrelationResolution(
            Guid? TenantId,
            Guid? PlanId,
            PagoSuscripcion? PaymentAttempt,
            RecurringCorrelationStatus Status,
            string? ManualReviewReason)
        {
            public bool RequiresManualReview => Status == RecurringCorrelationStatus.ManualReview;
            public bool IsUnmatched => Status == RecurringCorrelationStatus.Unmatched;

            public static RecurringCorrelationResolution Manual(string reason) =>
                new(
                    TenantId: null,
                    PlanId: null,
                    PaymentAttempt: null,
                    Status: RecurringCorrelationStatus.ManualReview,
                    ManualReviewReason: reason);

            public static RecurringCorrelationResolution Unmatched(string reason) =>
                new(
                    TenantId: null,
                    PlanId: null,
                    PaymentAttempt: null,
                    Status: RecurringCorrelationStatus.Unmatched,
                    ManualReviewReason: reason);
        }

        private sealed record AddonAutomaticActivationValidation(
            bool CanActivate,
            string? Reason)
        {
            public static AddonAutomaticActivationValidation Allowed() => new(true, null);

            public static AddonAutomaticActivationValidation Blocked(string reason) => new(false, reason);
        }
    }
}
