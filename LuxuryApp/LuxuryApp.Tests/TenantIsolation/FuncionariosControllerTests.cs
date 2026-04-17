using LuxuryApp.Controllers.Funcionarios;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class FuncionariosControllerTests
    {
        [Fact]
        public async Task Create_ShouldPersistFuncionario_WhenTenantPlanAllowsIt()
        {
            var tenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Full",
                PrecioMensual = 99,
                Moneda = "CRC",
                Activo = true,
                MaxFuncionarios = 5
            });

            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Estilista",
                Detalle = "Atención general"
            });

            await context.SaveChangesAsync();

            var puesto = await context.Puestos.SingleAsync();
            var controller = CreateController(context, tenantId);
            controller.HttpContext.Items["TenantCommercialAccess"] = new TenantCommercialAccessResult
            {
                CanAccessApp = true,
                TenantId = tenantId,
                EffectivePlanId = planId,
                EffectivePlanName = "Full"
            };

            var result = await controller.Create(new Funcionario
            {
                Nombre = "Ana",
                Telefono = "8888-0000",
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#111111",
                PorcentajeGanancia = 40,
                PorcentajeProducto = 10,
                FechaIngreso = new DateTime(2026, 4, 13)
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(FuncionariosController.Index), redirect.ActionName);

            var funcionario = await context.Funcionarios.SingleAsync();
            Assert.Equal("Ana", funcionario.Nombre);
            Assert.Equal(puesto.IdPuesto, funcionario.IdPuesto);
        }

        [Fact]
        public async Task Create_ShouldExposeError_WhenCommercialAccessCannotBeResolved()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Recepción",
                Detalle = "Front desk"
            });
            await context.SaveChangesAsync();

            var puesto = await context.Puestos.SingleAsync();
            var controller = CreateController(context, tenantId);

            var result = await controller.Create(new Funcionario
            {
                Nombre = "Carlos",
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#222222",
                PorcentajeGanancia = 30,
                PorcentajeProducto = 5,
                FechaIngreso = new DateTime(2026, 4, 13)
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(FuncionariosController.Index), redirect.ActionName);
            Assert.Equal(
                "No fue posible resolver el acceso comercial del tenant para validar el límite de funcionarios.",
                controller.TempData["Error"]);

            Assert.Empty(await context.Funcionarios.ToListAsync());
        }

        [Fact]
        public async Task Create_ShouldReturnViewWithModelError_WhenPuestoIsInvalid()
        {
            var tenantId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Full",
                PrecioMensual = 99,
                Moneda = "CRC",
                Activo = true,
                MaxFuncionarios = 5
            });

            await context.SaveChangesAsync();

            var controller = CreateController(context, tenantId);
            controller.HttpContext.Items["TenantCommercialAccess"] = new TenantCommercialAccessResult
            {
                CanAccessApp = true,
                TenantId = tenantId,
                EffectivePlanId = planId,
                EffectivePlanName = "Full"
            };

            var result = await controller.Create(new Funcionario
            {
                Nombre = "Paola",
                Telefono = "8888-4444",
                IdPuesto = 999,
                ColorCalendario = "#333333",
                PorcentajeGanancia = 35,
                PorcentajeProducto = 10,
                FechaIngreso = new DateTime(2026, 4, 13)
            });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Funcionario>(view.Model);

            Assert.Equal("Paola", model.Nombre);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains(nameof(Funcionario.IdPuesto), controller.ModelState.Keys);
            Assert.Empty(await context.Funcionarios.ToListAsync());
        }

        [Fact]
        public async Task Eliminar_ShouldBlockDeletion_WhenFuncionarioHasRelatedPayments()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Masajista",
                Detalle = "Spa"
            });
            await context.SaveChangesAsync();

            var puesto = await context.Puestos.SingleAsync();
            var funcionario = new Funcionario
            {
                Nombre = "Andrea",
                Telefono = "8888-5555",
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#444444",
                PorcentajeGanancia = 40,
                PorcentajeProducto = 10,
                FechaIngreso = new DateTime(2026, 4, 13),
                Activo = true
            };

            context.Funcionarios.Add(funcionario);
            await context.SaveChangesAsync();

            context.PagosFuncionarios.Add(new PagoFuncionario
            {
                FuncionarioId = funcionario.IdFuncionario,
                MontoPagado = 250,
                FechaPago = new DateTime(2026, 4, 13),
                InicioSemana = new DateTime(2026, 4, 13),
                FinSemana = new DateTime(2026, 4, 19),
                Observacion = "Pago semanal"
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context, tenantId);

            var result = await controller.Eliminar(funcionario.IdFuncionario);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(FuncionariosController.Index), redirect.ActionName);
            Assert.Equal(
                "No se puede eliminar el funcionario porque tiene citas, cobros o pagos asociados. Puedes dejarlo inactivo si ya no trabaja en el negocio.",
                controller.TempData["Error"]);
            Assert.Single(await context.Funcionarios.ToListAsync());
        }

        private static FuncionariosController CreateController(ProyectoIdentity.Datos.ApplicationDbContext context, Guid tenantId)
        {
            var controller = new FuncionariosController(
                context,
                NullLogger<FuncionariosController>.Instance);

            ControllerTestSupport.AttachHttpContext(
                controller,
                ControllerTestSupport.BuildTenantPrincipal("user-funcionarios", tenantId));

            return controller;
        }
    }
}
