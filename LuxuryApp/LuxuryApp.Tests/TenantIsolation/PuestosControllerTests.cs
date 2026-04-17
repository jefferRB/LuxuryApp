using LuxuryApp.Controllers.Funcionarios;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class PuestosControllerTests
    {
        [Fact]
        public async Task Save_ShouldUpdateExistingPuesto()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Colorista",
                Detalle = "Cabello"
            });
            await context.SaveChangesAsync();

            var puesto = await context.Puestos.SingleAsync();
            var controller = CreateController(context, tenantId);

            var result = await controller.Save(new Puesto
            {
                IdPuesto = puesto.IdPuesto,
                NombrePuesto = "Colorista Senior",
                Detalle = "Cabello y asesoría"
            });

            Assert.IsType<OkResult>(result);

            var puestoActualizado = await context.Puestos.SingleAsync();
            Assert.Equal("Colorista Senior", puestoActualizado.NombrePuesto);
            Assert.Equal("Cabello y asesoría", puestoActualizado.Detalle);
        }

        [Fact]
        public async Task Delete_ShouldRemovePuesto_WhenNoFuncionariosAreAssociated()
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
            var controller = CreateController(context, tenantId);

            var result = await controller.Delete(puesto.IdPuesto);

            Assert.IsType<OkResult>(result);
            Assert.Empty(await context.Puestos.ToListAsync());
        }

        [Fact]
        public async Task Delete_ShouldBlockPuesto_WhenFuncionariosExist()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var puesto = new Puesto
            {
                NombrePuesto = "Barbero",
                Detalle = "Cabina 1"
            };
            context.Puestos.Add(puesto);
            await context.SaveChangesAsync();

            context.Funcionarios.Add(new Funcionario
            {
                Nombre = "Luis",
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#444444",
                PorcentajeGanancia = 35,
                PorcentajeProducto = 5,
                FechaIngreso = new DateTime(2026, 4, 13),
                Activo = true
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context, tenantId);
            var result = await controller.Delete(puesto.IdPuesto);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("funcionarios asociados", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Single(await context.Puestos.ToListAsync());
        }

        private static PuestosController CreateController(ProyectoIdentity.Datos.ApplicationDbContext context, Guid tenantId)
        {
            var controller = new PuestosController(
                context,
                NullLogger<PuestosController>.Instance);

            ControllerTestSupport.AttachHttpContext(
                controller,
                ControllerTestSupport.BuildTenantPrincipal("user-puestos", tenantId));

            return controller;
        }
    }
}
