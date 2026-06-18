using System.Security.Claims;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Layout;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.SaaS;
using Microsoft.AspNetCore.Identity;

namespace LuxuryApp.Services.Layout
{
    public sealed class PrivateNavigationService : IPrivateNavigationService
    {
        private static readonly IReadOnlyCollection<NavigationMenuItemViewModel> CommercialItems =
        [
            new() { Text = "Dashboard", Controller = "Dashboard", Action = "Index", Icon = "bi-grid-1x2" },
            new() { Text = "Clientes", Controller = "Clientes", Action = "Index", Icon = "bi-people" },
            new() { Text = "Funcionarios", Controller = "Funcionarios", Action = "Index", Icon = "bi-person-badge" },
            new() { Text = "Productos", Controller = "Productos", Action = "Index", Icon = "bi-box-seam" },
            new() { Text = "Calendario", Controller = "Calendar", Action = "Index", Icon = "bi-calendar3" },
            new() { Text = "Reservas", Controller = "Reservas", Action = "Index", Icon = "bi-calendar-check" },
            new() { Text = "Ingresos", Controller = "Cobros", Action = "Index", Icon = "bi-cash-coin" },
            new() { Text = "Egresos", Controller = "Egresos", Action = "Index", Icon = "bi-receipt" },
            new() { Text = "Informacion", Controller = "Informacion", Action = "Index", Icon = "bi-bar-chart-line" }
        ];

        private readonly UserManager<AppUsuario> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITenantCommercialAccessResolver _commercialAccessResolver;

        public PrivateNavigationService(
            UserManager<AppUsuario> userManager,
            IHttpContextAccessor httpContextAccessor,
            ITenantCommercialAccessResolver commercialAccessResolver)
        {
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _commercialAccessResolver = commercialAccessResolver;
        }

        public async Task<PrivateNavigationViewModel> BuildAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken = default)
        {
            if (principal.Identity?.IsAuthenticated != true)
            {
                return new PrivateNavigationViewModel();
            }

            var user = await _userManager.GetUserAsync(principal);
            if (user is null)
            {
                return new PrivateNavigationViewModel();
            }

            var isPlatformSuperAdmin = user.IsPlatformSuperAdmin || principal.HasClaim(
                CustomClaimTypes.PlatformSuperAdmin,
                bool.TrueString);

            var access = await ResolveAccessAsync(user, cancellationToken);
            var hasCommercialAccess = isPlatformSuperAdmin || access?.CanAccessApp == true;
            var canAccessCommercialModules = principal.IsInRole("Administrador") && hasCommercialAccess;

            var secondaryItems = new List<NavigationMenuItemViewModel>
            {
                new()
                {
                    Text = "Cuenta",
                    Controller = "Accounts",
                    Action = "Cuenta",
                    Icon = "bi-person-circle",
                    Highlight = false
                },
                new()
                {
                    Text = hasCommercialAccess ? "Suscripcion" : "Activar plan",
                    Controller = "Billing",
                    Action = "Planes",
                    Icon = "bi-credit-card-2-front",
                    Highlight = !hasCommercialAccess
                }
            };

            if (isPlatformSuperAdmin)
            {
                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = "Plataforma",
                    Controller = "Platform",
                    Action = "Index",
                    Icon = "bi-shield-check",
                    Highlight = false
                });
            }

            return new PrivateNavigationViewModel
            {
                IsAuthenticated = true,
                CanAccessCommercialModules = canAccessCommercialModules,
                AccountDisplayName = ResolveDisplayName(user),
                HomeController = canAccessCommercialModules
                    ? "Dashboard"
                    : isPlatformSuperAdmin
                        ? "Platform"
                        : "Billing",
                HomeAction = canAccessCommercialModules
                    ? "Index"
                    : isPlatformSuperAdmin
                        ? "Index"
                        : "Planes",
                AccessBadgeText = ResolveAccessBadgeText(isPlatformSuperAdmin, access),
                AccessBadgeTone = ResolveAccessBadgeTone(isPlatformSuperAdmin, access),
                PrimaryItems = canAccessCommercialModules ? CommercialItems : Array.Empty<NavigationMenuItemViewModel>(),
                SecondaryItems = secondaryItems
            };
        }

        private async Task<TenantCommercialAccessResult?> ResolveAccessAsync(
            AppUsuario user,
            CancellationToken cancellationToken)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Items.TryGetValue("TenantCommercialAccess", out var rawAccess) == true &&
                rawAccess is TenantCommercialAccessResult access)
            {
                return access;
            }

            if (user.TenantId == Guid.Empty)
            {
                return null;
            }

            return await _commercialAccessResolver.ResolveAsync(user.TenantId, user, cancellationToken);
        }

        private static string ResolveDisplayName(AppUsuario user)
        {
            if (!string.IsNullOrWhiteSpace(user.Name))
            {
                return user.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                return user.Email.Trim();
            }

            return "Cuenta";
        }

        private static string ResolveAccessBadgeText(
            bool isPlatformSuperAdmin,
            TenantCommercialAccessResult? access)
        {
            if (isPlatformSuperAdmin)
            {
                return "Acceso interno";
            }

            if (access?.CanAccessApp == true)
            {
                if (access.IsInGracePeriod)
                {
                    return "En gracia";
                }

                return string.IsNullOrWhiteSpace(access.EffectivePlanName)
                    ? "Acceso activo"
                    : $"Plan {access.EffectivePlanName}";
            }

            return access?.HasCommercialHistory == true
                ? "Plan vencido"
                : "Sin plan activo";
        }

        private static string ResolveAccessBadgeTone(
            bool isPlatformSuperAdmin,
            TenantCommercialAccessResult? access)
        {
            if (isPlatformSuperAdmin)
            {
                return "info";
            }

            if (access?.CanAccessApp == true)
            {
                return access.IsInGracePeriod ? "warning" : "success";
            }

            return access?.HasCommercialHistory == true
                ? "warning"
                : "danger";
        }
    }
}
