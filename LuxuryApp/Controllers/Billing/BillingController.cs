using System.Net;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Marketing;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Contracts;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Security;
using LuxuryApp.Services.WhatsApp;
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
        private readonly SuscripcionService _suscripcionService;
        private readonly IPromotionalCodeService _promotionalCodeService;
        private readonly ITenantCommercialAccessResolver _commercialAccessResolver;
        private readonly IPublicSiteContentService _publicSiteContentService;
        private readonly PublicCallbackHealthService _publicCallbackHealthService;
        private readonly UserManager<AppUsuario> _userManager;
        private readonly ITenantWhatsAppSettingsService _tenantWhatsAppSettingsService;
        private readonly ISubscriptionSummaryService _subscriptionSummaryService;
        private readonly ISubscriptionPricingCatalog _pricingCatalog;
        private readonly IPlanChangeService _planChangeService;
        private readonly IWebHostEnvironment _environment;
        private readonly OpcionesTilopay _tilopayOptions;
        private readonly OpcionesPago _paymentOptions;
        private readonly TilopayRepeatOptions _tilopayRepeatOptions;

        public BillingController(
            ILogger<BillingController> logger,
            ApplicationDbContext context,
            SaaSPaymentService paymentService,
            SuscripcionService suscripcionService,
            IPromotionalCodeService promotionalCodeService,
            ITenantCommercialAccessResolver commercialAccessResolver,
            IPublicSiteContentService publicSiteContentService,
            PublicCallbackHealthService publicCallbackHealthService,
            UserManager<AppUsuario> userManager,
            ITenantWhatsAppSettingsService tenantWhatsAppSettingsService,
            ISubscriptionSummaryService subscriptionSummaryService,
            ISubscriptionPricingCatalog pricingCatalog,
            IPlanChangeService planChangeService,
            IWebHostEnvironment environment,
            IOptions<OpcionesTilopay> tilopayOptions,
            IOptions<OpcionesPago> paymentOptions,
            IOptions<TilopayRepeatOptions> tilopayRepeatOptions)
        {
            _logger = logger;
            _context = context;
            _paymentService = paymentService;
            _suscripcionService = suscripcionService;
            _promotionalCodeService = promotionalCodeService;
            _commercialAccessResolver = commercialAccessResolver;
            _publicSiteContentService = publicSiteContentService;
            _publicCallbackHealthService = publicCallbackHealthService;
            _userManager = userManager;
            _tenantWhatsAppSettingsService = tenantWhatsAppSettingsService;
            _subscriptionSummaryService = subscriptionSummaryService;
            _pricingCatalog = pricingCatalog;
            _planChangeService = planChangeService;
            _environment = environment;
            _tilopayOptions = tilopayOptions.Value;
            _paymentOptions = paymentOptions.Value;
            _tilopayRepeatOptions = tilopayRepeatOptions.Value;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Planes(Guid? selectedPlanId = null, CancellationToken cancellationToken = default)
        {
            var basePlanCards = await _publicSiteContentService.GetPlanCardsAsync(cancellationToken);
            var whatsAppAddonCards = await _publicSiteContentService.GetWhatsAppAddonCardsAsync(cancellationToken);

            var user = await _userManager.GetUserAsync(User);
            TenantCommercialAccessResult? currentAccess = null;
            BillingSubscriptionSummaryViewModel? currentSubscription = null;
            IReadOnlyCollection<MarketingPlanCardViewModel> internalPlanCards;
            if (_environment.IsDevelopment() && _paymentOptions.EnableValidationPlans)
            {
                internalPlanCards = await _publicSiteContentService.GetInternalPlanCardsAsync(cancellationToken);
            }
            else
            {
                internalPlanCards = Array.Empty<MarketingPlanCardViewModel>();
            }

            if (user is not null && user.TenantId != Guid.Empty)
            {
                currentAccess = await _commercialAccessResolver.ResolveAsync(
                    user.TenantId,
                    user,
                    cancellationToken);

                currentSubscription = await _subscriptionSummaryService.BuildAsync(
                    user.TenantId,
                    cancellationToken);
            }

            return View(new BillingPlanesViewModel
            {
                BasePlanCards = basePlanCards,
                WhatsAppAddonCards = whatsAppAddonCards,
                InternalPlanCards = internalPlanCards,
                CurrentAccess = currentAccess,
                CurrentSubscription = currentSubscription,
                IsAuthenticated = user is not null,
                SelectedPlanId = selectedPlanId,
                Calculator = BuildCalculator(currentSubscription)
            });
        }

        /// <summary>
        /// Vista PRIVADA de suscripcion (layout privado del panel). Enfocada en lo comercial:
        /// plan, estado, renovacion, funcionarios y paquetes WhatsApp como add-on.
        /// La configuracion operativa de WhatsApp vive en el modulo /WhatsApp.
        /// </summary>
        public async Task<IActionResult> Suscripcion(Guid? selectedPlanId = null, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null || user.TenantId == Guid.Empty)
            {
                // Sin tenant no hay nada comercial que mostrar; volvemos al pricing publico.
                return RedirectToAction(nameof(Planes));
            }

            var basePlanCards = await _publicSiteContentService.GetPlanCardsAsync(cancellationToken);
            var whatsAppAddonCards = await _publicSiteContentService.GetWhatsAppAddonCardsAsync(cancellationToken);

            IReadOnlyCollection<MarketingPlanCardViewModel> internalPlanCards =
                _environment.IsDevelopment() && _paymentOptions.EnableValidationPlans
                    ? await _publicSiteContentService.GetInternalPlanCardsAsync(cancellationToken)
                    : Array.Empty<MarketingPlanCardViewModel>();

            var currentAccess = await _commercialAccessResolver.ResolveAsync(
                user.TenantId,
                user,
                cancellationToken);

            var currentSubscription = await _subscriptionSummaryService.BuildAsync(
                user.TenantId,
                cancellationToken);

            return View(new BillingPlanesViewModel
            {
                BasePlanCards = basePlanCards,
                WhatsAppAddonCards = whatsAppAddonCards,
                InternalPlanCards = internalPlanCards,
                CurrentAccess = currentAccess,
                CurrentSubscription = currentSubscription,
                IsAuthenticated = true,
                SelectedPlanId = selectedPlanId,
                Calculator = BuildCalculator(currentSubscription)
            });
        }

        /// <summary>
        /// Inicia el checkout recurrente para una combinacion (funcionarios, ciclo) de la calculadora.
        /// El backend resuelve la opcion server-side; NUNCA confia en monto/codigo enviados por el cliente.
        /// Reutiliza el pending abierto (anti doble-click) via CreateRecurringCheckoutAsync.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckoutCalculadora(int workers, string? cycle, CancellationToken cancellationToken)
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

            var billingCycle = ParseBillingCycle(cycle);

            // Resolucion autoritativa server-side. Cualquier monto/codigo del cliente se ignora.
            var resolution = _pricingCatalog.Resolve(workers, billingCycle);
            if (!resolution.IsAvailable || resolution.Option is not { } option || !option.IsPublic)
            {
                _logger.LogWarning(
                    "Checkout calculadora rechazado. TenantId {TenantId}. Workers {Workers}. Cycle {Cycle}. Reason {Reason}.",
                    user.TenantId,
                    workers,
                    billingCycle,
                    resolution.Error ?? "opcion no publica");
                TempData["BillingError"] = "La combinacion seleccionada no esta disponible. Revisa la cantidad de funcionarios y el ciclo, o contacta soporte.";
                return RedirectToAction(nameof(Suscripcion));
            }

            // Piso de funcionarios: no permitir bajar por debajo de los funcionarios activos existentes.
            // Funcionarios es tenant-scoped por el query filter del contexto del request autenticado.
            var activeFuncionarios = await _context.Funcionarios.CountAsync(f => f.Activo, cancellationToken);
            if (option.WorkerCount < activeFuncionarios)
            {
                TempData["BillingError"] = $"Tu negocio tiene {activeFuncionarios} funcionarios activos. Elegi un plan para al menos {activeFuncionarios}.";
                return RedirectToAction(nameof(Suscripcion));
            }

            // Resolver la fila Plan por Codigo (server-side). El monto a cobrar lo define el hosted link.
            var plan = await _context.Planes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Codigo == option.Code && p.Activo, cancellationToken);

            if (plan is null)
            {
                _logger.LogError(
                    "Plan {Code} no existe o no esta activo en BD para checkout calculadora. TenantId {TenantId}.",
                    option.Code,
                    user.TenantId);
                TempData["BillingError"] = "El plan seleccionado no esta disponible en este momento. Contacta soporte.";
                return RedirectToAction(nameof(Suscripcion));
            }

            // Si el tenant ya tiene una suscripcion recurrente ACTIVA con OTRO plan, esto es un
            // cambio/upgrade: registrar el intento (anti doble-cambio) y dejar trazada la
            // suscripcion proveedor anterior para alertar su cancelacion manual al confirmarse.
            var currentSubscription = await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == user.TenantId)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .FirstOrDefaultAsync(cancellationToken);

            var isPlanChange = currentSubscription is not null &&
                               currentSubscription.TilopayRecurringPlanId.HasValue &&
                               currentSubscription.TilopayRecurringPlanId.Value != option.TilopayRecurringPlanId &&
                               _suscripcionService.CanAccessApp(currentSubscription);

            if (isPlanChange)
            {
                var change = await _planChangeService.CreateOrReuseAsync(new PlanChangeRequest
                {
                    TenantId = user.TenantId,
                    FromPlanId = currentSubscription!.PlanId,
                    FromPlanCode = currentSubscription.CodigoPlan,
                    FromWorkerCount = currentSubscription.MaxFuncionarios,
                    FromTilopayRecurringPlanId = currentSubscription.TilopayRecurringPlanId,
                    FromProviderSubscriptionId = currentSubscription.ProviderSubscriptionId,
                    ToPlanId = plan.Id,
                    ToPlanCode = option.Code,
                    ToWorkerCount = option.WorkerCount,
                    ToBillingCycle = billingCycle,
                    ToTilopayRecurringPlanId = option.TilopayRecurringPlanId
                }, cancellationToken);

                if (!change.Succeeded)
                {
                    TempData["BillingError"] = change.Error ?? "No fue posible iniciar el cambio de plan.";
                    return RedirectToAction(nameof(Suscripcion));
                }
            }

            try
            {
                EnsurePublicCallbackBaseUrl();

                if (_paymentOptions.ValidatePublicCallbackReachability)
                {
                    await _publicCallbackHealthService.EnsureReachableAsync(
                        BuildPublicCallbackHealthUrl(),
                        cancellationToken);
                }

                var checkout = await _paymentService.CreateRecurringCheckoutAsync(
                    user.TenantId,
                    plan.Id,
                    string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
                    user.Email,
                    cancellationToken);

                _logger.LogInformation(
                    "Checkout calculadora iniciado. TenantId {TenantId}. Code {Code}. Workers {Workers}. Cycle {Cycle}.",
                    user.TenantId,
                    option.Code,
                    option.WorkerCount,
                    billingCycle);

                return Redirect(checkout.RedirectUrl);
            }
            catch (RecurringCheckoutBlockedException ex)
            {
                // Bloqueo de negocio (pago en revision manual): el mensaje es seguro para el usuario.
                TempData["BillingError"] = ex.Message;
                return RedirectToAction(nameof(Suscripcion));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error iniciando checkout calculadora. TenantId {TenantId}. Code {Code}.",
                    user.TenantId,
                    option.Code);
                TempData["BillingError"] = "No fue posible iniciar el checkout. Revisa la configuracion y vuelve a intentarlo.";
                return RedirectToAction(nameof(Suscripcion));
            }
        }

        private static BillingCycle ParseBillingCycle(string? cycle)
        {
            var normalized = cycle?.Trim();
            return !string.IsNullOrWhiteSpace(normalized) &&
                   (normalized.Equals("Annual", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("Anual", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals("1", StringComparison.Ordinal))
                ? BillingCycle.Annual
                : BillingCycle.Monthly;
        }

        private SubscriptionCalculatorViewModel BuildCalculator(BillingSubscriptionSummaryViewModel? summary)
        {
            var resolutions = _pricingCatalog.EnumerateAll();
            var availableByCode = resolutions
                .Where(resolution => resolution.Option is not null)
                .Select(resolution => resolution.Option!)
                .ToDictionary(option => option.Code, option => option, StringComparer.OrdinalIgnoreCase);

            var options = new List<SubscriptionCalculatorOption>();
            foreach (var resolution in resolutions)
            {
                if (resolution.Option is { } option)
                {
                    decimal annualSavings = 0m;
                    var savingsPercent = 0;

                    if (option.BillingCycle == BillingCycle.Annual)
                    {
                        var monthlyCode = PlanCodes.BuildCalculatorCode(option.WorkerCount, BillingCycle.Monthly);
                        if (monthlyCode is not null && availableByCode.TryGetValue(monthlyCode, out var monthlyOption))
                        {
                            var yearAtMonthly = monthlyOption.ChargeAmount * 12m;
                            annualSavings = yearAtMonthly - option.ChargeAmount;
                            savingsPercent = yearAtMonthly > 0
                                ? (int)Math.Round(annualSavings / yearAtMonthly * 100m, MidpointRounding.AwayFromZero)
                                : 0;
                        }
                    }

                    options.Add(new SubscriptionCalculatorOption
                    {
                        Code = option.Code,
                        Workers = option.WorkerCount,
                        Cycle = option.BillingCycle == BillingCycle.Annual ? "Annual" : "Monthly",
                        ChargeAmount = option.ChargeAmount,
                        MonthlyEquivalentAmount = option.MonthlyEquivalentAmount,
                        AnnualSavings = annualSavings,
                        SavingsPercent = savingsPercent,
                        IsAvailable = true
                    });
                }
                else
                {
                    var code = PlanCodes.BuildCalculatorCode(resolution.WorkerCount, resolution.BillingCycle);
                    options.Add(new SubscriptionCalculatorOption
                    {
                        Code = code ?? string.Empty,
                        Workers = resolution.WorkerCount,
                        Cycle = resolution.BillingCycle == BillingCycle.Annual ? "Annual" : "Monthly",
                        IsAvailable = false,
                        UnavailableReason = resolution.Error
                    });
                }
            }

            int? currentWorkers = null;
            BillingCycle? currentCycle = null;
            var currentCode = summary?.PlanCode;
            if (PlanCodes.IsCalculatorPlanCode(currentCode))
            {
                var current = _pricingCatalog.ResolveByCode(currentCode);
                if (current.Option is { } currentOption)
                {
                    currentWorkers = currentOption.WorkerCount;
                    currentCycle = currentOption.BillingCycle;
                }
            }

            var activeFuncionarios = summary?.ActiveFuncionarios ?? 0;
            var minWorkers = Math.Clamp(
                Math.Max(PlanCodes.CalculatorMinWorkers, activeFuncionarios),
                PlanCodes.CalculatorMinWorkers,
                PlanCodes.CalculatorMaxWorkers);
            var defaultWorkers = currentWorkers.HasValue
                ? Math.Clamp(currentWorkers.Value, minWorkers, PlanCodes.CalculatorMaxWorkers)
                : minWorkers;

            return new SubscriptionCalculatorViewModel
            {
                Options = options,
                Currency = "CRC",
                MinWorkers = minWorkers,
                MaxWorkers = PlanCodes.CalculatorMaxWorkers,
                DefaultWorkers = defaultWorkers,
                DefaultCycle = currentCycle ?? BillingCycle.Monthly,
                ActiveFuncionarios = activeFuncionarios,
                HasActiveSubscription = summary?.CanAccessApp == true && currentWorkers.HasValue,
                CurrentWorkers = currentWorkers,
                CurrentCycle = currentCycle,
                CurrentPlanCode = currentCode
            };
        }

        public IActionResult SinSuscripcion()
        {
            return View();
        }

        public async Task<IActionResult> PlanVencido(CancellationToken cancellationToken = default)
        {
            // Mensaje humano según el origen del vencimiento (prueba vs suscripción pagada),
            // sin exponer términos técnicos al cliente.
            var user = await _userManager.GetUserAsync(User);
            var isTrial = false;

            if (user is not null && user.TenantId != Guid.Empty)
            {
                var hadPromotionalGrant = await _context.TenantCommercialAccessGrants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(grant => grant.TenantId == user.TenantId, cancellationToken);

                var hadSubscription = await _context.Suscripciones
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(subscription => subscription.TenantId == user.TenantId, cancellationToken);

                // Si solo tuvo acceso por código promocional (prueba) y nunca una suscripción, es trial.
                isTrial = hadPromotionalGrant && !hadSubscription;
            }

            ViewData["IsTrialExpired"] = isTrial;
            ViewData["ExpiredHeading"] = isTrial
                ? "Tu prueba finalizó"
                : "Tu suscripción finalizó";
            ViewData["ExpiredMessage"] = isTrial
                ? "Tu prueba de LuxuryCloud finalizó. Renueva tu suscripción para continuar usando la plataforma."
                : "Tu suscripción finalizó. Renueva tu plan para continuar usando LuxuryCloud.";

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateWhatsAppPreferences(CancellationToken cancellationToken = default)
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

            // Leer con ContractAcceptanceBindingHelper para manejar correctamente el patrón
            // hidden(false) + checkbox(true) independientemente del orden en que el navegador los envíe.
            var sendAppointmentConfirmations = ContractAcceptanceBindingHelper.IsAccepted(
                Request.Form, "sendAppointmentConfirmations");
            var sendAppointmentReminders = ContractAcceptanceBindingHelper.IsAccepted(
                Request.Form, "sendAppointmentReminders");

            if (_environment.IsDevelopment())
            {
                _logger.LogDebug(
                    "UpdateWhatsAppPreferences form binding. FormKeys: [{FormKeys}]. RawConfirmations: [{RawConfirmations}]. RawReminders: [{RawReminders}]. ResolvedConfirmations: {ResolvedConfirmations}. ResolvedReminders: {ResolvedReminders}.",
                    string.Join(", ", Request.Form.Keys),
                    string.Join("|", ContractAcceptanceBindingHelper.GetSubmittedValues(Request, "sendAppointmentConfirmations")),
                    string.Join("|", ContractAcceptanceBindingHelper.GetSubmittedValues(Request, "sendAppointmentReminders")),
                    sendAppointmentConfirmations,
                    sendAppointmentReminders);
            }

            var addon = await _context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(current => current.Plan)
                .Where(current => current.TenantId == user.TenantId)
                .OrderByDescending(current => current.UpdatedAtUtc)
                .ThenByDescending(current => current.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (addon is null || !_suscripcionService.IsWhatsAppAddonActive(addon))
            {
                TempData["BillingError"] = "Activa un paquete de WhatsApp antes de cambiar estas preferencias.";
                return RedirectToAction(nameof(Suscripcion));
            }

            var currentSettings = await _tenantWhatsAppSettingsService.GetSettingsForTenantAsync(
                user.TenantId,
                cancellationToken);
            var automationEnabled = sendAppointmentConfirmations || sendAppointmentReminders;

            var dto = new Models.WhatsApp.TenantWhatsAppSettingsUpdateDto
            {
                IsEnabled = automationEnabled,
                SendConfirmationOnCreate = sendAppointmentConfirmations,
                SendReminderThreeHoursBefore = sendAppointmentReminders,
                DailyMessageLimit = _suscripcionService.ResolveWhatsAppDailyMessageLimit(
                    addon,
                    currentSettings.DailyMessageLimit),
                TimeZoneId = currentSettings.TimeZoneId,
                Notes = currentSettings.Notes
            };

            if (_environment.IsDevelopment())
            {
                _logger.LogDebug(
                    "UpdateWhatsAppPreferences DTO a guardar. IsEnabled: {IsEnabled}. SendConfirmationOnCreate: {SendConfirmationOnCreate}. SendReminderThreeHoursBefore: {SendReminderThreeHoursBefore}. DailyMessageLimit: {DailyMessageLimit}. TimeZoneId: {TimeZoneId}.",
                    dto.IsEnabled,
                    dto.SendConfirmationOnCreate,
                    dto.SendReminderThreeHoursBefore,
                    dto.DailyMessageLimit,
                    dto.TimeZoneId);
            }

            await _tenantWhatsAppSettingsService.UpdateSettingsAsync(
                user.TenantId,
                dto,
                user.Id,
                cancellationToken);

            TempData["BillingSuccess"] = "Preferencias de automatizacion WhatsApp actualizadas correctamente.";
            return RedirectToAction(nameof(Suscripcion));
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
                return RedirectToAction(nameof(Suscripcion));
            }

            var redemption = await _promotionalCodeService.RedeemAsync(
                accessCode,
                user.TenantId,
                user,
                cancellationToken);

            if (!redemption.Succeeded)
            {
                TempData["BillingError"] = redemption.Error ?? "No fue posible aplicar el código promocional.";
                return RedirectToAction(nameof(Suscripcion));
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
                    "Billing/Exito detecto una correlacion ambigua. UserId {UserId}. TenantId {TenantId}. RequestedReferenceSuffix {RequestedReferenceSuffix}. CheckoutIdSuffix {CheckoutIdSuffix}. Matches {Matches}.",
                    user.Id,
                    user.TenantId,
                    SensitiveDataMasker.MaskReference(requestedReference),
                    SensitiveDataMasker.MaskReference(checkoutId),
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
                    "Billing/Exito rechazo un retorno cross-tenant. UserId {UserId}. CurrentTenantId {CurrentTenantId}. PaymentTenantId {PaymentTenantId}. RequestedReferenceSuffix {RequestedReferenceSuffix}. CheckoutIdSuffix {CheckoutIdSuffix}. PaymentId {PaymentId}.",
                    user.Id,
                    user.TenantId,
                    pago.TenantId,
                    SensitiveDataMasker.MaskReference(requestedReference),
                    SensitiveDataMasker.MaskReference(checkoutId),
                    pago.Id);

                return BuildRestrictedCheckoutResult(
                    requestedReference,
                    code,
                    description,
                    "Este retorno de pago no pertenece a la cuenta autenticada. Ingresa con la cuenta que inicio el checkout.");
            }

            var suscripcion = await FindCurrentSubscriptionAsync(user.TenantId);

            var pollAttemptNormalized = Math.Max(0, pollAttempt);
            var suscripcionActiva = suscripcion is not null && _suscripcionService.CanAccessApp(suscripcion);
            var confirmadoPorWebhook = pago?.Estado == EstadoPagoProveedor.Confirmado || suscripcionActiva;
            var pagoAprobadoPorProveedor = confirmadoPorWebhook || IsProviderApproved(code, description);
            var requiereConsulta = pago is not null && pagoAprobadoPorProveedor && !confirmadoPorWebhook;
            var requiereRevisionRecurrente = pago is not null &&
                                             pago.TilopayRecurringPlanId.HasValue &&
                                             !suscripcionActiva &&
                                             pago.Estado is EstadoPagoProveedor.Pendiente or EstadoPagoProveedor.ManualReview;
            var debeAutoActualizar = requiereConsulta && pollAttemptNormalized < CheckoutAutoRefreshMaxAttempts;

            var model = new ResultadoCheckoutViewModel
            {
                Referencia = requestedReference,
                CodigoProveedor = code,
                DescripcionProveedor = description,
                NombrePlan = pago?.Plan?.Nombre ?? suscripcion?.Plan?.Nombre,
                EstadoPago = pago?.Estado,
                EstadoSuscripcion = suscripcion?.Estado,
                VigenciaHastaUtc = suscripcion?.FechaFin,
                ProximoCobroUtc = suscripcion?.FechaProximoCobroUtc,
                MaxFuncionarios = suscripcion?.MaxFuncionarios ?? suscripcion?.Plan?.MaxFuncionarios,
                PagoAprobadoPorProveedor = pagoAprobadoPorProveedor,
                ConfirmadoPorWebhook = confirmadoPorWebhook,
                SuscripcionActiva = suscripcionActiva,
                DebeAutoActualizar = debeAutoActualizar,
                SegundosAutoActualizacion = debeAutoActualizar ? CheckoutAutoRefreshSeconds : 0,
                UrlActualizacion = requiereConsulta || requiereRevisionRecurrente
                    ? BuildCheckoutRefreshUrl(orderNumber, reference, code, description, pollAttemptNormalized + 1)
                    : null
            };

            ApplyCheckoutMessaging(model, pago, requestedReference);
            ApplyRecurringHostedLinkFallbackMessaging(model, pago, user);
            ApplyCheckoutActionLinks(model);

            return View("Exito", model);
        }

        [HttpGet]
        public async Task<IActionResult> CheckoutReturn(
            string? orderNumber = null,
            string? reference = null,
            string? lc_ref = null,
            string? correlationToken = null,
            string? code = null,
            string? description = null,
            int pollAttempt = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var resolvedReference = FirstNonEmpty(reference, lc_ref, correlationToken, orderNumber);

                if (string.IsNullOrWhiteSpace(resolvedReference))
                {
                    var user = await _userManager.GetUserAsync(User);
                    if (user is null || user.TenantId == Guid.Empty)
                    {
                        return Challenge();
                    }

                    var latestPayment = await _context.PagosSuscripcion
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(payment =>
                            payment.TenantId == user.TenantId &&
                            payment.Proveedor == PaymentProviderType.Tilopay &&
                            (payment.Estado == EstadoPagoProveedor.Pendiente ||
                             payment.Estado == EstadoPagoProveedor.ManualReview ||
                             payment.Estado == EstadoPagoProveedor.Confirmado))
                        .OrderByDescending(payment => payment.FechaCreacionUtc)
                        .FirstOrDefaultAsync(cancellationToken);

                    resolvedReference = FirstNonEmpty(
                        latestPayment?.CorrelationToken,
                        latestPayment?.ProviderReference,
                        latestPayment?.ReferenciaInterna);

                    _logger.LogInformation(
                        "Billing/CheckoutReturn sin referencia explicita. TenantId {TenantId}. FallbackPaymentId {PaymentId}. ResolvedReferenceSuffix {ResolvedReferenceSuffix}.",
                        user.TenantId,
                        latestPayment?.Id,
                        SensitiveDataMasker.MaskReference(resolvedReference));
                }

                return await Exito(
                    orderNumber,
                    resolvedReference,
                    code,
                    description,
                    pollAttempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error preparando Billing/CheckoutReturn. TraceIdentifier {TraceIdentifier}. OrderNumberSuffix {OrderNumberSuffix}. ReferenceSuffix {ReferenceSuffix}. LcRefSuffix {LcRefSuffix}. CorrelationTokenSuffix {CorrelationTokenSuffix}.",
                    HttpContext.TraceIdentifier,
                    SensitiveDataMasker.MaskReference(orderNumber),
                    SensitiveDataMasker.MaskReference(reference),
                    SensitiveDataMasker.MaskReference(lc_ref),
                    SensitiveDataMasker.MaskReference(correlationToken));

                Response.StatusCode = StatusCodes.Status200OK;

                var model = new ResultadoCheckoutViewModel
                {
                    Referencia = FirstNonEmpty(reference, lc_ref, correlationToken, orderNumber),
                    CodigoProveedor = code,
                    DescripcionProveedor = description,
                    MensajePrincipal = "No pudimos confirmar automaticamente este pago.",
                    MensajeSecundario = $"Revisa el estado de tu suscripcion o intenta de nuevo en unos segundos. Si el problema continua, comparte el identificador {HttpContext.TraceIdentifier} con soporte.",
                    PrimaryActionLabel = "Ir a mi suscripcion",
                    PrimaryActionUrl = Url?.Action(nameof(Suscripcion), "Billing") ?? "/Billing/Suscripcion",
                    SecondaryActionLabel = "Ir al panel",
                    SecondaryActionUrl = Url?.Action("Index", "Dashboard") ?? "/Dashboard"
                };

                return View("Exito", model);
            }
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
            PagoSuscripcion? pago,
            string? requestedReference)
        {
            if (model.SuscripcionActiva)
            {
                model.MensajePrincipal = "Pago confirmado y suscripcion activa.";
                model.MensajeSecundario = "Tu acceso ya esta habilitado para continuar dentro del sistema.";
                return;
            }

            if (pago is null)
            {
                if (string.IsNullOrWhiteSpace(requestedReference))
                {
                    model.MensajePrincipal = "No pudimos confirmar automaticamente este pago.";
                    model.MensajeSecundario = "Si ya pagaste, revisa el estado actual de tu suscripcion o intenta actualizar en unos segundos.";
                    return;
                }

                model.MensajePrincipal = "No encontramos un pago asociado a esa referencia dentro de tu tenant.";
                model.MensajeSecundario = "Verifica que ingresaste con la cuenta que inicio el checkout antes de volver a consultar.";
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

        private void ApplyRecurringHostedLinkFallbackMessaging(
            ResultadoCheckoutViewModel model,
            PagoSuscripcion? pago,
            AppUsuario user)
        {
            if (pago is null ||
                pago.TilopayRecurringPlanId is null ||
                model.AccesoRestringido ||
                model.SuscripcionActiva)
            {
                return;
            }

            var environment = HttpContext?.RequestServices?.GetService<IWebHostEnvironment>();
            var canUseInternalReconciliation = user.IsPlatformSuperAdmin ||
                                               (environment?.IsDevelopment() == true && User.IsInRole("Administrador"));

            model.MensajePrincipal = "Tu pago puede estar aprobado en Tilopay, pero aun no recibimos confirmacion automatica.";
            model.MensajeSecundario = canUseInternalReconciliation
                ? "Si ya pagaste, usa el boton para revisar tu suscripcion o abre la conciliacion interna con auditoria."
                : "Si ya pagaste y el estado no cambia, contacta soporte para validar la transaccion aprobada en Tilopay.";
            model.PrimaryActionLabel = "Ya pague, revisar mi suscripcion";

            if (canUseInternalReconciliation)
            {
                model.SecondaryActionLabel = "Abrir conciliacion interna";
                model.SecondaryActionUrl = Url?.Action(
                    "Index",
                    "RecurringReconciliation",
                    new { paymentId = pago.Id });
            }
        }

        private void ApplyCheckoutActionLinks(ResultadoCheckoutViewModel model)
        {
            if (model.AccesoRestringido)
            {
                model.PrimaryActionLabel ??= "Ingresar con la cuenta correcta";
                model.PrimaryActionUrl ??= Url?.Action("Acceso", "Accounts") ?? "/Accounts/Acceso";
                return;
            }

            if (model.SuscripcionActiva)
            {
                model.PrimaryActionLabel ??= "Ir al panel";
                model.PrimaryActionUrl ??= Url?.Action("Index", "Dashboard") ?? "/Dashboard";
                model.SecondaryActionLabel ??= "Ir a mi suscripcion";
                model.SecondaryActionUrl ??= Url?.Action(nameof(Suscripcion), "Billing") ?? "/Billing/Suscripcion";
                return;
            }

            if (!string.IsNullOrWhiteSpace(model.UrlActualizacion))
            {
                model.PrimaryActionLabel ??= "Actualizar estado";
                model.PrimaryActionUrl ??= model.UrlActualizacion;
                model.SecondaryActionLabel ??= "Ir a mi suscripcion";
                model.SecondaryActionUrl ??= Url?.Action(nameof(Suscripcion), "Billing") ?? "/Billing/Suscripcion";
                return;
            }

            model.PrimaryActionLabel ??= "Ir a mi suscripcion";
            model.PrimaryActionUrl ??= Url?.Action(nameof(Suscripcion), "Billing") ?? "/Billing/Suscripcion";
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

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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

        [HttpGet]
        public IActionResult CheckoutCancel(string? description = null) =>
            Cancelado(description);

        [HttpGet]
        public async Task<IActionResult> ContinuarCheckout(Guid planId, CancellationToken cancellationToken)
        {
            var selectedPlan = await _publicSiteContentService.FindAvailablePlanAsync(planId, cancellationToken);
            if (selectedPlan is null)
            {
                TempData["BillingError"] = "El plan seleccionado ya no esta disponible en este entorno.";
                return RedirectToAction(nameof(Suscripcion));
            }

            if (selectedPlan.EsPlanValidacion && !_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "Intento de acceder a ContinuarCheckout con plan de validacion fuera de Development. TenantId {TenantId}. PlanId {PlanId}.",
                    (await _userManager.GetUserAsync(User))?.TenantId,
                    planId);
                TempData["BillingError"] = "El plan seleccionado no está disponible en este entorno.";
                return RedirectToAction(nameof(Suscripcion));
            }

            return View(new BillingCheckoutContinuationViewModel
            {
                PlanId = selectedPlan.Id,
                PlanName = selectedPlan.Nombre,
                PlanCode = selectedPlan.Codigo,
                AccountEmail = (await _userManager.GetUserAsync(User))?.Email,
                IsAddon = selectedPlan.LimiteMensajesMensual.HasValue
            });
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
            var selectedPlan = await _publicSiteContentService.FindAvailablePlanAsync(planId, cancellationToken);
            var repeatPlanRegistration = selectedPlan is null
                ? null
                : _tilopayRepeatOptions.FindRegistrationByCode(selectedPlan.Codigo);
            var repeatPlan = repeatPlanRegistration?.Plan;
            var isManagedRecurringPlan = selectedPlan is not null && (
                repeatPlanRegistration is not null ||
                TilopayRepeatOptions.IsManagedPlanCode(selectedPlan.Codigo) ||
                selectedPlan.LimiteMensajesMensual.HasValue ||
                selectedPlan.EsPlanValidacion);
            var isWhatsAppAddon = repeatPlan?.IsAddon == true || selectedPlan?.LimiteMensajesMensual.HasValue == true;

            if (selectedPlan is null)
            {
                TempData["BillingError"] = "El plan seleccionado no está disponible en este entorno.";
                return RedirectToAction(nameof(Suscripcion));
            }

            if (selectedPlan.EsPlanValidacion && !_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    "Intento de checkout con plan de validacion fuera de Development. TenantId {TenantId}. PlanId {PlanId}.",
                    user.TenantId,
                    planId);
                TempData["BillingError"] = "El plan seleccionado no está disponible en este entorno.";
                return RedirectToAction(nameof(Suscripcion));
            }

            var recurringValidationError = ValidateRecurringCheckoutConfiguration(
                selectedPlan,
                repeatPlanRegistration,
                isManagedRecurringPlan);
            var shouldUseRecurringCheckout = isManagedRecurringPlan && recurringValidationError is null;

            _logger.LogInformation(
                "Plan seleccionado para checkout. TenantId {TenantId}. PlanId {PlanId}. PlanCode {PlanCode}. IsWhatsAppAddon {IsWhatsAppAddon}. UsesRecurringCheckout {UsesRecurringCheckout}.",
                user.TenantId,
                selectedPlan.Id,
                selectedPlan.Codigo ?? selectedPlan.Nombre,
                isWhatsAppAddon,
                shouldUseRecurringCheckout);

            if (isWhatsAppAddon)
            {
                var currentAccess = await _commercialAccessResolver.ResolveAsync(
                    user.TenantId,
                    user,
                    cancellationToken);

                if (!currentAccess.CanAccessApp)
                {
                    TempData["BillingError"] = "Primero activa un plan base de LuxuryCloud antes de contratar un paquete de WhatsApp.";
                    return RedirectToAction(nameof(Suscripcion), new { selectedPlanId = planId });
                }
            }

            if (recurringValidationError is not null)
            {
                _logger.LogError(
                    "Checkout recurrente bloqueado por configuracion. TenantId {TenantId}. PlanId {PlanId}. Reason {Reason}",
                    user.TenantId,
                    selectedPlan.Id,
                    recurringValidationError);
                TempData["BillingError"] = "El plan seleccionado no esta disponible para checkout en este momento. Contacta soporte para revisar la configuracion de pagos.";
                return RedirectToAction(nameof(Suscripcion), new { selectedPlanId = planId });
            }

            try
            {
                if (shouldUseRecurringCheckout)
                {
                    EnsurePublicCallbackBaseUrl();

                    if (_paymentOptions.ValidatePublicCallbackReachability)
                    {
                        await _publicCallbackHealthService.EnsureReachableAsync(
                            BuildPublicCallbackHealthUrl(),
                            cancellationToken);
                    }

                    var recurringCheckout = await _paymentService.CreateRecurringCheckoutAsync(
                        user.TenantId,
                        selectedPlan.Id,
                        string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
                        user.Email,
                        cancellationToken);

                    return Redirect(recurringCheckout.RedirectUrl);
                }

                if (isWhatsAppAddon)
                {
                    TempData["BillingError"] = $"El plan {selectedPlan.Codigo ?? selectedPlan.Nombre} no tiene mapping recurrente configurado.";
                    return RedirectToAction(nameof(Suscripcion), new { selectedPlanId = planId });
                }

                if (string.IsNullOrWhiteSpace(_tilopayOptions.WebhookAccessToken))
                {
                    throw new InvalidOperationException("Falta configuracion obligatoria: Tilopay:WebhookAccessToken.");
                }

                EnsurePublicCallbackBaseUrl();
                var successUrl = BuildSuccessUrl();
                var cancelUrl = BuildCancelUrl();
                var webhookUrl = BuildWebhookUrl();

                _logger.LogInformation(
                    "URLs de callback construidas para checkout. TenantId {TenantId}. SuccessUrl {SuccessUrl}. CancelUrl {CancelUrl}. WebhookUrl {WebhookUrl}. PublicBaseUrl {PublicBaseUrl}",
                    user.TenantId,
                    SensitiveDataMasker.RedactUrl(successUrl),
                    SensitiveDataMasker.RedactUrl(cancelUrl),
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
            catch (RecurringCheckoutBlockedException ex)
            {
                // Bloqueo de negocio (pago en revision manual): el mensaje es seguro para el usuario.
                TempData["BillingError"] = ex.Message;
                return RedirectToAction(nameof(Suscripcion), new { selectedPlanId = planId });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(
                    ex,
                    "Checkout Tilopay bloqueado por validacion/configuracion. TenantId {TenantId}. PlanId {PlanId}.",
                    user.TenantId,
                    selectedPlan.Id);
                TempData["BillingError"] = "No fue posible iniciar el checkout con Tilopay. Contacta soporte para revisar la configuracion de pagos.";
                return RedirectToAction(nameof(Suscripcion), new { selectedPlanId = planId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error iniciando checkout Tilopay para el tenant {TenantId}.", user.TenantId);
                TempData["BillingError"] = "No fue posible iniciar el checkout con Tilopay. Revisa la configuracion y vuelve a intentarlo.";
                return RedirectToAction(nameof(Suscripcion), new { selectedPlanId = planId });
            }
        }

        private string? ValidateRecurringCheckoutConfiguration(
            Plan selectedPlan,
            TilopayRepeatPlanRegistration? repeatPlanRegistration,
            bool requiresRecurringCheckout)
        {
            if (!requiresRecurringCheckout)
            {
                return null;
            }

            if (repeatPlanRegistration is null)
            {
                return $"El plan {selectedPlan.Codigo ?? selectedPlan.Nombre} no tiene mapping recurrente configurado.";
            }

            if (!_tilopayRepeatOptions.Enabled)
            {
                return "Tilopay Repeat esta deshabilitado: TilopayRepeat:Enabled=false.";
            }

            if (!_tilopayRepeatOptions.UseHostedLinks)
            {
                return "Tilopay Repeat requiere hosted links: TilopayRepeat:UseHostedLinks=false.";
            }

            if (repeatPlanRegistration.Plan.IsValidation && !_tilopayRepeatOptions.EnableTestRecurringPlan)
            {
                return "El plan TEST recurrente esta deshabilitado: TilopayRepeat:EnableTestRecurringPlan=false.";
            }

            if (!repeatPlanRegistration.Plan.IsValidation &&
                !repeatPlanRegistration.Plan.IsAddon &&
                !_tilopayRepeatOptions.UseRecurringCheckoutForPublicPlans &&
                !repeatPlanRegistration.Plan.UsesRecurringCheckout)
            {
                return "Tilopay Repeat esta deshabilitado para planes publicos: TilopayRepeat:UseRecurringCheckoutForPublicPlans=false.";
            }

            if (string.IsNullOrWhiteSpace(_tilopayOptions.WebhookAccessToken))
            {
                return "Falta WebhookAccessToken: Tilopay:WebhookAccessToken.";
            }

            if (string.IsNullOrWhiteSpace(repeatPlanRegistration.Plan.CheckoutUrl))
            {
                return $"Falta CheckoutUrl para {repeatPlanRegistration.Plan.Code}: TilopayRepeat:{repeatPlanRegistration.SectionKey}:CheckoutUrl.";
            }

            return null;
        }

        private static string? ResolveRepeatSectionKey(string? planCode) =>
            TilopayRepeatOptions.ResolveSectionKey(planCode);

        private string BuildSuccessUrl() => BuildAbsoluteUrl(nameof(CheckoutReturn));

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
            return SensitiveDataMasker.RedactUrl(webhookUrl);
        }
    }
}
