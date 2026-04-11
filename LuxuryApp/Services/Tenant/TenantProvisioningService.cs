using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Tenant
{
    public class TenantProvisioningService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUsuario> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly OpcionesOnboardingTenant _options;
        private readonly ILogger<TenantProvisioningService> _logger;

        public TenantProvisioningService(
            ApplicationDbContext context,
            UserManager<AppUsuario> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<OpcionesOnboardingTenant> options,
            ILogger<TenantProvisioningService> logger)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<TenantProvisioningResult> RegisterAsync(
            TenantRegistrationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var email = request.Email.Trim();
            var name = request.Name.Trim();
            var phoneNumber = request.PhoneNumber?.Trim();

            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser is not null)
            {
                return TenantProvisioningResult.Failure("El correo ya está registrado.");
            }

            var executionStrategy = _context.Database.CreateExecutionStrategy();
            TenantProvisioningResult? provisioningResult = null;

            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    foreach (var roleName in GetRegistrationRoles())
                    {
                        await EnsureRoleExistsAsync(roleName);
                    }

                    var tenant = new LuxuryApp.Models.SaaS.Tenant
                    {
                        Nombre = BuildTenantName(name, email),
                        Activo = true,
                        FechaCreacion = DateTime.UtcNow
                    };

                    _context.Tenants.Add(tenant);
                    await _context.SaveChangesAsync(cancellationToken);

                    var initialSubscriptionCreated = await CreateInitialSubscriptionAsync(tenant.Id, cancellationToken);

                    var usuario = new AppUsuario
                    {
                        UserName = email,
                        Email = email,
                        Name = name,
                        PhoneNumber = phoneNumber,
                        State = true,
                        TenantId = tenant.Id
                    };

                    var createUserResult = await _userManager.CreateAsync(usuario, request.Password);
                    if (!createUserResult.Succeeded)
                    {
                        provisioningResult = TenantProvisioningResult.Failure(
                            createUserResult.Errors.Select(error => error.Description).ToArray());
                        await transaction.RollbackAsync(cancellationToken);
                        return;
                    }

                    foreach (var roleName in GetRegistrationRoles())
                    {
                        var addToRoleResult = await _userManager.AddToRoleAsync(usuario, roleName);
                        if (!addToRoleResult.Succeeded)
                        {
                            provisioningResult = TenantProvisioningResult.Failure(
                                addToRoleResult.Errors.Select(error => error.Description).ToArray());
                            await transaction.RollbackAsync(cancellationToken);
                            return;
                        }
                    }

                    await transaction.CommitAsync(cancellationToken);

                    _logger.LogInformation(
                        "Tenant provisionado correctamente. TenantId {TenantId}. UserId {UserId}. InitialSubscriptionCreated {InitialSubscriptionCreated}.",
                        tenant.Id,
                        usuario.Id,
                        initialSubscriptionCreated);

                    provisioningResult = TenantProvisioningResult.Success(
                        usuario,
                        tenant.Id,
                        initialSubscriptionCreated);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });

            return provisioningResult
                ?? throw new InvalidOperationException("No se pudo completar el aprovisionamiento del tenant.");
        }

        private async Task<bool> CreateInitialSubscriptionAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            if (!_options.CreateInitialSubscription)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_options.InitialPlanName))
            {
                throw new InvalidOperationException(
                    "TenantOnboarding:InitialPlanName es obligatorio cuando CreateInitialSubscription es true.");
            }

            var plan = await _context.Planes
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.Activo && p.Nombre == _options.InitialPlanName,
                    cancellationToken);

            if (plan is null)
            {
                throw new InvalidOperationException(
                    $"No existe un plan activo con nombre '{_options.InitialPlanName}' para el onboarding del tenant.");
            }

            var now = DateTime.UtcNow;
            var subscription = new Suscripcion
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                Proveedor = PaymentProviderType.None,
                Estado = _options.InitialSubscriptionState,
                FechaInicio = now,
                FechaUltimaActualizacionUtc = now,
                MotivoEstado = "Suscripción inicial creada durante el registro."
            };

            if (_options.InitialSubscriptionState == EstadoSuscripcion.Trial)
            {
                if (_options.TrialDays <= 0)
                {
                    throw new InvalidOperationException(
                        "TenantOnboarding:TrialDays debe ser mayor que cero cuando el estado inicial es Trial.");
                }

                subscription.FechaTrialFin = now.AddDays(_options.TrialDays);
            }

            _context.Suscripciones.Add(subscription);
            await _context.SaveChangesAsync(cancellationToken);

            return true;
        }

        private IEnumerable<string> GetRegistrationRoles()
        {
            yield return GetRequiredRoleName(_options.RegistrationRole, nameof(_options.RegistrationRole));

            if (_options.AddRegisteredRole)
            {
                var registeredRole = GetRequiredRoleName(_options.RegisteredRole, nameof(_options.RegisteredRole));
                if (!string.Equals(registeredRole, _options.RegistrationRole, StringComparison.OrdinalIgnoreCase))
                {
                    yield return registeredRole;
                }
            }
        }

        private static string GetRequiredRoleName(string? value, string optionName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"La opción '{optionName}' es obligatoria.");
            }

            return value.Trim();
        }

        private async Task EnsureRoleExistsAsync(string roleName)
        {
            if (await _roleManager.RoleExistsAsync(roleName))
            {
                return;
            }

            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"No se pudo preparar el rol '{roleName}' para el onboarding del tenant.");
            }

            _logger.LogWarning("El rol {RoleName} no existía y fue creado automáticamente durante el onboarding.", roleName);
        }

        private static string BuildTenantName(string name, string email)
        {
            var tenantName = string.IsNullOrWhiteSpace(name) ? email : name;
            return tenantName.Length <= 150
                ? tenantName
                : tenantName[..150];
        }
    }
}
