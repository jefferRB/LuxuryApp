using System.Net;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Marketing;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.SaaS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class BillingController : Controller
    {
        private const int CheckoutAutoRefreshSeconds = 4;
        private const int CheckoutAutoRefreshMaxAttempts = 5;

        private readonly ILogger<BillingController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly SaaSPaymentService _paymentService;
        private readonly IPromotionalCodeService _promotionalCodeService;
        private readonly ITenantCommercialAccessResolver _commercialAccessResolver;
        private readonly IPublicSiteContentService _publicSiteContentService;
        private readonly PublicCallbackHealthService _publicCallbackHealthService;
        private readonly UserManager<AppUsuario> _userManager;
        private readonly OpcionesTilopay _tilopayOptions;
        private readonly OpcionesPago _paymentOptions;

        public BillingController(
            ILogger<BillingController> logger,
            ApplicationDbContext context,
            SaaSPaymentService paymentService,
            IPromotionalCodeService promotionalCodeService,
            ITenantCommercialAccessResolver commercialAccessResolver,
            IPublicSiteContentService publicSiteContentService,
            PublicCallbackHealthService publicCallbackHealthService,
            UserManager<AppUsuario> userManager,
            IOptions<OpcionesTilopay> tilopayOptions,
            IOptions<OpcionesPago> paymentOptions)
        {
            _logger = logger;
            _context = context;
            _paymentService = paymentService;
            _promotionalCodeService = promotionalCodeService;
            _commercialAccessResolver = commercialAccessResolver;
            _publicSiteContentService = publicSiteContentService;
            _publicCallbackHealthService = publicCallbackHealthService;
            _userManager = userManager;
            _tilopayOptions = tilopayOptions.Value;
            _paymentOptions = paymentOptions.Value;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Planes(Guid? selectedPlanId = null, CancellationToken cancellationToken = default)
        {
            var planCards = await _publicSiteContentService.GetPlanCardsAsync(cancellationToken);

            var user = await _userManager.GetUserAsync(User);
            TenantCommercialAccessResult? currentAccess = null;

            if (user is not null && user.TenantId != Guid.Empty)
            {
                currentAccess = await _commercialAccessResolver.ResolveAsync(
                    user.TenantId,
                    user,
                    cancellationToken);
            }

            return View(new BillingPlanesViewModel
            {
                PlanCards = planCards,
                CurrentAccess = currentAccess,
                IsAuthenticated = user is not null,
                SelectedPlanId = selectedPlanId
            });
        }

        public IActionResult SinSuscripcion()
        {
            return View();
        }

        public IActionResult PlanVencido()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AplicarCodigoPromocional(string accessCode, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            if (user.TenantId == Guid.Empty)
            {
                TempData["BillingError"] = "Tu cuenta no tiene un tenant asociado para aplicar el código.";
                return RedirectToAction(nameof(Planes));
            }

            var redemption = await _promotionalCodeService.RedeemAsync(
                accessCode,
                user.TenantId,
                user,
                cancellationToken);

            if (!redemption.Succeeded)
            {
                TempData["BillingError"] = redemption.Error ?? "No fue posible aplicar el código promocional.";
                return RedirectToAction(nameof(Planes));
            }

            TempData["BillingSuccess"] = $"Código aplicado correctamente. Tu acceso queda habilitado hasta {redemption.AccessGrant!.FechaFinUtc:yyyy-MM-dd HH:mm} UTC.";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> Exito(
            string? orderNumber = null,
            string? reference = null,
            string? code = null,
            string? description = null,
            int pollAttempt = 0)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null || user.TenantId == Guid.Empty)
            {
                return Challenge();
            }

            var requestedReference = !string.IsNullOrWhiteSpace(reference)
                ? reference.Trim()
                : orderNumber?.Trim();

            var checkoutId = TryExtractTilopayCheckoutId(orderNumber);
            var matchingPayments = await FindMatchingPaymentsAsync(requestedReference, checkoutId);

            if (matchingPayments.Count > 1)
            {
                _logger.LogError(
                    "Billing/Exito detecto una correlacion ambigua. UserId {UserId}. TenantId {TenantId}. RequestedReference {RequestedReference}. CheckoutId {CheckoutId}. Matches {Matches}.",
                    user.Id,
                    user.TenantId,
                    requestedReference,
                    checkoutId,
                    matchingPayments.Count);

                return BuildRestrictedCheckoutResult(
                    requestedReference,
                    code,
                    description,
                    "No fue posible validar de forma segura el retorno del pago. Inicia sesion nuevamente y vuelve a intentarlo.");
            }

            var pago = matchingPayments.SingleOrDefault();
            if (pago is not null && pago.TenantId != user.TenantId)
            {
                _logger.LogWarning(
                    "Billing/Exito rechazo un retorno cross-tenant. UserId {UserId}. CurrentTenantId {CurrentTenantId}. PaymentTenantId {PaymentTenantId}. RequestedReference {RequestedReference}. CheckoutId {CheckoutId}. PaymentId {PaymentId}.",
                    user.Id,
                    user.TenantId,
                    pago.TenantId,
                    requestedReference,
                    checkoutId,
                    pago.Id);

                return BuildRestrictedCheckoutResult(
                    requestedReference,
                    code,
                    description,
                    "Este retorno de pago no pertenece a la cuenta autenticada. Ingresa con la cuenta que inicio el checkout.");
            }

            var suscripcion = pago is null
                ? null
                : await FindCurrentSubscriptionAsync(user.TenantId);

            var pollAttemptNormalized = Math.Max(0, pollAttempt);
            var suscripcionActiva = IsSubscriptionActive(suscripcion);
            var confirmadoPorWebhook = pago?.Estado == EstadoPagoProveedor.Confirmado || suscripcionActiva;
            var pagoAprobadoPorProveedor = confirmadoPorWebhook || IsProviderApproved(code, description);
            var requiereConsulta = pago is not null && pagoAprobadoPorProveedor && !confirmadoPorWebhook;
            var debeAutoActualizar = requiereConsulta && pollAttemptNormalized < CheckoutAutoRefreshMaxAttempts;

            var model = new ResultadoCheckoutViewModel
            {
                Referencia = requestedReference,
                CodigoProveedor = code,
                DescripcionProveedor = description,
                NombrePlan = pago?.Plan?.Nombre ?? suscripcion?.Plan?.Nombre,
                EstadoPago = pago?.Estado,
                EstadoSuscripcion = suscripcion?.Estado,
                PagoAprobadoPorProveedor = pagoAprobadoPorProveedor,
                ConfirmadoPorWebhook = confirmadoPorWebhook,
                SuscripcionActiva = suscripcionActiva,
                DebeAutoActualizar = debeAutoActualizar,
                SegundosAutoActualizacion = debeAutoActualizar ? CheckoutAutoRefreshSeconds : 0,
                UrlActualizacion = requiereConsulta
                    ? BuildCheckoutRefreshUrl(orderNumber, reference, code, description, pollAttemptNormalized + 1)
                    : null
            };

            ApplyCheckoutMessaging(model, pago);

            return View(model);
        }

        private IActionResult BuildRestrictedCheckoutResult(
            string? requestedReference,
            string? code,
            string? description,
            string message)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;

            return View(
                "Exito",
                new ResultadoCheckoutViewModel
                {
                    Referencia = requestedReference,
                    CodigoProveedor = code,
                    DescripcionProveedor = description,
                    AccesoRestringido = true,
                    MensajePrincipal = message
                });
        }

        private Task<List<PagoSuscripcion>> FindMatchingPaymentsAsync(
            string? requestedReference,
            string? checkoutId)
        {
            if (string.IsNullOrWhiteSpace(requestedReference) && string.IsNullOrWhiteSpace(checkoutId))
            {
                return Task.FromResult(new List<PagoSuscripcion>());
            }

            return _context.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(p => p.Plan)
                .Where(
                    p => (!string.IsNullOrWhiteSpace(requestedReference) &&
                          (p.ReferenciaInterna == requestedReference ||
                           p.ProviderReference == requestedReference ||
                           p.ProviderCheckoutId == requestedReference)) ||
                         (!string.IsNullOrWhiteSpace(checkoutId) && p.ProviderCheckoutId == checkoutId))
                .OrderByDescending(p => p.FechaCreacionUtc)
                .Take(2)
                .ToListAsync();
        }

        private Task<Suscripcion?> FindCurrentSubscriptionAsync(Guid tenantId) =>
            _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .ThenByDescending(s => s.FechaInicio)
                .FirstOrDefaultAsync();

        private static bool IsSubscriptionActive(Suscripcion? suscripcion)
        {
            if (suscripcion is null)
            {
                return false;
            }

            if (suscripcion.Estado != EstadoSuscripcion.Activa &&
                suscripcion.Estado != EstadoSuscripcion.Trial)
            {
                return false;
            }

            return !suscripcion.FechaFin.HasValue || suscripcion.FechaFin.Value >= DateTime.UtcNow;
        }

        private static bool IsProviderApproved(string? code, string? description)
        {
            if (string.Equals(code?.Trim(), "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                return false;
            }

            return description.Contains("approved", StringComparison.OrdinalIgnoreCase) ||
                   description.Contains("aprob", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyCheckoutMessaging(
            ResultadoCheckoutViewModel model,
            PagoSuscripcion? pago)
        {
            if (pago is null)
            {
                model.MensajePrincipal = "No encontramos un pago asociado a esa referencia dentro de tu tenant.";
                model.MensajeSecundario = "Verifica que ingresaste con la cuenta que inicio el checkout antes de volver a consultar.";
                return;
            }

            if (model.SuscripcionActiva)
            {
                model.MensajePrincipal = "Pago confirmado y suscripcion activa.";
                model.MensajeSecundario = "Tu acceso ya esta habilitado para continuar dentro del sistema.";
                return;
            }

            if (model.PagoAprobadoPorProveedor)
            {
                model.MensajePrincipal = "Tilopay aprobo tu pago. Estamos activando tu suscripcion.";
                model.MensajeSecundario = model.DebeAutoActualizar
                    ? $"Esta pantalla se actualizara automaticamente en {model.SegundosAutoActualizacion} segundos."
                    : "Pulsa Actualizar estado para consultar nuevamente el resultado final.";
                return;
            }

            model.MensajePrincipal = "Tu pago fue recibido. Estamos esperando la confirmacion final del proveedor.";
            model.MensajeSecundario = "Mantendremos esta referencia ligada a tu tenant hasta completar la activacion.";
        }

        private string BuildCheckoutRefreshUrl(
            string? orderNumber,
            string? reference,
            string? code,
            string? description,
            int pollAttempt)
        {
            var basePath = Url?.Action(nameof(Exito), "Billing") ?? "/Billing/Exito";
            var queryValues = new List<KeyValuePair<string, string?>>();

            if (!string.IsNullOrWhiteSpace(orderNumber))
            {
                queryValues.Add(new KeyValuePair<string, string?>("orderNumber", orderNumber));
            }

            if (!string.IsNullOrWhiteSpace(reference))
            {
                queryValues.Add(new KeyValuePair<string, string?>("reference", reference));
            }

            if (!string.IsNullOrWhiteSpace(code))
            {
                queryValues.Add(new KeyValuePair<string, string?>("code", code));
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                queryValues.Add(new KeyValuePair<string, string?>("description", description));
            }

            queryValues.Add(new KeyValuePair<string, string?>("pollAttempt", pollAttempt.ToString()));

            return $"{basePath}{QueryString.Create(queryValues)}";
        }

        private static string? TryExtractTilopayCheckoutId(string? orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                return null;
            }

            var trimmed = orderNumber.Trim();
            if (trimmed.All(char.IsDigit))
            {
                return trimmed;
            }

            var separatorIndex = trimmed.LastIndexOf('_');
            if (separatorIndex < 0 || separatorIndex == trimmed.Length - 1)
            {
                return null;
            }

            var suffix = trimmed[(separatorIndex + 1)..];
            return suffix.All(char.IsDigit) ? suffix : null;
        }

        [HttpGet]
        public IActionResult Cancelado(string? description = null)
        {
            ViewBag.CancelMessage = string.IsNullOrWhiteSpace(description)
                ? "No completaste el proceso de pago."
                : description;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(Guid planId, CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            if (user.TenantId == Guid.Empty)
            {
                return BadRequest("El usuario autenticado no tiene tenant asociado.");
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return BadRequest("El usuario autenticado no tiene correo asociado.");
            }

            if (string.IsNullOrWhiteSpace(_tilopayOptions.WebhookAccessToken))
            {
                throw new InvalidOperationException(
                    "Tilopay requiere Tilopay:WebhookAccessToken configurado para aceptar webhooks de forma segura.");
            }

            var selectedPlan = await _publicSiteContentService.FindAvailablePlanAsync(planId, cancellationToken);

            if (selectedPlan is null)
            {
                TempData["BillingError"] = "El plan seleccionado no está disponible en este entorno.";
                return RedirectToAction(nameof(Planes));
            }

            try
            {
                EnsurePublicCallbackBaseUrl();
                var successUrl = BuildSuccessUrl();
                var cancelUrl = BuildCancelUrl();
                var webhookUrl = BuildWebhookUrl();

                _logger.LogInformation(
                    "URLs de callback construidas para checkout. TenantId {TenantId}. SuccessUrl {SuccessUrl}. CancelUrl {CancelUrl}. WebhookUrl {WebhookUrl}. PublicBaseUrl {PublicBaseUrl}",
                    user.TenantId,
                    successUrl,
                    cancelUrl,
                    SanitizeWebhookUrl(webhookUrl),
                    string.IsNullOrWhiteSpace(_paymentOptions.PublicBaseUrl) ? "<request-host>" : _paymentOptions.PublicBaseUrl);

                if (_paymentOptions.ValidatePublicCallbackReachability)
                {
                    await _publicCallbackHealthService.EnsureReachableAsync(
                        BuildPublicCallbackHealthUrl(),
                        cancellationToken);
                }

                var checkout = await _paymentService.CreateCheckoutAsync(
                    user.TenantId,
                    selectedPlan.Id,
                    string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
                    user.Email,
                    successUrl,
                    cancelUrl,
                    webhookUrl,
                    cancellationToken);

                return Redirect(checkout.RedirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error iniciando checkout Tilopay para el tenant {TenantId}.", user.TenantId);
                TempData["BillingError"] = "No fue posible iniciar el checkout con Tilopay. Revisa la configuracion y vuelve a intentarlo.";
                return RedirectToAction(nameof(Planes));
            }
        }

        private string BuildSuccessUrl() => BuildAbsoluteUrl(nameof(Exito));

        private string BuildCancelUrl() => BuildAbsoluteUrl(nameof(Cancelado));

        private string BuildPublicCallbackHealthUrl() => BuildAbsolutePath("api/health/public-callback");

        private string BuildWebhookUrl()
        {
            var queryName = Uri.EscapeDataString(_tilopayOptions.WebhookAccessTokenQueryParameter);
            var queryValue = Uri.EscapeDataString(_tilopayOptions.WebhookAccessToken);
            var webhookUrl = BuildAbsolutePath("api/webhooks/tilopay");
            var separator = webhookUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{webhookUrl}{separator}{queryName}={queryValue}";
        }

        private string BuildAbsoluteUrl(string actionName)
        {
            if (!string.IsNullOrWhiteSpace(_paymentOptions.PublicBaseUrl))
            {
                var relativePath = Url.Action(actionName, "Billing", values: null, protocol: null)
                    ?? throw new InvalidOperationException($"No fue posible construir la ruta relativa para {actionName}.");

                return BuildAbsolutePath(relativePath);
            }

            return Url.Action(actionName, "Billing", values: null, protocol: Request.Scheme)
                ?? throw new InvalidOperationException($"No fue posible construir la URL absoluta para {actionName}.");
        }

        private string BuildAbsolutePath(string relativeOrAbsolutePath)
        {
            if (!string.IsNullOrWhiteSpace(_paymentOptions.PublicBaseUrl))
            {
                var baseUri = ParsePublicBaseUri();
                return new Uri(baseUri, relativeOrAbsolutePath.TrimStart('/')).ToString();
            }

            if (Uri.TryCreate(relativeOrAbsolutePath, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            var builder = new UriBuilder(Request.Scheme, Request.Host.Host, Request.Host.Port ?? -1)
            {
                Path = relativeOrAbsolutePath.TrimStart('/')
            };

            return builder.Uri.ToString();
        }

        private void EnsurePublicCallbackBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(_paymentOptions.PublicBaseUrl))
            {
                _ = ParsePublicBaseUri();
                return;
            }

            var host = Request.Host.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException(
                    "No fue posible determinar el host público para construir las URLs de retorno y webhook.");
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(host, out var parsedIp) && IPAddress.IsLoopback(parsedIp))
            {
                throw new InvalidOperationException(
                    "Tilopay requiere una URL pública para callback y webhook. Define Payments:PublicBaseUrl antes de iniciar un checkout desde un entorno local.");
            }
        }
        private Uri ParsePublicBaseUri()
        {
            if (!Uri.TryCreate(_paymentOptions.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    "Payments:PublicBaseUrl debe ser una URL absoluta valida con esquema http o https.");
            }

            return baseUri;
        }

        private string SanitizeWebhookUrl(string webhookUrl)
        {
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
            {
                return webhookUrl;
            }

            var queryName = Uri.EscapeDataString(_tilopayOptions.WebhookAccessTokenQueryParameter);
            var builder = new UriBuilder(uri)
            {
                Query = $"{queryName}=***"
            };

            return builder.Uri.ToString();
        }
    }
}
