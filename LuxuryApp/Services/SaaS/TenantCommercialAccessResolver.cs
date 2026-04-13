using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.SaaS
{
    public sealed class TenantCommercialAccessResolver : ITenantCommercialAccessResolver
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ITenantCommercialAccessCache _accessCache;

        public TenantCommercialAccessResolver(
            ApplicationDbContext context,
            IMemoryCache cache,
            ITenantCommercialAccessCache accessCache)
        {
            _context = context;
            _cache = cache;
            _accessCache = accessCache;
        }

        public async Task<TenantCommercialAccessResult> ResolveAsync(
            Guid tenantId,
            AppUsuario? user = null,
            CancellationToken cancellationToken = default)
        {
            if (tenantId == Guid.Empty)
            {
                return Denied(
                    tenantId,
                    TenantCommercialAccessMode.RequiresSubscription,
                    "No fue posible resolver el tenant.");
            }

            if (user?.IsPlatformSuperAdmin == true)
            {
                var superAdminPlan = await ResolvePlanAsync(tenantId, cancellationToken);
                return new TenantCommercialAccessResult
                {
                    CanAccessApp = true,
                    RequiresBilling = false,
                    IsPlatformSuperAdmin = true,
                    TenantId = tenantId,
                    EffectivePlanId = superAdminPlan?.Id,
                    EffectivePlanName = superAdminPlan?.Nombre,
                    CommercialAccessMode = superAdminPlan is null
                        ? TenantCommercialAccessMode.RequiresSubscription
                        : TenantCommercialAccessMode.Internal,
                    AccessSource = TenantCommercialAccessSource.PlatformSuperAdmin,
                    Reason = "Acceso interno de plataforma.",
                    HasCommercialHistory = true
                };
            }

            var cacheKey = _accessCache.BuildTenantKey(tenantId);
            if (_cache.TryGetValue(cacheKey, out TenantCommercialAccessResult? cached) && cached is not null)
            {
                return cached;
            }

            var result = await ResolveCoreAsync(tenantId, cancellationToken);
            _cache.Set(cacheKey, result, CacheDuration);
            return result;
        }

        private async Task<TenantCommercialAccessResult> ResolveCoreAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.ForcedPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

            if (tenant is null)
            {
                return Denied(
                    tenantId,
                    TenantCommercialAccessMode.RequiresSubscription,
                    "El tenant no existe.");
            }

            if (!tenant.Activo)
            {
                return Denied(
                    tenantId,
                    tenant.CommercialAccessMode,
                    "El tenant se encuentra suspendido o inactivo.");
            }

            if (tenant.CommercialAccessMode == TenantCommercialAccessMode.Exempt ||
                tenant.CommercialAccessMode == TenantCommercialAccessMode.Internal)
            {
                var forcedPlan = tenant.ForcedPlan;
                if (forcedPlan is null || !forcedPlan.Activo)
                {
                    return Denied(
                        tenantId,
                        tenant.CommercialAccessMode,
                        "El tenant tiene acceso comercial especial, pero no tiene un plan forzado valido.");
                }

                return new TenantCommercialAccessResult
                {
                    CanAccessApp = true,
                    RequiresBilling = false,
                    TenantId = tenantId,
                    EffectivePlanId = forcedPlan.Id,
                    EffectivePlanName = forcedPlan.Nombre,
                    CommercialAccessMode = tenant.CommercialAccessMode,
                    AccessSource = tenant.CommercialAccessMode == TenantCommercialAccessMode.Internal
                        ? TenantCommercialAccessSource.TenantInternal
                        : TenantCommercialAccessSource.TenantExempt,
                    Reason = tenant.CommercialAccessMode == TenantCommercialAccessMode.Internal
                        ? "Tenant interno con acceso operativo."
                        : "Tenant exento con acceso patrocinado.",
                    HasCommercialHistory = true
                };
            }

            var now = DateTime.UtcNow;
            var grant = await _context.TenantCommercialAccessGrants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(g => g.Plan)
                .Where(g =>
                    g.TenantId == tenantId &&
                    g.Activo &&
                    g.FechaInicioUtc <= now &&
                    g.FechaFinUtc >= now)
                .OrderByDescending(g => g.FechaFinUtc)
                .ThenByDescending(g => g.FechaInicioUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (grant is not null && grant.Plan is not null && grant.Plan.Activo)
            {
                return new TenantCommercialAccessResult
                {
                    CanAccessApp = true,
                    RequiresBilling = grant.RequiresBilling,
                    TenantId = tenantId,
                    EffectivePlanId = grant.PlanId,
                    EffectivePlanName = grant.Plan.Nombre,
                    CommercialAccessMode = tenant.CommercialAccessMode,
                    AccessSource = TenantCommercialAccessSource.PromotionalGrant,
                    Reason = "Acceso comercial temporal activo.",
                    AccessEndsUtc = grant.FechaFinUtc,
                    HasCommercialHistory = true
                };
            }

            var suscripcion = await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .ThenByDescending(s => s.FechaInicio)
                .FirstOrDefaultAsync(cancellationToken);

            if (suscripcion is not null &&
                suscripcion.Plan is not null &&
                suscripcion.Plan.Activo &&
                (suscripcion.Estado == EstadoSuscripcion.Activa || suscripcion.Estado == EstadoSuscripcion.Trial) &&
                (!suscripcion.FechaFin.HasValue || suscripcion.FechaFin.Value >= now) &&
                (!suscripcion.FechaTrialFin.HasValue || suscripcion.FechaTrialFin.Value >= now))
            {
                return new TenantCommercialAccessResult
                {
                    CanAccessApp = true,
                    RequiresBilling = true,
                    TenantId = tenantId,
                    EffectivePlanId = suscripcion.PlanId,
                    EffectivePlanName = suscripcion.Plan.Nombre,
                    CommercialAccessMode = tenant.CommercialAccessMode,
                    AccessSource = suscripcion.Estado == EstadoSuscripcion.Trial
                        ? TenantCommercialAccessSource.SubscriptionTrial
                        : TenantCommercialAccessSource.SubscriptionActive,
                    Reason = suscripcion.Estado == EstadoSuscripcion.Trial
                        ? "Acceso permitido por trial vigente."
                        : "Acceso permitido por suscripcion activa.",
                    AccessEndsUtc = suscripcion.FechaTrialFin ?? suscripcion.FechaFin,
                    HasCommercialHistory = true
                };
            }

            var hasCommercialHistory = suscripcion is not null ||
                await _context.TenantCommercialAccessGrants
                    .IgnoreQueryFilters()
                    .AnyAsync(grantHistory => grantHistory.TenantId == tenantId, cancellationToken);

            return Denied(
                tenantId,
                tenant.CommercialAccessMode,
                "El tenant no tiene acceso comercial activo.",
                hasCommercialHistory);
        }

        private async Task<Plan?> ResolvePlanAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.ForcedPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

            if (tenant?.ForcedPlan is not null && tenant.ForcedPlan.Activo)
            {
                return tenant.ForcedPlan;
            }

            return await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Include(s => s.Plan)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .Select(s => s.Plan)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static TenantCommercialAccessResult Denied(
            Guid tenantId,
            TenantCommercialAccessMode commercialAccessMode,
            string reason,
            bool hasCommercialHistory = false) =>
            new()
            {
                TenantId = tenantId,
                CommercialAccessMode = commercialAccessMode,
                AccessSource = TenantCommercialAccessSource.None,
                CanAccessApp = false,
                RequiresBilling = commercialAccessMode == TenantCommercialAccessMode.RequiresSubscription,
                Reason = reason,
                HasCommercialHistory = hasCommercialHistory
            };
    }
}
