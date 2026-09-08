using System.Security.Claims;
using LuxuryApp.Models.Asociados;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Layout;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Asociados;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.SaaS;
using Microsoft.AspNetCore.Identity;

namespace LuxuryApp.Services.Layout
{
    public sealed class PrivateNavigationService : IPrivateNavigationService
    {
        /// <summary>
        /// Módulos del menú principal con el permiso que los abre. El administrador los ve todos;
        /// un asociado ve solo los que tiene concedidos.
        ///
        /// <para>
        /// Esto es SOLO experiencia de usuario: esconder un ítem no autoriza nada. La puerta real
        /// está en <c>[RequirePermission]</c> dentro de cada controlador.
        /// </para>
        /// </summary>
        private static readonly IReadOnlyList<(NavigationMenuItemViewModel Item, string Permiso)> CommercialItems =
        [
            (new() { Text = "Dashboard", Controller = "Dashboard", Action = "Index", Icon = "bi-grid-1x2" },
                AppPermissions.DashboardView),
            (new() { Text = "Clientes", Controller = "Clientes", Action = "Index", Icon = "bi-people" },
                AppPermissions.ClientsView),
            (new() { Text = "Funcionarios", Controller = "Funcionarios", Action = "Index", Icon = "bi-person-badge" },
                AppPermissions.EmployeesView),
            (new() { Text = "Productos", Controller = "Productos", Action = "Index", Icon = "bi-box-seam" },
                AppPermissions.ProductsView),
            (new() { Text = "Calendario", Controller = "Calendar", Action = "Index", Icon = "bi-calendar3" },
                AppPermissions.CalendarView),
            (new() { Text = "Reservas", Controller = "Reservas", Action = "Index", Icon = "bi-calendar-check" },
                AppPermissions.ReservationsView),
            (new() { Text = "Ingresos", Controller = "Cobros", Action = "Index", Icon = "bi-cash-coin" },
                AppPermissions.IncomeView),
            (new() { Text = "Egresos", Controller = "Egresos", Action = "Index", Icon = "bi-receipt" },
                AppPermissions.ExpensesView),
            (new() { Text = "Informacion", Controller = "Informacion", Action = "Index", Icon = "bi-bar-chart-line" },
                AppPermissions.InformationView)
        ];

        private readonly UserManager<AppUsuario> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITenantCommercialAccessResolver _commercialAccessResolver;
        private readonly IAssociatePermissionService _associatePermissionService;

        public PrivateNavigationService(
            UserManager<AppUsuario> userManager,
            IHttpContextAccessor httpContextAccessor,
            ITenantCommercialAccessResolver commercialAccessResolver,
            IAssociatePermissionService associatePermissionService)
        {
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _commercialAccessResolver = commercialAccessResolver;
            _associatePermissionService = associatePermissionService;
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

            var esAdministrador = principal.IsInRole(AppRoles.Administrador);
            var esAsociado = !esAdministrador && principal.IsInRole(AppRoles.Asociado);

            // Un asociado también trabaja dentro del negocio: si el plan está al día, ve los
            // módulos que le concedieron. El dueño sigue siendo el único que gestiona el plan.
            var canAccessCommercialModules = (esAdministrador || esAsociado) && hasCommercialAccess;

            var permisos = esAsociado
                ? await _associatePermissionService.ObtenerDelUsuarioActualAsync(cancellationToken)
                : AssociatePermissionSet.Ninguno;

            bool Puede(string permiso) => esAdministrador || permisos.Tiene(permiso);

            var primaryItems = canAccessCommercialModules
                ? CommercialItems
                    .Where(entrada => Puede(entrada.Permiso))
                    .Select(entrada => entrada.Item)
                    .ToArray()
                : Array.Empty<NavigationMenuItemViewModel>();

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
                }
            };

            // La suscripción es asunto de la parte contratante: el asociado no la ve ni la toca.
            if (!esAsociado)
            {
                secondaryItems.Add(new NavigationMenuItemViewModel
                {
                    Text = hasCommercialAccess ? "Suscripcion" : "Activar plan",
                    Controller = "Billing",
                    // Con acceso comercial vamos a la vista privada de Suscripcion; sin acceso,
                    // al pricing publico (Planes) para activar/regularizar el plan.
                    Action = hasCommercialAccess ? "Suscripcion" : "Planes",
                    Icon = "bi-credit-card-2-front",
                    Highlight = !hasCommercialAccess
                });
            }

