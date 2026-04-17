using LuxuryApp.Controllers.Finanzas;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class CategoriasControllerTests
    {
        [Fact]
        public async Task Save_ShouldUpdateExistingCategoria()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Categorias.Add(new Categoria
            {
                Nombre = "Marketing",
                Detalle = "Ads"
            });
            await context.SaveChangesAsync();

            var categoria = await context.Categorias.SingleAsync();
            var controller = CreateController(context, tenantId);

            var result = await controller.Save(new Categoria
            {
                Id = categoria.Id,
                Nombre = "Marketing Digital",
                Detalle = "Ads y campañas"
            });

            Assert.IsType<OkResult>(result);

            var categoriaActualizada = await context.Categorias.SingleAsync();
            Assert.Equal("Marketing Digital", categoriaActualizada.Nombre);
            Assert.Equal("Ads y campañas", categoriaActualizada.Detalle);
        }

        [Fact]
        public async Task Delete_ShouldRemoveCategoria_WhenItIsUnused()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Categorias.Add(new Categoria
            {
                Nombre = "Papelería",
                Detalle = "Insumos"
            });
            await context.SaveChangesAsync();

            var categoria = await context.Categorias.SingleAsync();
            var controller = CreateController(context, tenantId);

            var result = await controller.Delete(categoria.Id);

            Assert.IsType<OkResult>(result);
            Assert.Empty(await context.Categorias.ToListAsync());
        }

        [Fact]
        public async Task Delete_ShouldBlockCategoria_WhenUsedByEgresos()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var categoria = new Categoria
            {
                Nombre = "Servicios",
                Detalle = "Pagos recurrentes"
            };
            context.Categorias.Add(categoria);
            await context.SaveChangesAsync();

            context.Egresos.Add(new Egreso
            {
                FechaEgreso = new DateTime(2026, 4, 13),
                Detalle = "Pago proveedor",
                Monto = 150,
                MetodoPago = "EFECTIVO",
                CategoriaId = categoria.Id
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context, tenantId);
            var result = await controller.Delete(categoria.Id);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("ya está siendo usada", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Single(await context.Categorias.ToListAsync());
        }

        private static CategoriasController CreateController(ProyectoIdentity.Datos.ApplicationDbContext context, Guid tenantId)
        {
            var controller = new CategoriasController(
                context,
                NullLogger<CategoriasController>.Instance);

            ControllerTestSupport.AttachHttpContext(
                controller,
                ControllerTestSupport.BuildTenantPrincipal("user-categorias", tenantId));

            return controller;
        }
    }
}
