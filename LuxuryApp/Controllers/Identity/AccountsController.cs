using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
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
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountsController> _logger;

        public AccountsController(
            UserManager<AppUsuario> userManager,
            SignInManager<AppUsuario> signInManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            ILogger<AccountsController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Index() => View();

        // =========================
        // REGISTRO
        // =========================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Registro(string returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            return View(new RegistroViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Registro(RegistroViewModel model, string returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            returnurl = Url.IsLocalUrl(returnurl) ? returnurl : Url.Content("~/");

            if (!ModelState.IsValid)
                return View(model);

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 🔹 Validar email duplicado GLOBAL (seguridad SaaS)
                var existingUser = await _userManager.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Email == model.Email);

                if (existingUser != null)
                {
                    ModelState.AddModelError("", "El correo ya está registrado");
                    return View(model);
                }

                // 1. Crear Tenant
                var tenant = new Tenant
                {
                    Nombre = $"{model.Name} Business"
                };

                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync();

                // 2. Obtener Plan FREE
                var planFree = await _context.Planes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Nombre == "Free" && p.Activo);

                if (planFree == null)
                    throw new Exception("Plan Free no configurado");

                // 3. Crear Suscripción
                var suscripcion = new Suscripcion
                {
                    TenantId = tenant.Id,
                    PlanId = planFree.Id,
                    Estado = EstadoSuscripcion.Trial,
                    FechaInicio = DateTime.UtcNow,
                    FechaTrialFin = DateTime.UtcNow.AddDays(14)
                };

                _context.Suscripciones.Add(suscripcion);
                await _context.SaveChangesAsync();

                // 4. Crear Usuario
                var usuario = new AppUsuario
                {
                    UserName = model.Email,
                    Email = model.Email,
                    Name = model.Name,
                    PhoneNumber = model.PhoneNumber,
                    State = model.State,
                    TenantId = tenant.Id
                };

                var result = await _userManager.CreateAsync(usuario, model.Password);

                if (!result.Succeeded)
                {
                    ValidarErrores(result);
                    await transaction.RollbackAsync();
                    return View(model);
                }

                // 5. Rol
                if (!await _roleManager.RoleExistsAsync("Registrado"))
                    throw new Exception("Rol no existe");

                await _userManager.AddToRoleAsync(usuario, "Registrado");

                await transaction.CommitAsync();

                // 🔹 Login después de commit
                await _signInManager.SignInAsync(usuario, isPersistent: false);

                return LocalRedirect(returnurl);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                _logger.LogError(ex, "Error en registro SaaS");

                ModelState.AddModelError("", "Error interno. Intente nuevamente.");
                return View(model);
            }
        }
        // LOGIN
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Acceso(string returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Acceso(AccesoViewModel model, string returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            returnurl = Url.IsLocalUrl(returnurl) ? returnurl : Url.Content("~/");

            if (!ModelState.IsValid)
                return View(model);

            // LOGIN MULTI-TENANT SEGURO
            var usuario = await _userManager.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            // Anti-enumeración
            if (usuario == null)
            {
                await Task.Delay(500);
                ModelState.AddModelError("", "Acceso inválido");
                return View(model);
            }

            var result = await _signInManager.CheckPasswordSignInAsync(
                usuario,
                model.Password,
                lockoutOnFailure: true
            );

            if (result.Succeeded)
            {
                await _signInManager.SignInAsync(usuario, model.RememberMe);

                return LocalRedirect(returnurl);
            }

            if (result.IsLockedOut)
                return View("Bloqueado");

            ModelState.AddModelError("", "Acceso inválido");
            return View(model);
        }
        // LOGOUT
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalirAplicacion()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }
        // HELPERS
        private void ValidarErrores(IdentityResult resultado)
        {
            foreach (var error in resultado.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult OlvidoPassword() => View();

        [AllowAnonymous]
        public IActionResult Bloqueado() => View();
    }
}