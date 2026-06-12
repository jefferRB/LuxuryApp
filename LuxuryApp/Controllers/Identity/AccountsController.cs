using System.Text;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Account;
using LuxuryApp.Services.Contracts;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.Security;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Identity
{
    public class AccountsController : Controller
    {
        private readonly UserManager<AppUsuario> _userManager;
        private readonly SignInManager<AppUsuario> _signInManager;
        private readonly ApplicationDbContext _context;
        private readonly TenantProvisioningService _tenantProvisioningService;
        private readonly IContractService _contractService;
        private readonly IPublicSiteContentService _publicSiteContentService;
        private readonly TilopayRepeatOptions _tilopayRepeatOptions;
        private readonly IWebHostEnvironment _environment;
        private readonly IAccountEmailService _accountEmailService;
        private readonly ILogger<AccountsController> _logger;

        public AccountsController(
            UserManager<AppUsuario> userManager,
            SignInManager<AppUsuario> signInManager,
            ApplicationDbContext context,
            TenantProvisioningService tenantProvisioningService,
            IContractService contractService,
            IPublicSiteContentService publicSiteContentService,
            IOptions<TilopayRepeatOptions> tilopayRepeatOptions,
            IWebHostEnvironment environment,
            IAccountEmailService accountEmailService,
            ILogger<AccountsController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _tenantProvisioningService = tenantProvisioningService;
            _contractService = contractService;
            _publicSiteContentService = publicSiteContentService;
            _tilopayRepeatOptions = tilopayRepeatOptions.Value;
            _environment = environment;
            _accountEmailService = accountEmailService;
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
            var model = new RegistroViewModel
            {
                SelectedPlanId = selectedPlanId
            };

            await PopulateCurrentContractAsync(model, cancellationToken);
            return View(model);
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
            var currentContractDocumentIdBeforePopulate = model.CurrentContractDocumentId;
            var contractAcceptanceFieldName = nameof(RegistroViewModel.AcceptCurrentContract);
            var postedFormKeys = Request.HasFormContentType
                ? Request.Form.Keys.ToArray()
                : Array.Empty<string>();
            var postedCheckboxValues = ContractAcceptanceBindingHelper.GetSubmittedValues(
                Request,
                contractAcceptanceFieldName);
            var boundContractAcceptanceValue = model.AcceptCurrentContract;

            await PopulateCurrentContractAsync(model, cancellationToken);
            ModelState.Remove(nameof(RegistroViewModel.CurrentContractDocumentId));
            var normalizedContractAcceptanceValue = NormalizeContractAcceptance(model);
            var submittedContractDocumentId = model.CurrentContractDocumentId;
            var safeReturnUrl = Url.IsLocalUrl(returnurl)
                ? returnurl!
                : Url.Content("~/") ?? "/";

            if (!model.HasCurrentContract)
            {
                ModelState.AddModelError(string.Empty, "No hay un contrato vigente configurado en este momento. Contacta soporte.");
            }

            LogDevelopmentRegistrationContractTrace(
                contractAcceptanceFieldName,
                postedFormKeys,
                postedCheckboxValues,
                boundContractAcceptanceValue,
                normalizedContractAcceptanceValue,
                currentContractDocumentIdBeforePopulate,
                model.CurrentContractDocumentId,
                submittedContractDocumentId,
                model.HasCurrentContract);

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
                        AccessCode = model.AccessCode,
                        AcceptCurrentContract = model.AcceptCurrentContract,
                        SubmittedContractDocumentId = model.CurrentContractDocumentId,
                        ContractIpAddress = ContractRequestMetadataResolver.ResolveClientIp(HttpContext),
                        ContractUserAgent = ContractRequestMetadataResolver.ResolveUserAgent(HttpContext)
                    },
                    cancellationToken);

                if (!provisioning.Succeeded || provisioning.User is null)
                {
                    LogDevelopmentProvisioningFailure(provisioning.Errors);

                    foreach (var error in provisioning.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error);
                    }

                    return View(model);
                }

                await _signInManager.SignInAsync(provisioning.User, isPersistent: false);

                if (provisioning.RequiresPlanSelection)
                {
                    if (model.SelectedPlanId.HasValue && model.SelectedPlanId.Value != Guid.Empty)
                    {
                        var selectedPlan = await _publicSiteContentService.FindAvailablePlanAsync(
                            model.SelectedPlanId.Value,
                            cancellationToken);
                        var selectedRepeatPlan = selectedPlan is null
                            ? null
                            : _tilopayRepeatOptions.FindRegistrationByCode(selectedPlan.Codigo);

                        if (ShouldAutoContinueToCheckout(selectedRepeatPlan))
                        {
                            _logger.LogInformation(
                                "Registro completado con plan preseleccionado listo para checkout recurrente. TenantId {TenantId}. UserId {UserId}. PlanId {PlanId}. PlanCode {PlanCode}.",
                                provisioning.TenantId,
                                provisioning.User.Id,
                                selectedPlan!.Id,
                                selectedPlan.Codigo ?? selectedPlan.Nombre);

                            return RedirectToAction(
                                "ContinuarCheckout",
                                "Billing",
                                new { planId = selectedPlan.Id });
                        }
                    }

                    return RedirectToAction("Planes", "Billing", new { selectedPlanId = model.SelectedPlanId });
                }

                return LocalRedirect(safeReturnUrl);
            }
            catch (Exception ex)
            {
                var correlationId = HttpContext.TraceIdentifier;

                _logger.LogError(
                    ex,
                    "Error en registro SaaS. CorrelationId {CorrelationId}. MaskedEmail {MaskedEmail}.",
                    correlationId,
                    SensitiveDataMasker.MaskEmail(model.Email));

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
                ModelState.AddModelError(string.Empty, "No pudimos iniciar sesión con esos datos. Revisa tu correo y contraseña.");
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

                var contractStatus = await _contractService.GetAcceptanceStatusAsync(usuario.Id);
                if (contractStatus.BlocksApplicationAccess)
                {
                    return RedirectToAction("Reaccept", "Contract", new { returnurl = safeReturnUrl });
                }

                return LocalRedirect(safeReturnUrl);
            }

            if (result.IsLockedOut)
            {
                return View("Bloqueado");
            }

            ModelState.AddModelError(string.Empty, "No pudimos iniciar sesión con esos datos. Revisa tu correo y contraseña.");
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> OlvidoPassword(
            OlvidoPasswordViewModel model,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email.Trim());
            if (user is not null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var resetUrl = Url.Action(
                    nameof(ResetPassword),
                    "Accounts",
                    new { userId = user.Id, token = encodedToken },
                    Request.Scheme)!;

                _logger.LogInformation(
                    "OlvidoPassword solicitado. UserId: {UserId}.",
                    user.Id);

                try
                {
                    await _accountEmailService.SendPasswordResetEmailAsync(
                        user.Email!,
                        user.Name ?? user.UserName ?? "Usuario",
                        resetUrl,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error al enviar email OlvidoPassword. UserId: {UserId}.",
                        user.Id);
                }
            }

            return RedirectToAction(nameof(ConfirmacionOlvidoPassword));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ConfirmacionOlvidoPassword() => View();

        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResetPassword(string? userId, string? token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                return RedirectToAction(nameof(Acceso));
            }

            return View(new ResetPasswordViewModel { UserId = userId, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user is null)
            {
                TempData["Mensaje"] = "Contraseña actualizada. Ingresa con tu nueva contraseña.";
                return RedirectToAction(nameof(Acceso));
            }

            var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
            var result = await _userManager.ResetPasswordAsync(user, decodedToken, model.Password);

            if (result.Succeeded)
            {
                TempData["Mensaje"] = "Contraseña actualizada correctamente. Ingresa con tu nueva contraseña.";
                return RedirectToAction(nameof(Acceso));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Cuenta()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return RedirectToAction(nameof(Acceso));
            }

            return View(new CuentaViewModel
            {
                Email = user.Email ?? string.Empty,
                Name = user.Name ?? string.Empty,
                PhoneNumber = user.PhoneNumber
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cuenta(CuentaViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return RedirectToAction(nameof(Acceso));
            }

            if (!ModelState.IsValid)
            {
                return View(new CuentaViewModel
                {
                    Email = user.Email ?? string.Empty,
                    Name = model.Name,
                    PhoneNumber = model.PhoneNumber
                });
            }

            user.Name = model.Name.Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber)
                ? null
                : model.PhoneNumber.Trim();

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                TempData["Mensaje"] = "Datos actualizados correctamente.";
            }
            else
            {
                TempData["Error"] = "No se pudieron guardar los cambios. Intenta de nuevo.";
            }

            return RedirectToAction(nameof(Cuenta));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EnviarEnlaceCambioPassword(CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return RedirectToAction(nameof(Acceso));
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var resetUrl = Url.Action(
                nameof(ResetPassword),
                "Accounts",
                new { userId = user.Id, token = encodedToken },
                Request.Scheme)!;

            _logger.LogInformation(
                "Cambio de contraseña solicitado desde Cuenta. UserId: {UserId}.",
                user.Id);

            try
            {
                await _accountEmailService.SendPasswordResetEmailAsync(
                    user.Email!,
                    user.Name ?? user.UserName ?? "Usuario",
                    resetUrl,
                    cancellationToken);
                TempData["Mensaje"] = "Te enviamos un enlace para cambiar tu contraseña. Revisa tu correo.";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al enviar email de cambio de contraseña. UserId: {UserId}.",
                    user.Id);
                TempData["Error"] = "No pudimos enviar el correo en este momento. Inténtalo nuevamente más tarde.";
            }

            return RedirectToAction(nameof(Cuenta));
        }

        [AllowAnonymous]
        public IActionResult Bloqueado() => View();

        private async Task PopulateCurrentContractAsync(
            RegistroViewModel model,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(model);

            var activeContract = await _contractService.GetActiveContractAsync(cancellationToken);
            if (activeContract is null)
            {
                model.CurrentContractDocumentId = null;
                model.CurrentContractTitle = string.Empty;
                model.CurrentContractVersion = string.Empty;
                model.CurrentContractEffectiveFromUtc = null;
                return;
            }

            model.CurrentContractDocumentId = activeContract.Id;
            model.CurrentContractTitle = activeContract.Title;
            model.CurrentContractVersion = activeContract.VersionNumber;
            model.CurrentContractEffectiveFromUtc = activeContract.EffectiveFromUtc;
        }

        private bool NormalizeContractAcceptance(RegistroViewModel model)
        {
            var fieldName = nameof(RegistroViewModel.AcceptCurrentContract);
            model.AcceptCurrentContract = Request.HasFormContentType
                ? ContractAcceptanceBindingHelper.IsAccepted(Request.Form, fieldName)
                : model.AcceptCurrentContract;

            ModelState.Remove(fieldName);

            if (!model.AcceptCurrentContract)
            {
                ModelState.AddModelError(
                    fieldName,
                    "Debes aceptar el contrato para crear tu cuenta.");
            }

            return model.AcceptCurrentContract;
        }

        private void LogDevelopmentRegistrationContractTrace(
            string fieldName,
            IReadOnlyCollection<string> postedFormKeys,
            IReadOnlyCollection<string> postedCheckboxValues,
            bool boundContractAcceptanceValue,
            bool normalizedContractAcceptanceValue,
            Guid? currentContractDocumentIdBeforePopulate,
            Guid? currentContractDocumentIdAfterPopulate,
            Guid? submittedContractDocumentId,
            bool hasCurrentContract)
        {
            if (!_environment.IsDevelopment())
            {
                return;
            }

            var contractRelatedKeys = postedFormKeys
                .Where(key => key.Contains("contract", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var modelStateErrors = ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error => $"{entry.Key}: {error.ErrorMessage}"))
                .ToArray();

            _logger.LogInformation(
                "Registro POST contract trace. TraceIdentifier {TraceIdentifier}. FormKeys {FormKeys}. ContractKeys {ContractKeys}. CheckboxField {CheckboxField}. RawCheckboxValues {RawCheckboxValues}. BoundViewModelValue {BoundViewModelValue}. NormalizedAcceptanceValue {NormalizedAcceptanceValue}. CurrentContractDocumentIdBeforePopulate {CurrentContractDocumentIdBeforePopulate}. CurrentContractDocumentIdAfterPopulate {CurrentContractDocumentIdAfterPopulate}. SubmittedContractDocumentIdFinal {SubmittedContractDocumentIdFinal}. HasCurrentContract {HasCurrentContract}. ModelStateIsValidBeforeRegister {ModelStateIsValidBeforeRegister}. ModelStateErrorsBeforeRegister {ModelStateErrorsBeforeRegister}.",
                HttpContext.TraceIdentifier,
                postedFormKeys,
                contractRelatedKeys,
                fieldName,
                postedCheckboxValues,
                boundContractAcceptanceValue,
                normalizedContractAcceptanceValue,
                currentContractDocumentIdBeforePopulate,
                currentContractDocumentIdAfterPopulate,
                submittedContractDocumentId,
                hasCurrentContract,
                ModelState.IsValid,
                modelStateErrors);
        }

        private void LogDevelopmentProvisioningFailure(IReadOnlyCollection<string> errors)
        {
            if (!_environment.IsDevelopment())
            {
                return;
            }

            _logger.LogInformation(
                "Registro provisioning returned failure. TraceIdentifier {TraceIdentifier}. Errors {Errors}.",
                HttpContext.TraceIdentifier,
                errors);
        }

        private bool ShouldAutoContinueToCheckout(TilopayRepeatPlanRegistration? repeatPlan)
        {
            if (repeatPlan is null || !_tilopayRepeatOptions.Enabled)
            {
                return false;
            }

            if (repeatPlan.Plan.IsAddon)
            {
                return false;
            }

            if (repeatPlan.Plan.IsValidation)
            {
                return _tilopayRepeatOptions.EnableTestRecurringPlan;
            }

            return _tilopayRepeatOptions.UseRecurringCheckoutForPublicPlans;
        }
    }
}
