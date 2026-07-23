using System.Diagnostics;
using LuxuryApp.Models;
using LuxuryApp.Models.Marketing;
using LuxuryApp.Services.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IPublicSiteContentService _publicSiteContentService;

        public HomeController(
            ILogger<HomeController> logger,
            IPublicSiteContentService publicSiteContentService)
        {
            _logger = logger;
            _publicSiteContentService = publicSiteContentService;
        }

        // Convención de facto (nginx): "el cliente cerró la conexión antes de responder".
        // Coincide con ClientDisconnectMiddleware para dar una respuesta silenciosa.
        private const int ClientClosedRequest = 499;

        [AllowAnonymous]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                // Funcionario: su área es el portal limitado, no el dashboard del negocio.
                if (User.IsInRole(LuxuryApp.Services.Identity.AppRoles.Funcionario) &&
                    !User.IsInRole(LuxuryApp.Services.Identity.AppRoles.Administrador))
                {
                    return Redirect("/MiPortal");
                }

                return RedirectToAction("Index", "Dashboard");
            }

            // El visitante ya abandonó (navegación rápida, cierre o refresco): no hacemos
            // trabajo extra ni construimos la vista. No es un error de la aplicación.
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Landing pública abortada por el cliente antes de cargar los planes.");
                return new StatusCodeResult(ClientClosedRequest);
            }

            var plans = await LoadPlanCardsResilientlyAsync(cancellationToken);
            if (plans is null)
            {
                // El cliente canceló mientras se cargaban los planes. Respuesta 499 silenciosa.
                return new StatusCodeResult(ClientClosedRequest);
            }

            var model = new PublicHomeViewModel
            {
                HeroMetrics = _publicSiteContentService.GetHeroMetrics(),
                Modules = _publicSiteContentService.GetModules(),
                Plans = plans.Take(3).ToArray()
            };

            return View(model);
        }

        /// <summary>
        /// Carga las cards de planes tolerando fallos: la landing debe renderizarse siempre.
        /// Devuelve <c>null</c> únicamente cuando el propio cliente canceló el request.
        /// </summary>
        private async Task<IReadOnlyCollection<MarketingPlanCardViewModel>?> LoadPlanCardsResilientlyAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await _publicSiteContentService.GetPlanCardsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancelación del cliente (navegador). No es un error: como máximo Debug, sin 500.
                _logger.LogDebug("Carga de planes de la landing cancelada por el cliente.");
                return null;
            }
            catch (OperationCanceledException ex)
            {
                // Cancelación/timeout NO originado por el request (p. ej. límite de base de datos).
                // Se degrada con un fallback seguro: la landing se muestra sin precios dinámicos.
                _logger.LogWarning(
                    ex,
                    "Carga de planes públicos cancelada por timeout u origen ajeno al request. " +
                    "Se renderiza la landing sin precios dinámicos.");
                return Array.Empty<MarketingPlanCardViewModel>();
            }
            catch (Exception ex)
            {
                // Cualquier otro fallo (base de datos, mapeo, etc.) no debe tumbar la landing.
                _logger.LogWarning(
                    ex,
                    "No fue posible cargar los planes públicos. " +
                    "Se renderiza la landing sin la sección dinámica de precios.");
                return Array.Empty<MarketingPlanCardViewModel>();
            }
        }

        [AllowAnonymous]
        [HttpGet("/privacidad")]
        [HttpHead("/privacidad")]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpGet("/eliminacion-datos")]
        [HttpHead("/eliminacion-datos")]
        public IActionResult DataDeletion()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpGet("/Home/Privacy")]
        public IActionResult PrivacyLegacy()
        {
            return RedirectPermanent("/privacidad");
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
