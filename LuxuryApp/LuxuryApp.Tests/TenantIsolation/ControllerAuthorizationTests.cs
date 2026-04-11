using LuxuryApp.Controllers.Calendar;
using LuxuryApp.Controllers;
using LuxuryApp.Controllers.DataBase;
using LuxuryApp.Controllers.Finanzas;
using LuxuryApp.Controllers.Funcionarios;
using LuxuryApp.Controllers.Identity;
using LuxuryApp.Controllers.Productos;
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
                typeof(WhatsAppTestController)
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
    }
}
