using LuxuryApp.Controllers.Calendar;
using LuxuryApp.Controllers;
using LuxuryApp.Controllers.DataBase;
using LuxuryApp.Controllers.Finanzas;
using LuxuryApp.Controllers.Funcionarios;
using LuxuryApp.Controllers.Identity;
using LuxuryApp.Controllers.Platform;
using LuxuryApp.Controllers.Productos;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Authorization;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class ControllerAuthorizationTests
    {
        [Fact]
        public void SensitiveControllers_ShouldRequireAdministradorRole()
        {
            var sensitiveControllers = new[]
            {
                typeof(CalendarController),
                typeof(ClientesController),
                typeof(CategoriasController),
                typeof(CobrosController),
                typeof(DashboardController),
                typeof(EgresosController),
                typeof(ServiciosController),
                typeof(FuncionariosController),
                typeof(PuestosController),
                typeof(InformacionController),
                typeof(ProductosController),
                typeof(BillingController),
                typeof(RolesController),
                // Módulos nuevos: la participación de un inversionista y los bloqueos de agenda
                // son configuración sensible del negocio, solo para administradores.
                typeof(LuxuryApp.Controllers.Inversionistas.InversionistasController),
                typeof(LuxuryApp.Controllers.Horarios.BloqueosRecurrentesController)
            };

            foreach (var controllerType in sensitiveControllers)
            {
                var authorizeAttribute = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                    .OfType<AuthorizeAttribute>()
                    .FirstOrDefault();

                Assert.NotNull(authorizeAttribute);
                Assert.Equal("Administrador", authorizeAttribute!.Roles);
            }
        }

        [Fact]
        public void PlatformController_ShouldRequirePlatformSuperAdminPolicy()
        {
            var authorizeAttribute = typeof(PlatformController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .FirstOrDefault();

            Assert.NotNull(authorizeAttribute);
            Assert.Equal(PlatformAuthorizationPolicies.PlatformSuperAdmin, authorizeAttribute!.Policy);
        }

        [Fact]
        public void PlatformMonthlyReportsController_ShouldRequirePlatformSuperAdminPolicy()
        {
            var authorizeAttribute = typeof(PlatformMonthlyReportsController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .FirstOrDefault();

            Assert.NotNull(authorizeAttribute);
            Assert.Null(authorizeAttribute!.Roles);
            Assert.Equal(PlatformAuthorizationPolicies.PlatformSuperAdmin, authorizeAttribute.Policy);
        }

        [Fact]
        public void RecurringReconciliationController_ShouldRequirePlatformSuperAdminPolicy()
        {
            var authorizeAttribute = typeof(RecurringReconciliationController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .FirstOrDefault();

            Assert.NotNull(authorizeAttribute);
            Assert.Null(authorizeAttribute!.Roles);
            Assert.Equal(PlatformAuthorizationPolicies.PlatformSuperAdmin, authorizeAttribute.Policy);
        }
    }
}
