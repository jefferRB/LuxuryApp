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
                // MFA opcional para cualquier usuario autenticado (obligatorio solo para
                // superadmin vía RequireMfaEnrollmentFilter, nunca desde el menú).
                new()
                {
                    Text = "Doble autenticación",
                    Controller = "Seguridad",
                    Action = "Enrolar",
                    Icon = "bi-shield-lock",
                    Highlight = false
                },
                new()
                {
                    Text = hasCommercialAccess ? "Suscripcion" : "Activar plan",
                    Controller = "Billing",
                    // Con acceso comercial vamos a la vista privada de Suscripcion; sin acceso,
                    // al pricing publico (Planes) para activar/regularizar el plan.
                    Action = hasCommercialAccess ? "Suscripcion" : "Planes",
                    Icon = "bi-credit-card-2-front",
                    Highlight = !hasCommercialAccess
                }
            };

            // Ajustes fiscales del negocio (IVA): solo administradores con acceso comercial.
            if (canAccessCommercialModules)
            {
                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = "Impuestos",
                    Controller = "ConfiguracionFiscal",
                    Action = "Index",
                    Icon = "bi-percent",
                    Highlight = false
                });

                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = "Pagina publica",
                    Controller = "PaginaPublica",
                    Action = "Index",
                    Icon = "bi-window",
                    Highlight = false
                });

                // Bloqueos recurrentes de horario (almuerzo, limpieza...). Es configuración de
                // agenda, por eso vive junto a los demás ajustes del negocio y no en el menú principal.
                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = "Bloqueos de horario",
                    Controller = "BloqueosRecurrentes",
                    Action = "Index",
                    Icon = "bi-clock-history",
                    Highlight = false
                });

                // Inversionistas: reparto de la ganancia del negocio. Solo administradores.
                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = "Inversionistas",
                    Controller = "Inversionistas",
                    Action = "Index",
                    Icon = "bi-people-fill",
                    Highlight = false
                });
            }

            // Resumen Ejecutivo Mensual: función administrada EXCLUSIVAMENTE por el super admin
            // desde Plataforma (/Platform/MonthlyReports). Los tenants no ven ni configuran esto;
            // el dueño solo recibe el correo. Por eso no se agrega al menú del tenant.

            // Modulo WhatsApp: solo cuando el tenant tiene acceso comercial operativo.
            // La propia vista maneja el estado vacio si aun no hay paquete activo.
            if (canAccessCommercialModules)
            {
                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = "WhatsApp",
                    Controller = "WhatsApp",
                    Action = "Index",
                    Icon = "bi-whatsapp",
                    Highlight = false
                });
            }

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
