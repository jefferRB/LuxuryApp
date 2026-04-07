using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Identity;
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
            // 🔹 1. Ignorar rutas públicas / críticas
            var path = context.Request.Path.Value?.ToLower();

            if (path.StartsWith("/accounts") ||
                path.StartsWith("/home") ||
                path.StartsWith("/error") ||
                path.StartsWith("/billing"))
            {
                await _next(context);
                return;
            }

            // 🔹 2. Usuario no autenticado
            if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                await _next(context);
                return;
            }

            // 🔹 3. Obtener TenantId seguro
            var tenantClaim = context.User.FindFirst(CustomClaimTypes.TenantId);

            if (tenantClaim == null || !Guid.TryParse(tenantClaim.Value, out var tenantId))
            {
                _logger.LogWarning("TenantId inválido o inexistente");
                context.Response.Redirect("/Accounts/Acceso");
                return;
            }

            //  (SESSION_CONTEXT)
            var connection = db.Database.GetDbConnection();

            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "EXEC sp_set_session_context @key=N'TenantId', @value=@tenantId";

                var param = command.CreateParameter();
                param.ParameterName = "@tenantId";
                param.Value = tenantId;

                command.Parameters.Add(param);

                await command.ExecuteNonQueryAsync();
            }

            // 🔥 4. CACHE (CLAVE PARA ESCALAR)
            var cacheKey = $"suscripcion_{tenantId}";

            if (!_cache.TryGetValue(cacheKey, out Suscripcion suscripcion))
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

            // 🔹 5. Validaciones
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

            // 🔹 6. Guardar contexto
            context.Items["TenantId"] = tenantId;
            context.Items["Suscripcion"] = suscripcion;

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en SuscripcionMiddleware");

            // 🔒 fallback seguro
            context.Response.Redirect("/Home/Error");
        }
    }
}