using LuxuryApp.Models.Finanzas;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class EgresoServiceTests
    {
        [Fact]
        public async Task RegistrarAsync_ShouldPersistNormalizedExpense()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var categoria = await SeedCategoriaAsync(context, "Compras", "Operativo");
            var service = ControllerTestSupport.CreateEgresoService(context);

            await service.RegistrarAsync(new EgresoCreateRequest
            {
                FechaEgreso = new DateTime(2026, 4, 23, 10, 15, 49),
                Detalle = "  Pago    proveedor   local  ",
                Monto = 1250.567m,
                MetodoPago = " efectivo ",
                CategoriaId = categoria.Id
            });

            context.ChangeTracker.Clear();

            var egreso = await context.Egresos.AsNoTracking().SingleAsync();
            Assert.Equal(categoria.Id, egreso.CategoriaId);
            Assert.Equal(new DateTime(2026, 4, 23, 10, 15, 0), egreso.FechaEgreso);
            Assert.Equal("Pago proveedor local", egreso.Detalle);
            Assert.Equal(1250.57m, egreso.Monto);
            Assert.Equal("EFECTIVO", egreso.MetodoPago);
        }

        [Fact]
        public async Task RegistrarAsync_ShouldRejectInactiveCategoria()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var categoria = await SeedCategoriaAsync(context, "Compras", "Inactiva", activo: false);
            var service = ControllerTestSupport.CreateEgresoService(context);

            var exception = await Assert.ThrowsAsync<EgresoValidationException>(() => service.RegistrarAsync(new EgresoCreateRequest
            {
                FechaEgreso = new DateTime(2026, 4, 23, 11, 0, 0),
                Detalle = "Intento invalido",
                Monto = 500m,
                MetodoPago = "TARJETA",
                CategoriaId = categoria.Id
            }));

            Assert.Equal("Egreso.CategoriaId", exception.ModelStateKey);
            Assert.Empty(await context.Egresos.ToListAsync());
        }

        [Fact]
        public async Task RegistrarAsync_ShouldRejectCrossTenantCategoria()
        {
            var currentTenantId = Guid.NewGuid();
            var foreignTenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = foreignTenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var foreignCategoria = await SeedCategoriaAsync(context, "Externa", "Foreign");

            tenantProvider.TenantId = currentTenantId;
            context.ChangeTracker.Clear();

            var service = ControllerTestSupport.CreateEgresoService(context);
            var exception = await Assert.ThrowsAsync<EgresoValidationException>(() => service.RegistrarAsync(new EgresoCreateRequest
            {
                FechaEgreso = new DateTime(2026, 4, 23, 12, 0, 0),
                Detalle = "Cross tenant",
                Monto = 300m,
                MetodoPago = "SINPE",
                CategoriaId = foreignCategoria.Id
            }));

            Assert.Equal("Egreso.CategoriaId", exception.ModelStateKey);
            Assert.Empty(await context.Egresos.ToListAsync());
        }

        [Fact]
        public async Task RegistrarAsync_ShouldRejectUnsupportedPaymentMethod()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var categoria = await SeedCategoriaAsync(context, "Compras", "Operativo");
            var service = ControllerTestSupport.CreateEgresoService(context);

            var exception = await Assert.ThrowsAsync<EgresoValidationException>(() => service.RegistrarAsync(new EgresoCreateRequest
            {
                FechaEgreso = new DateTime(2026, 4, 23, 13, 0, 0),
                Detalle = "Metodo invalido",
                Monto = 150m,
                MetodoPago = "CHEQUE",
                CategoriaId = categoria.Id
            }));

            Assert.Equal("Egreso.MetodoPago", exception.ModelStateKey);
            Assert.Empty(await context.Egresos.ToListAsync());
        }

        private static async Task<Categoria> SeedCategoriaAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            string detalle,
            bool activo = true)
        {
            var categoria = new Categoria
            {
                Nombre = nombre,
                Detalle = detalle,
                Activo = activo
            };

            context.Categorias.Add(categoria);
            await context.SaveChangesAsync();
            return categoria;
        }
    }
}
