using System.Security.Claims;
using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.SaaS;
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

        public PlatformController(
            ApplicationDbContext context,
            ITenantCommercialAccessResolver commercialAccessResolver,
            ITenantCommercialAccessCache accessCache)
        {
            _context = context;
            _commercialAccessResolver = commercialAccessResolver;
            _accessCache = accessCache;
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
                    Reason = access.Reason
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

            var model = new PlatformDashboardViewModel
            {
                TotalTenants = tenants.Count,
                TotalUsers = await _context.Users.CountAsync(cancellationToken),
                TotalActiveSubscriptions = totalActiveSubscriptions,
                TotalPromotionalCodes = await _context.PromotionalCodes.CountAsync(cancellationToken),
                AvailablePlans = plans,
                Tenants = tenantRows,
                RecentUsers = recentUsers,
                RecentPayments = recentPayments
            };

            return View(model);
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
    }
}
