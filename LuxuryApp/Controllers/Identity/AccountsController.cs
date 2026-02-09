using LuxuryApp.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Identity
{
    public class AccountsController : Controller
    {
        private readonly UserManager<AppUsuario> _userManager;
        private readonly SignInManager<AppUsuario> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AccountsController(
            UserManager<AppUsuario> userManager,
            SignInManager<AppUsuario> signInManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
        }
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous]

        public async Task<IActionResult> Registro(string returnurl = null)
        {
      
            ViewData["ReturnUrl"] = returnurl;
            RegistroViewModel registroVM = new RegistroViewModel();
            return View(registroVM);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]

        public async Task<IActionResult> Registro(RegistroViewModel rgViewModel, string returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            returnurl = returnurl ?? Url.Content("~/");

            if (ModelState.IsValid)
            {
                var usuario = new AppUsuario
                {
                    UserName = rgViewModel.Email,
                    Email = rgViewModel.Email,
                    Name = rgViewModel.Name,
                    PhoneNumber = rgViewModel.PhoneNumber,
                    State = rgViewModel.State
                };
                var resultado = await _userManager.CreateAsync(usuario, rgViewModel.Password);

                if (resultado.Succeeded)
                {
                    //Esta linea es para asignacion del usuario que se registra al rol
                    await _userManager.AddToRoleAsync(usuario, "Registrado");

                    await _signInManager.SignInAsync(usuario, isPersistent: false);
                    //return RedirectToAction("Index", "Home");
                    return LocalRedirect(returnurl);
                }
                ValidarErrores(resultado);
            }
            return View(rgViewModel);
        }
        //Manejador de errores
        [AllowAnonymous]

        private void ValidarErrores(IdentityResult resultado)
        {
            foreach (var error in resultado.Errors)
            {
                ModelState.AddModelError(String.Empty, error.Description);
            }
        }

        //Metodo mostrar formulario de acceso
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

        public async Task<IActionResult> Acceso(AccesoViewModel accViewModel, string returnurl = null)
        {
            ViewData["ReturnUrl"] = returnurl;
            returnurl = returnurl ?? Url.Content("~/");

            if (ModelState.IsValid)
            {
                var resultado = await _signInManager.PasswordSignInAsync(accViewModel.Email, accViewModel.Password, accViewModel.RememberMe, lockoutOnFailure: true);

                if (resultado.Succeeded)
                {
                    if (!string.IsNullOrEmpty(returnurl) && Url.IsLocalUrl(returnurl))
                    {
                        return Redirect(returnurl);
                    }

                    return RedirectToAction("Index", "Dashboard");
                }
                if (resultado.IsLockedOut)
                {
                    //return RedirectToAction("Index", "Home");
                    return View("Bloqueado");
                }
                else
                {
                    ModelState.AddModelError(String.Empty, "Acceso invalido");
                    return View(accViewModel);

                }

            }
            return View(accViewModel);
        }

        //Salir o cerrar sesion de la app logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalirAplicacion()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        //Metodo para olvido de contrasena
        [HttpGet]
        [AllowAnonymous]

        public IActionResult OlvidoPassword()
        {
            return View();
        }

        [AllowAnonymous]
        public IActionResult Bloqueado()
        {
            return View();
        }

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //[AllowAnonymous]

        //public async Task<IActionResult> OlvidoPassword(OlvidoPasswordViewModel opViewModel)
        //{
        //    if (ModelState.IsValid)
        //    {
        //        var usuario = await _userManager.FindByEmailAsync(opViewModel.Email);
        //        if (usuario == null)
        //        {
        //            return RedirectToAction("ConfirmacionOlvidoPassword");
        //        }
        //        var codigo = await _userManager.GeneratePasswordResetTokenAsync(usuario);
        //        var urlRetorno = Url.Action("ResetPassword", "Cuentas", new { userId = usuario.Id, code = codigo }, protocol: HttpContext.Request.Scheme);

        //        await _emailSender.SendEmailAsync(opViewModel.Email, "Recuperar contraseña - Proyecto Identity",
        //            "Por favor recupere su contraseña dando click aquí: <a href=\"" + urlRetorno + "\">enlace</a>");

        //        return RedirectToAction("ConfirmacionOlvidoPassword");
        //    }
        //    return View(opViewModel);
        //}
        //[HttpGet]
        //[AllowAnonymous]
        //public IActionResult ConfirmacionOlvidoPassword()
        //{
        //    return View();
        //}

    }
}
