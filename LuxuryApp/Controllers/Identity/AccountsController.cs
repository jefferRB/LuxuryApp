using LuxuryApp.Models.Identity;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Identity
{
    public class AccountsController : Controller
    {
        private readonly UserManager<AppUsuario> _userManager;
        private readonly SignInManager<AppUsuario> _signInManager;
        private readonly ApplicationDbContext _context;
        private readonly TenantProvisioningService _tenantProvisioningService;
        private readonly IPublicSiteContentService _publicSiteContentService;
        private readonly ILogger<AccountsController> _logger;

        public AccountsController(
            UserManager<AppUsuario> userManager,
            SignInManager<AppUsuario> signInManager,
            ApplicationDbContext context,
            TenantProvisioningService tenantProvisioningService,
            IPublicSiteContentService publicSiteContentService,
            ILogger<AccountsController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _tenantProvisioningService = tenantProvisioningService;
            _publicSiteContentService = publicSiteContentService;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Index() => View();

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Registro(
            string? returnurl = null,
            Guid? selectedPlanId = null,
            CancellationToken cancellationToken = default)
        {
            ViewData["ReturnUrl"] = returnurl;
            ViewData["SelectedPlanName"] = await _publicSiteContentService.GetPlanNameAsync(selectedPlanId, cancellationToken);

            return View(new RegistroViewModel
            {
                SelectedPlanId = selectedPlanId
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Registro(
            RegistroViewModel model,
            string? returnurl = null,
            CancellationToken cancellationToken = default)
        {
            ViewData["ReturnUrl"] = returnurl;
            ViewData["SelectedPlanName"] = await _publicSiteContentService.GetPlanNameAsync(model.SelectedPlanId, cancellationToken);
            var safeReturnUrl = Url.IsLocalUrl(returnurl)
                ? returnurl!
                : Url.Content("~/") ?? "/";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var provisioning = await _tenantProvisioningService.RegisterAsync(
                    new TenantRegistrationRequest
                    {
                        Email = model.Email,
                        Password = model.Password,
                        Name = model.Name,
                        PhoneNumber = model.PhoneNumber,
                        AccessCode = model.AccessCode
                    },
                    cancellationToken);

                if (!provisioning.Succeeded || provisioning.User is null)
                {
                    foreach (var error in provisioning.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error);
                    }

                    return View(model);
                }

                await _signInManager.SignInAsync(provisioning.User, isPersistent: false);

                if (provisioning.RequiresPlanSelection)
                {
                    return RedirectToAction("Planes", "Billing", new { selectedPlanId = model.SelectedPlanId });
                }

                return LocalRedirect(safeReturnUrl);
            }
            catch (Exception ex)
            {
                var correlationId = HttpContext.TraceIdentifier;

                _logger.LogError(
                    ex,
                    "Error en registro SaaS. CorrelationId {CorrelationId}. Email {Email}.",
                    correlationId,
                    model.Email);

                ModelState.AddModelError(
                    string.Empty,
                    $"No fue posible crear la cuenta. Si el problema continúa, comparte este código con soporte: {correlationId}");

                return View(model);
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Acceso(string? returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Acceso(AccesoViewModel model, string? returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            var safeReturnUrl = Url.IsLocalUrl(returnurl)
                ? returnurl!
                : Url.Content("~/") ?? "/";

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var usuario = await _userManager.FindByEmailAsync(model.Email.Trim());

            if (usuario == null)
            {
                await Task.Delay(500);
                ModelState.AddModelError(string.Empty, "Acceso inválido");
                return View(model);
            }

            var result = await _signInManager.CheckPasswordSignInAsync(
                usuario,
                model.Password,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                if (!usuario.State)
                {
                    _logger.LogWarning(
                        "Intento de acceso con usuario deshabilitado. UserId {UserId}. TenantId {TenantId}.",
                        usuario.Id,
                        usuario.TenantId);

                    await Task.Delay(250);
                    ModelState.AddModelError(string.Empty, "Tu cuenta no estÃ¡ disponible en este momento.");
                    return View(model);
                }

                var tenantDisponible = usuario.TenantId != Guid.Empty &&
                    await _context.Tenants
                        .AsNoTracking()
                        .AnyAsync(t =>
                            t.Id == usuario.TenantId &&
                            (usuario.IsPlatformSuperAdmin || t.Activo));

                if (!tenantDisponible)
                {
                    _logger.LogError(
                        "Intento de acceso con usuario sin tenant válido. UserId {UserId}. TenantId {TenantId}.",
                        usuario.Id,
                        usuario.TenantId);

                    await Task.Delay(250);
                    ModelState.AddModelError(string.Empty, "Tu cuenta no está disponible en este momento.");
                    return View(model);
                }

                await _signInManager.SignInAsync(usuario, model.RememberMe);
                return LocalRedirect(safeReturnUrl);
            }

            if (result.IsLockedOut)
            {
                return View("Bloqueado");
            }

            ModelState.AddModelError(string.Empty, "Acceso inválido");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalirAplicacion()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult OlvidoPassword() => View();

        [AllowAnonymous]
        public IActionResult Bloqueado() => View();
    }
}
