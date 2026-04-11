using System.Net;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Payments;
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
        private readonly ILogger<BillingController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly SaaSPaymentService _paymentService;
        private readonly PublicCallbackHealthService _publicCallbackHealthService;
        private readonly UserManager<AppUsuario> _userManager;
        private readonly OpcionesTilopay _tilopayOptions;
        private readonly OpcionesPago _paymentOptions;

        public BillingController(
            ILogger<BillingController> logger,
            ApplicationDbContext context,
            SaaSPaymentService paymentService,
            PublicCallbackHealthService publicCallbackHealthService,
            UserManager<AppUsuario> userManager,
            IOptions<OpcionesTilopay> tilopayOptions,
            IOptions<OpcionesPago> paymentOptions)
        {
            _logger = logger;
            _context = context;
            _paymentService = paymentService;
            _publicCallbackHealthService = publicCallbackHealthService;
            _userManager = userManager;
            _tilopayOptions = tilopayOptions.Value;
            _paymentOptions = paymentOptions.Value;
        }

        public async Task<IActionResult> Planes()
        {
            var planes = await BuildAvailablePlansQuery()
                .AsNoTracking()
                .OrderBy(p => p.PrecioMensual)
                .ToListAsync();

            return View(planes);
        }

        public IActionResult SinSuscripcion()
        {
            return View();
        }

        public IActionResult PlanVencido()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Exito(
            string? orderNumber,
            string? code,
            string? description)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null || user.TenantId == Guid.Empty)
            {
                return Challenge();
            }

            var pago = string.IsNullOrWhiteSpace(orderNumber)
                ? null
                : await _context.PagosSuscripcion
                    .AsNoTracking()
                    .Include(p => p.Plan)
                    .FirstOrDefaultAsync(
                        p => p.TenantId == user.TenantId &&
                             (p.ReferenciaInterna == orderNumber || p.ProviderReference == orderNumber));

            var model = new ResultadoCheckoutViewModel
            {
                Referencia = orderNumber,
                CodigoProveedor = code,
                DescripcionProveedor = description,
                NombrePlan = pago?.Plan?.Nombre,
                EstadoPago = pago?.Estado,
                MensajePrincipal = pago is null
                    ? "No encontramos un pago asociado a esa referencia dentro de tu tenant."
                    : pago.Estado == EstadoPagoProveedor.Confirmado
                    ? "Pago confirmado y suscripcion actualizada."
                    : "Tu pago fue recibido. Estamos esperando la confirmacion final del proveedor."
            };

            return View(model);
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

            var selectedPlan = await BuildAvailablePlansQuery()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

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

        private IQueryable<Plan> BuildAvailablePlansQuery() =>
            _context.Planes.Where(plan =>
                plan.Activo &&
                (!plan.EsPlanValidacion || _paymentOptions.EnableValidationPlans));

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