            if (canAccessCommercialModules)
            {
                // Ajustes fiscales, bloqueos de agenda y WhatsApp son configuración del negocio:
                // se mantienen exclusivos del administrador, sin permiso delegable.
                if (esAdministrador)
                {
                    secondaryItems.Add(new NavigationMenuItemViewModel
                    {
                        Text = "Impuestos",
                        Controller = "ConfiguracionFiscal",
                        Action = "Index",
                        Icon = "bi-percent",
                        Highlight = false
                    });
                }

                if (Puede(AppPermissions.PublicWebsiteView))
                {
                    secondaryItems.Add(new NavigationMenuItemViewModel
                    {
                        Text = "Pagina publica",
                        Controller = "PaginaPublica",
                        Action = "Index",
                        Icon = "bi-window",
                        Highlight = false
                    });
                }

                if (esAdministrador)
                {
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
                }

                // Asociados: inversionistas, socios y colaboradores externos, con sus accesos y su
                // participación en la ganancia del negocio.
                if (Puede(AppPermissions.AssociatesView))
                {
                    secondaryItems.Add(new NavigationMenuItemViewModel
                    {
                        Text = "Asociados",
                        Controller = "Asociados",
                        Action = "Index",
                        Icon = "bi-people-fill",
                        Highlight = false
                    });
                }
            }

            // Resumen Ejecutivo Mensual: función administrada EXCLUSIVAMENTE por el super admin
            // desde Plataforma (/Platform/MonthlyReports). Los tenants no ven ni configuran esto;
            // el dueño solo recibe el correo. Por eso no se agrega al menú del tenant.

            // Modulo WhatsApp: solo cuando el tenant tiene acceso comercial operativo.
            // La propia vista maneja el estado vacio si aun no hay paquete activo.
            if (canAccessCommercialModules && esAdministrador)
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

            var (homeController, homeAction) = ResolveHome(
                canAccessCommercialModules,
                esAdministrador,
                isPlatformSuperAdmin,
                primaryItems,
                secondaryItems,
                esAsociado);

            return new PrivateNavigationViewModel
            {
                IsAuthenticated = true,
                CanAccessCommercialModules = canAccessCommercialModules,
                AccountDisplayName = ResolveDisplayName(user),
                HomeController = homeController,
                HomeAction = homeAction,
                AccessBadgeText = ResolveAccessBadgeText(isPlatformSuperAdmin, access),
                AccessBadgeTone = ResolveAccessBadgeTone(isPlatformSuperAdmin, access),
                PrimaryItems = primaryItems,
                SecondaryItems = secondaryItems
            };
        }

        /// <summary>
        /// Destino del logo. Para el administrador es el Dashboard de siempre; para un asociado es
        /// el primer módulo que sí puede abrir, así el logo nunca lleva a un Access Denied.
        /// </summary>
        private static (string Controller, string Action) ResolveHome(
            bool canAccessCommercialModules,
            bool esAdministrador,
            bool isPlatformSuperAdmin,
            IReadOnlyList<NavigationMenuItemViewModel> primaryItems,
            IReadOnlyList<NavigationMenuItemViewModel> secondaryItems,
            bool esAsociado)
        {
            if (canAccessCommercialModules && (esAdministrador || isPlatformSuperAdmin))
            {
                return ("Dashboard", "Index");
            }

            if (esAsociado)
            {
                if (primaryItems.Count > 0)
                {
                    return (primaryItems[0].Controller, primaryItems[0].Action);
                }

                var paginaPublica = secondaryItems
                    .FirstOrDefault(item => item.Controller == "PaginaPublica");

                if (paginaPublica is not null)
                {
                    return (paginaPublica.Controller, paginaPublica.Action);
                }

                var asociados = secondaryItems.FirstOrDefault(item => item.Controller == "Asociados");
                if (asociados is not null)
                {
                    return (asociados.Controller, asociados.Action);
                }

                return ("Home", "SinPermisos");
            }

            if (canAccessCommercialModules)
            {
                return ("Dashboard", "Index");
            }

            return isPlatformSuperAdmin
                ? ("Platform", "Index")
                : ("Billing", "Planes");
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
