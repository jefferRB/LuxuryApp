using System.Security.Claims;
using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Platform
{
    [Authorize(Policy = PlatformAuthorizationPolicies.PlatformSuperAdmin)]
    public class PlatformController : Controller
    {
        private const string PromotionalCodeFormPrefix = nameof(PlatformPromotionalCodesPageViewModel.CreateForm);
        private readonly ApplicationDbContext _context;
        private readonly ITenantCommercialAccessResolver _commercialAccessResolver;
        private readonly ITenantCommercialAccessCache _accessCache;
        private readonly TenantExecutionService _tenantExecutionService;
        private readonly IMetaWhatsAppClient _metaWhatsAppClient;

        public PlatformController(
            ApplicationDbContext context,
            ITenantCommercialAccessResolver commercialAccessResolver,
            ITenantCommercialAccessCache accessCache,
            TenantExecutionService tenantExecutionService,
            IMetaWhatsAppClient metaWhatsAppClient)
        {
            _context = context;
            _commercialAccessResolver = commercialAccessResolver;
            _accessCache = accessCache;
            _tenantExecutionService = tenantExecutionService;
            _metaWhatsAppClient = metaWhatsAppClient;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var plans = await _context.Planes
                .AsNoTracking()
                .Where(plan => plan.Activo)
                .OrderBy(plan => plan.PrecioMensual)
                .ToListAsync(cancellationToken);

            var tenants = await _context.Tenants
                .AsNoTracking()
                .Include(tenant => tenant.ForcedPlan)
                .OrderBy(tenant => tenant.Nombre)
                .ToListAsync(cancellationToken);

            var tenantRows = new List<PlatformTenantRowViewModel>(tenants.Count);
            foreach (var tenant in tenants)
            {
                var ownerEmail = await _context.Users
                    .AsNoTracking()
                    .Where(user => user.TenantId == tenant.Id)
                    .OrderBy(user => user.Email)
                    .Select(user => user.Email)
                    .FirstOrDefaultAsync(cancellationToken);

                var access = await _commercialAccessResolver.ResolveAsync(tenant.Id, cancellationToken: cancellationToken);
                var whatsApp = await GetTenantWhatsAppSummaryAsync(tenant.Id, cancellationToken);
                tenantRows.Add(new PlatformTenantRowViewModel
                {
                    TenantId = tenant.Id,
                    TenantName = tenant.Nombre,
                    TenantActive = tenant.Activo,
                    CommercialAccessMode = tenant.CommercialAccessMode,
                    ForcedPlanId = tenant.ForcedPlanId,
                    ForcedPlanName = tenant.ForcedPlan?.Nombre,
                    OwnerEmail = ownerEmail,
                    CommercialNotes = tenant.CommercialNotes,
                    CanAccessApp = access.CanAccessApp,
                    EffectivePlanName = access.EffectivePlanName,
                    Reason = access.Reason,
                    WhatsAppEnabled = whatsApp.IsEnabled,
                    SendWhatsAppConfirmationOnCreate = whatsApp.SendConfirmationOnCreate,
                    SendWhatsAppReminderThreeHoursBefore = whatsApp.SendReminderThreeHoursBefore,
                    WhatsAppDailyMessageLimit = whatsApp.DailyMessageLimit,
                    WhatsAppTodayUsage = whatsApp.TodayUsage,
                    WhatsAppTimeZoneId = whatsApp.TimeZoneId,
                    WhatsAppNotes = whatsApp.Notes,
                    WhatsAppLastErrorCode = whatsApp.LastErrorCode,
                    WhatsAppLastErrorMessage = whatsApp.LastErrorMessage,
                    WhatsAppLastErrorAtUtc = whatsApp.LastErrorAtUtc,
                    WhatsAppAddonCode = whatsApp.AddonCode,
                    WhatsAppAddonIsManual = whatsApp.AddonIsManual,
                    WhatsAppAddonFechaFin = whatsApp.AddonFechaFin,
                    WhatsAppAddonMonthlyLimit = whatsApp.AddonMonthlyLimit
                });
            }

            var totalActiveSubscriptions = await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    subscription => subscription.Estado == EstadoSuscripcion.Activa || subscription.Estado == EstadoSuscripcion.Trial,
                    cancellationToken);

            var recentUsers = await _context.Users
                .AsNoTracking()
                .Join(
                    _context.Tenants.AsNoTracking(),
                    user => user.TenantId,
                    tenant => tenant.Id,
                    (user, tenant) => new PlatformRecentUserViewModel
                    {
                        Email = user.Email ?? user.UserName ?? string.Empty,
                        Name = user.Name,
                        TenantName = tenant.Nombre,
                        IsPlatformSuperAdmin = user.IsPlatformSuperAdmin
                    })
                .OrderByDescending(user => user.IsPlatformSuperAdmin)
                .ThenBy(user => user.Email)
                .Take(10)
                .ToListAsync(cancellationToken);

            var recentPayments = await _context.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(payment => payment.Plan)
                .Join(
                    _context.Tenants.AsNoTracking(),
                    payment => payment.TenantId,
                    tenant => tenant.Id,
                    (payment, tenant) => new PlatformRecentPaymentViewModel
                    {
                        TenantName = tenant.Nombre,
                        PlanName = payment.Plan != null ? payment.Plan.Nombre : "Sin plan",
                        Amount = payment.Monto,
                        Currency = payment.Moneda,
                        Status = payment.Estado,
                        CreatedUtc = payment.FechaCreacionUtc
                    })
                .OrderByDescending(payment => payment.CreatedUtc)
                .Take(10)
                .ToListAsync(cancellationToken);

            var latestSubscriptions = await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(subscription => subscription.Plan)
                .Where(subscription =>
                    subscription.Proveedor == PaymentProviderType.Tilopay ||
                    subscription.TilopayRecurringPlanId.HasValue)
                .OrderByDescending(subscription => subscription.FechaUltimaActualizacionUtc ?? subscription.FechaInicio)
                .ThenByDescending(subscription => subscription.FechaInicio)
                .ToListAsync(cancellationToken);

            var latestSubscriptionByTenant = latestSubscriptions
                .GroupBy(subscription => subscription.TenantId)
                .ToDictionary(group => group.Key, group => group.First());

            var ownerByTenantId = tenantRows
                .ToDictionary(row => row.TenantId, row => row.OwnerEmail, EqualityComparer<Guid>.Default);

            var pendingRecurringCheckouts = await _context.PagosSuscripcion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(payment => payment.Plan)
                .Where(payment =>
                    payment.Proveedor == PaymentProviderType.Tilopay &&
                    (payment.Estado == EstadoPagoProveedor.Pendiente ||
                     payment.Estado == EstadoPagoProveedor.ManualReview))
                .OrderByDescending(payment => payment.FechaCreacionUtc)
                .Take(25)
                .ToListAsync(cancellationToken);

            var pendingRecurringCheckoutRows = pendingRecurringCheckouts
                .Select(payment =>
                {
                    latestSubscriptionByTenant.TryGetValue(payment.TenantId, out var currentSubscription);
                    ownerByTenantId.TryGetValue(payment.TenantId, out var ownerEmail);
                    return new PlatformBillingPendingCheckoutViewModel
                    {
                        PaymentId = payment.Id,
                        TenantName = tenants.FirstOrDefault(tenant => tenant.Id == payment.TenantId)?.Nombre ?? payment.TenantId.ToString(),
                        OwnerEmail = ownerEmail,
                        PlanName = payment.Plan?.Nombre ?? "Sin plan",
                        PlanCode = payment.Plan?.Codigo,
                        CheckoutKind = ResolveCheckoutKind(payment, currentSubscription),
                        Amount = payment.Monto,
                        Currency = payment.Moneda,
                        Status = payment.Estado,
                        CreatedUtc = payment.FechaCreacionUtc,
                        CorrelationToken = payment.CorrelationToken ?? payment.ProviderReference,
                        ProviderSubscriberId = payment.ProviderSubscriberId,
                        ProviderTransactionId = payment.ProviderTransactionId,
                        ProviderResultMessage = payment.ProviderResultMessage
                    };
                })
                .ToArray();

            var recentBillingEvents = await _context.EventosPago
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(evento => evento.Proveedor == PaymentProviderType.Tilopay)
                .OrderByDescending(evento => evento.FechaRecepcionUtc)
                .Take(25)
                .ToListAsync(cancellationToken);

            var recentBillingEventRows = recentBillingEvents
                .Select(evento => new PlatformBillingEventViewModel
                {
                    TenantName = tenants.FirstOrDefault(tenant => tenant.Id == evento.TenantId)?.Nombre ?? (evento.TenantId?.ToString() ?? "Sin correlacion"),
                    PlanName = plans.FirstOrDefault(plan => plan.Id == evento.PlanId)?.Nombre,
                    EventType = evento.Tipo,
                    ProcessingStatus = evento.EstadoProcesamiento,
                    ReceivedUtc = evento.FechaRecepcionUtc,
                    Amount = evento.Monto,
                    Currency = evento.Moneda,
                    CorrelationId = evento.CorrelationId,
                    TilopayRecurringPlanId = evento.TilopayRecurringPlanId,
                    ProviderTransactionId = evento.ProviderTransactionId,
                    ProviderSubscriberId = evento.ProviderSubscriberId,
                    Error = evento.Error
                })
                .ToArray();

            var activeRecurringSubscriptionRows = latestSubscriptions
                .Where(subscription => subscription.Estado is EstadoSuscripcion.Activa or EstadoSuscripcion.Trial or EstadoSuscripcion.Morosa)
                .Take(25)
                .Select(subscription => new PlatformSubscriptionStatusViewModel
                {
                    TenantName = tenants.FirstOrDefault(tenant => tenant.Id == subscription.TenantId)?.Nombre ?? subscription.TenantId.ToString(),
                    PlanName = subscription.Plan?.Nombre ?? "Sin plan",
                    PlanCode = subscription.CodigoPlan ?? subscription.Plan?.Codigo,
                    Status = subscription.Estado,
                    CurrentPeriodEndUtc = subscription.FechaFin,
                    NextBillingDateUtc = subscription.FechaProximoCobroUtc,
                    MaxFuncionarios = subscription.MaxFuncionarios ?? subscription.Plan?.MaxFuncionarios,
                    ProviderSubscriberId = subscription.ProviderSubscriptionId,
                    ProviderTransactionId = subscription.ProviderTransactionId
                })
                .ToArray();

            var activeRecurringAddons = await _context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(addon => addon.Plan)
                .Where(addon =>
                    addon.Estado == EstadoSuscripcion.Activa ||
                    addon.Estado == EstadoSuscripcion.Morosa)
                .OrderByDescending(addon => addon.UpdatedAtUtc)
                .Take(25)
                .ToListAsync(cancellationToken);

            var activeRecurringAddonRows = activeRecurringAddons
                .Select(addon => new PlatformSubscriptionStatusViewModel
                {
                    TenantName = tenants.FirstOrDefault(tenant => tenant.Id == addon.TenantId)?.Nombre ?? addon.TenantId.ToString(),
                    PlanName = addon.Plan?.Nombre ?? "Sin add-on",
                    PlanCode = addon.AddonCode ?? addon.Plan?.Codigo,
                    Status = addon.Estado,
                    CurrentPeriodEndUtc = addon.FechaFin,
                    NextBillingDateUtc = addon.FechaProximoCobroUtc,
                    MonthlyMessageLimit = addon.MonthlyMessageLimit > 0 ? addon.MonthlyMessageLimit : addon.Plan?.LimiteMensajesMensual,
                    ProviderSubscriberId = addon.ProviderSubscriptionId,
                    ProviderTransactionId = addon.ProviderTransactionId
                })
                .ToArray();

            var model = new PlatformDashboardViewModel
            {
                TotalTenants = tenants.Count,
                TotalUsers = await _context.Users.CountAsync(cancellationToken),
                TotalActiveSubscriptions = totalActiveSubscriptions,
                TotalPromotionalCodes = await _context.PromotionalCodes.CountAsync(cancellationToken),
                AvailablePlans = plans,
                Tenants = tenantRows,
                RecentUsers = recentUsers,
                RecentPayments = recentPayments,
                PendingRecurringCheckouts = pendingRecurringCheckoutRows,
                RecentBillingEvents = recentBillingEventRows,
                ActiveRecurringSubscriptions = activeRecurringSubscriptionRows,
                ActiveRecurringAddons = activeRecurringAddonRows
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTenantWhatsAppSettings(
            Guid tenantId,
            [Bind(Prefix = "whatsappSettings")] TenantWhatsAppSettingsUpdateDto whatsappSettings,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                TempData["PlatformError"] = "Revisa la configuracion de WhatsApp. El limite no puede ser negativo y la zona horaria es obligatoria.";
                return RedirectToAction(nameof(Index));
            }

            var tenantExists = await _context.Tenants
                .AsNoTracking()
                .AnyAsync(tenant => tenant.Id == tenantId, cancellationToken);

            if (!tenantExists)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(whatsappSettings.AddonCode))
            {
                if (string.IsNullOrWhiteSpace(whatsappSettings.ManualAssignmentObservation))
                {
                    TempData["PlatformError"] = "La observacion es obligatoria al cambiar el paquete WhatsApp.";
                    return RedirectToAction(nameof(Index));
                }

                await _tenantExecutionService.RunForTenantAsync(
                    tenantId,
                    async (serviceProvider, scopedTenantId, ct) =>
                    {
                        var svc = serviceProvider.GetRequiredService<SuscripcionService>();
                        await svc.AssignManualWhatsAppAddonAsync(
                            scopedTenantId,
                            whatsappSettings.AddonCode,
                            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "platform",
                            whatsappSettings.ManualAssignmentObservation,
                            whatsappSettings.SendConfirmationOnCreate,
                            whatsappSettings.SendReminderThreeHoursBefore,
                            ct);
                    },
                    cancellationToken);
            }
            else
            {
                await _tenantExecutionService.RunForTenantAsync(
                    tenantId,
                    async (serviceProvider, scopedTenantId, ct) =>
                    {
                        var settingsService = serviceProvider.GetRequiredService<ITenantWhatsAppSettingsService>();
                        await settingsService.UpdateSettingsAsync(
                            scopedTenantId,
                            whatsappSettings,
                            User.FindFirstValue(ClaimTypes.NameIdentifier),
                            ct);
                    },
                    cancellationToken);
            }

            TempData["PlatformSuccess"] = "Configuracion WhatsApp del tenant actualizada.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestMetaWhatsAppConfiguration(
            Guid? tenantId,
            CancellationToken cancellationToken)
        {
            if (tenantId.HasValue)
            {
                var tenantExists = await _context.Tenants
                    .AsNoTracking()
                    .AnyAsync(tenant => tenant.Id == tenantId.Value, cancellationToken);

                if (!tenantExists)
                {
                    return NotFound();
                }
            }

            var diagnostic = await _metaWhatsAppClient.TestConfigurationAsync(cancellationToken);

            TenantWhatsAppSettingsSnapshot? tenantSettings = null;
            if (tenantId.HasValue)
            {
                await _tenantExecutionService.RunForTenantAsync(
                    tenantId.Value,
                    async (serviceProvider, scopedTenantId, ct) =>
                    {
                        var settingsService = serviceProvider.GetRequiredService<ITenantWhatsAppSettingsService>();
                        tenantSettings = await settingsService.GetSettingsForTenantAsync(scopedTenantId, ct);
                    },
                    cancellationToken);
            }

            return Json(new
            {
                success = diagnostic.Success,
                tenantId,
                configuration = diagnostic.Configuration,
                phoneNumberProbe = diagnostic.PhoneNumberProbe,
                wabaPhoneNumbersProbe = diagnostic.WabaPhoneNumbersProbe,
                phoneNumberBelongsToConfiguredWaba = diagnostic.PhoneNumberBelongsToConfiguredWaba,
                tenantSettings
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTenantCommercialSettings(
            Guid tenantId,
            TenantCommercialAccessMode commercialAccessMode,
            Guid? forcedPlanId,
            string? commercialNotes,
            CancellationToken cancellationToken)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(currentTenant => currentTenant.Id == tenantId, cancellationToken);
            if (tenant is null)
            {
                return NotFound();
            }

            if (commercialAccessMode != TenantCommercialAccessMode.RequiresSubscription)
            {
                var hasValidPlan = forcedPlanId.HasValue &&
                    await _context.Planes.AsNoTracking().AnyAsync(plan => plan.Id == forcedPlanId && plan.Activo, cancellationToken);

                if (!hasValidPlan)
                {
                    TempData["PlatformError"] = "Los tenants exentos o internos requieren un plan forzado activo.";
                    return RedirectToAction(nameof(Index));
                }
            }

            tenant.CommercialAccessMode = commercialAccessMode;
            tenant.ForcedPlanId = commercialAccessMode == TenantCommercialAccessMode.RequiresSubscription
                ? null
                : forcedPlanId;
            tenant.CommercialNotes = string.IsNullOrWhiteSpace(commercialNotes) ? null : commercialNotes.Trim();
            tenant.CommercialUpdatedUtc = DateTime.UtcNow;
            tenant.CommercialUpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            await _context.SaveChangesAsync(cancellationToken);
            _accessCache.Invalidate(tenant.Id);

            TempData["PlatformSuccess"] = "Configuracion comercial del tenant actualizada.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> PromotionalCodes(CancellationToken cancellationToken)
        {
            var model = await BuildPromotionalCodesPageAsync(new PlatformPromotionalCodeCreateViewModel(), cancellationToken);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePromotionalCode(
            [Bind(Prefix = PromotionalCodeFormPrefix)] PlatformPromotionalCodeCreateViewModel model,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View("PromotionalCodes", await BuildPromotionalCodesPageAsync(model, cancellationToken));
            }

            var plan = await _context.Planes
                .AsNoTracking()
                .FirstOrDefaultAsync(currentPlan => currentPlan.Id == model.PlanId && currentPlan.Activo, cancellationToken);

            if (plan is null)
            {
                ModelState.AddModelError(
                    $"{PromotionalCodeFormPrefix}.{nameof(model.PlanId)}",
                    "Debes seleccionar un plan activo.");
                return View("PromotionalCodes", await BuildPromotionalCodesPageAsync(model, cancellationToken));
            }

            var code = new PromotionalCode
            {
                Id = Guid.NewGuid(),
                Codigo = model.Codigo.Trim().ToUpperInvariant(),
                Activo = model.Activo,
                TipoBeneficio = PromotionalBenefitType.FreeAccessDays,
                DiasGratis = model.DiasGratis,
                PlanId = model.PlanId,
                MaxUsos = model.MaxUsos,
                FechaExpiracionUtc = model.FechaExpiracionUtc,
                SoloPrimerRegistro = model.SoloPrimerRegistro,
                EmailObjetivo = string.IsNullOrWhiteSpace(model.EmailObjetivo) ? null : model.EmailObjetivo.Trim(),
                CreadoPorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                NotasInternas = string.IsNullOrWhiteSpace(model.NotasInternas) ? null : model.NotasInternas.Trim(),
                FechaCreacionUtc = DateTime.UtcNow
            };

            _context.PromotionalCodes.Add(code);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                TempData["PlatformSuccess"] = "Codigo promocional creado correctamente.";
                return RedirectToAction(nameof(PromotionalCodes));
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(
                    $"{PromotionalCodeFormPrefix}.{nameof(model.Codigo)}",
                    "Ya existe un codigo con ese valor.");
                return View("PromotionalCodes", await BuildPromotionalCodesPageAsync(model, cancellationToken));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePromotionalCode(Guid id, CancellationToken cancellationToken)
        {
            var code = await _context.PromotionalCodes.FirstOrDefaultAsync(currentCode => currentCode.Id == id, cancellationToken);
            if (code is null)
            {
                return NotFound();
            }

            code.Activo = !code.Activo;
            code.FechaActualizacionUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            TempData["PlatformSuccess"] = code.Activo
                ? "Codigo promocional activado."
                : "Codigo promocional desactivado.";

            return RedirectToAction(nameof(PromotionalCodes));
        }

        [HttpGet]
        public async Task<IActionResult> PromotionalCode(Guid id, CancellationToken cancellationToken)
        {
            var code = await _context.PromotionalCodes
                .AsNoTracking()
                .Include(currentCode => currentCode.Plan)
                .FirstOrDefaultAsync(currentCode => currentCode.Id == id, cancellationToken);

            if (code is null)
            {
                return NotFound();
            }

            var redemptions = await _context.PromotionalCodeRedemptions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(redemption => redemption.PromotionalCodeId == id)
                .Join(
                    _context.Tenants.AsNoTracking(),
                    redemption => redemption.TenantId,
                    tenant => tenant.Id,
                    (redemption, tenant) => new
                    {
                        redemption.EmailConsumidor,
                        redemption.FechaConsumoUtc,
                        TenantName = tenant.Nombre,
                        redemption.TenantCommercialAccessGrantId
                    })
                .ToListAsync(cancellationToken);

            var grantIds = redemptions
                .Where(redemption => redemption.TenantCommercialAccessGrantId.HasValue)
                .Select(redemption => redemption.TenantCommercialAccessGrantId!.Value)
                .Distinct()
                .ToList();

            var grants = await _context.TenantCommercialAccessGrants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(grant => grantIds.Contains(grant.Id))
                .ToDictionaryAsync(grant => grant.Id, cancellationToken);

            var model = new PlatformPromotionalCodeDetailsViewModel
            {
                Code = new PlatformPromotionalCodeListItemViewModel
                {
                    Id = code.Id,
                    Codigo = code.Codigo,
                    Activo = code.Activo,
                    PlanName = code.Plan?.Nombre ?? "Sin plan",
                    DiasGratis = code.DiasGratis,
                    MaxUsos = code.MaxUsos,
                    UsosActuales = code.UsosActuales,
                    FechaExpiracionUtc = code.FechaExpiracionUtc,
                    SoloPrimerRegistro = code.SoloPrimerRegistro,
                    EmailObjetivo = code.EmailObjetivo
                },
                NotasInternas = code.NotasInternas,
                Redemptions = redemptions
                    .OrderByDescending(redemption => redemption.FechaConsumoUtc)
                    .Select(redemption => new PlatformPromotionalCodeRedemptionItemViewModel
                    {
                        TenantName = redemption.TenantName,
                        EmailConsumidor = redemption.EmailConsumidor,
                        FechaConsumoUtc = redemption.FechaConsumoUtc,
                        AccessEndsUtc = redemption.TenantCommercialAccessGrantId.HasValue &&
                                        grants.TryGetValue(redemption.TenantCommercialAccessGrantId.Value, out var grant)
                            ? grant.FechaFinUtc
                            : null
                    })
                    .ToList()
            };

            return View(model);
        }

        private async Task<PlatformPromotionalCodesPageViewModel> BuildPromotionalCodesPageAsync(
            PlatformPromotionalCodeCreateViewModel createModel,
            CancellationToken cancellationToken)
        {
            var plans = await _context.Planes
                .AsNoTracking()
                .Where(plan => plan.Activo)
                .OrderBy(plan => plan.PrecioMensual)
                .ToListAsync(cancellationToken);

            var codes = await _context.PromotionalCodes
                .AsNoTracking()
                .Include(code => code.Plan)
                .OrderByDescending(code => code.FechaCreacionUtc)
                .Select(code => new PlatformPromotionalCodeListItemViewModel
                {
                    Id = code.Id,
                    Codigo = code.Codigo,
                    Activo = code.Activo,
                    PlanName = code.Plan != null ? code.Plan.Nombre : "Sin plan",
                    DiasGratis = code.DiasGratis,
                    MaxUsos = code.MaxUsos,
                    UsosActuales = code.UsosActuales,
                    FechaExpiracionUtc = code.FechaExpiracionUtc,
                    SoloPrimerRegistro = code.SoloPrimerRegistro,
                    EmailObjetivo = code.EmailObjetivo
                })
                .ToListAsync(cancellationToken);

            return new PlatformPromotionalCodesPageViewModel
            {
                AvailablePlans = plans,
                CreateForm = createModel,
                Codes = codes
            };
        }

        private async Task<PlatformTenantWhatsAppSummary> GetTenantWhatsAppSummaryAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            PlatformTenantWhatsAppSummary? summary = null;

            await _tenantExecutionService.RunForTenantAsync(
                tenantId,
                async (serviceProvider, scopedTenantId, ct) =>
                {
                    var settingsService = serviceProvider.GetRequiredService<ITenantWhatsAppSettingsService>();
                    var settings = await settingsService.GetSettingsForTenantAsync(scopedTenantId, ct);
                    var todayUsage = await settingsService.GetTodayUsageAsync(scopedTenantId, ct);
                    var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
                    var lastError = await db.WhatsAppMessageLogs
                        .AsNoTracking()
                        .Where(message =>
                            message.Direction == WhatsAppMessageDirections.Outbound &&
                            message.ErrorCode != null)
                        .OrderByDescending(message => message.CreatedAtUtc)
                        .Select(message => new
                        {
                            message.ErrorCode,
                            message.ErrorMessage,
                            message.CreatedAtUtc
                        })
                        .FirstOrDefaultAsync(ct);

                    var addonRecord = await db.TenantSubscriptionAddons
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.TenantId == tenantId, ct);

                    var subscriptionSvc = serviceProvider.GetRequiredService<SuscripcionService>();
                    var addonIsActive = addonRecord is not null && subscriptionSvc.IsWhatsAppAddonActive(addonRecord);

                    summary = new PlatformTenantWhatsAppSummary(
                        settings.IsEnabled,
                        settings.SendConfirmationOnCreate,
                        settings.SendReminderThreeHoursBefore,
                        settings.DailyMessageLimit,
                        todayUsage,
                        settings.TimeZoneId,
                        settings.Notes,
                        lastError?.ErrorCode,
                        lastError?.ErrorMessage,
                        lastError?.CreatedAtUtc,
                        addonIsActive ? addonRecord!.AddonCode : null,
                        addonIsActive && addonRecord!.TilopayRecurringPlanId is null,
                        addonIsActive ? addonRecord!.FechaFin : null,
                        addonIsActive ? addonRecord!.MonthlyMessageLimit : null);
                },
                cancellationToken);

            return summary ?? throw new InvalidOperationException("No fue posible cargar la configuracion WhatsApp del tenant.");
        }

        private static string ResolveCheckoutKind(PagoSuscripcion payment, Suscripcion? currentSubscription)
        {
            var planCode = payment.Plan?.Codigo?.Trim();
            if (!string.IsNullOrWhiteSpace(planCode) &&
                PlanCodes.WhatsAppAddons.Contains(planCode, StringComparer.OrdinalIgnoreCase))
            {
                return "PendingAddonCheckout";
            }

            if (currentSubscription is null ||
                currentSubscription.Estado is EstadoSuscripcion.Pendiente or EstadoSuscripcion.Fallida or EstadoSuscripcion.Suspendida or EstadoSuscripcion.Cancelada or EstadoSuscripcion.Vencida)
            {
                return "PendingSubscriptionSignup";
            }

            if (currentSubscription.PlanId != payment.PlanId)
            {
                return "PendingPlanChange";
            }

            return "PendingRecurringCheckout";
        }

        private sealed record PlatformTenantWhatsAppSummary(
            bool IsEnabled,
            bool SendConfirmationOnCreate,
            bool SendReminderThreeHoursBefore,
            int DailyMessageLimit,
            int TodayUsage,
            string TimeZoneId,
            string? Notes,
            string? LastErrorCode,
            string? LastErrorMessage,
            DateTime? LastErrorAtUtc,
            string? AddonCode,
            bool AddonIsManual,
            DateTime? AddonFechaFin,
            int? AddonMonthlyLimit);
    }
}
