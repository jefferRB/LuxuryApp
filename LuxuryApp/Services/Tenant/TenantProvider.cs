using System.Security.Claims;
using LuxuryApp.Services.Identity;

namespace LuxuryApp.Services.Tenant
{
    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private Guid? _cachedTenantId;

        public TenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid GetTenantId()
        {
            if (_cachedTenantId.HasValue)
                return _cachedTenantId.Value;

            var httpContext = _httpContextAccessor.HttpContext;

            if (httpContext == null)
                throw new Exception("HttpContext no disponible");

            var user = httpContext.User;

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
                throw new Exception("Usuario no autenticado");

            var tenantClaim = user.FindFirst(CustomClaimTypes.TenantId);

            if (tenantClaim == null)
                throw new Exception("TenantId no encontrado en claims");

            if (!Guid.TryParse(tenantClaim.Value, out var tenantId))
                throw new Exception("TenantId inválido");

            _cachedTenantId = tenantId;

            return tenantId;
        }

        public bool HasTenant()
        {
            try
            {
                return GetTenantId() != Guid.Empty;
            }
            catch
            {
                return false;
            }
        }
    }
}