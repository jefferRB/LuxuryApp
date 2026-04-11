using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProyectoIdentity.Datos;

public class SuscripcionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SuscripcionMiddleware> _logger;

    public SuscripcionMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<SuscripcionMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context, ApplicationDbContext db)
    {
        try
        {
            var path = (context.Request.Path.Value ?? string.Empty).ToLowerInvariant();

            if (path.StartsWith("/accounts") ||
                path.StartsWith("/home") ||
                path.StartsWith("/error") ||
                path.StartsWith("/billing"))
            {
                await _next(context);
                return;
            }

            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                await _next(context);
                return;
            }

            var tenantClaim = context.User.FindFirst(CustomClaimTypes.TenantId);

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId) || tenantId == Guid.Empty)
            {
                _logger.LogWarning("TenantId invalido o inexistente.");
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/Accounts/Acceso");
                return;
            }

            var tenantActivo = await db.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.Id == tenantId && t.Activo);

            if (!tenantActivo)
            {
                _logger.LogWarning("Tenant suspendido o inexistente detectado en middleware. TenantId {TenantId}", tenantId);
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/Accounts/Acceso");
                return;
            }

            var cacheKey = $"suscripcion_{tenantId}";

            if (!_cache.TryGetValue(cacheKey, out Suscripcion? suscripcion))
            {
                suscripcion = await db.Suscripciones
                    .AsNoTracking()
                    .Include(s => s.Plan)
                    .Where(s => s.TenantId == tenantId)
                    .OrderByDescending(s => s.FechaInicio)
                    .FirstOrDefaultAsync();

                if (suscripcion != null)
                {
                    _cache.Set(cacheKey, suscripcion, TimeSpan.FromMinutes(5));
                }
            }

            if (suscripcion == null)
            {
                context.Response.Redirect("/Billing/SinSuscripcion");
                return;
            }

            if (suscripcion.Estado != EstadoSuscripcion.Activa &&
                suscripcion.Estado != EstadoSuscripcion.Trial)
            {
                context.Response.Redirect("/Billing/PlanVencido");
                return;
            }

            if (suscripcion.FechaFin.HasValue &&
                suscripcion.FechaFin < DateTime.UtcNow)
            {
                context.Response.Redirect("/Billing/PlanVencido");
                return;
            }

            context.Items["TenantId"] = tenantId;
            context.Items["Suscripcion"] = suscripcion;

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en SuscripcionMiddleware");
            context.Response.Redirect("/Home/Error");
        }
    }
}
