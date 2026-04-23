using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Productos;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Tests.Support;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class CobroQueryServiceTests
    {
        [Fact]
        public async Task BuildIndexViewModelAsync_ShouldApplyWeekFilter_FromMondayToSunday()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Semana");
            var monday = StartOfWeek(DateTime.Today);
            var sunday = monday.AddDays(6);
            var nextMonday = monday.AddDays(7);

            await SeedCobroServicioAsync(context, funcionario, "Lunes", monday.AddHours(9), 50m, "EFECTIVO", "Cliente Lunes");
            await SeedCobroServicioAsync(context, funcionario, "Domingo", sunday.AddHours(18), 60m, "EFECTIVO", "Cliente Domingo");
            await SeedCobroServicioAsync(context, funcionario, "Siguiente", nextMonday.AddHours(9), 70m, "EFECTIVO", "Cliente Siguiente");

            var queryService = new CobroQueryService(context);
            var result = await queryService.BuildIndexViewModelAsync(new CobroFiltroViewModel { VistaTiempo = "semana" }, includeFilterOptions: false);

            Assert.Equal(2, result.Cobros.Count);
            Assert.Contains(result.Cobros, c => c.NombreCliente == "Cliente Lunes");
            Assert.Contains(result.Cobros, c => c.NombreCliente == "Cliente Domingo");
            Assert.DoesNotContain(result.Cobros, c => c.NombreCliente == "Cliente Siguiente");
        }

        [Fact]
        public async Task BuildIndexViewModelAsync_ShouldApplyFuncionarioMetodoYTipoFilters()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var targetFuncionario = await SeedFuncionarioAsync(context, "Objetivo");
            var otherFuncionario = await SeedFuncionarioAsync(context, "Otro");

            await SeedCobroServicioAsync(context, targetFuncionario, "Servicio", new DateTime(2026, 4, 23, 8, 0, 0), 90m, "EFECTIVO", "Servicio");
            await SeedCobroProductoAsync(context, targetFuncionario, "Producto Target", new DateTime(2026, 4, 23, 9, 0, 0), 35m, "SINPE", "Producto Target");
            await SeedCobroProductoAsync(context, otherFuncionario, "Producto Otro", new DateTime(2026, 4, 23, 10, 0, 0), 45m, "SINPE", "Producto Otro");

            var queryService = new CobroQueryService(context);
            var result = await queryService.BuildIndexViewModelAsync(new CobroFiltroViewModel
            {
                VistaTiempo = "todo",
                FuncionarioId = targetFuncionario.IdFuncionario,
                MetodoPago = "sinpe",
                MostrarServicios = false,
                MostrarProductos = true
            }, includeFilterOptions: false);

            var row = Assert.Single(result.Cobros);
            Assert.Equal("Producto Target", row.NombreCliente);
            Assert.False(row.EsServicio);
            Assert.Equal(35m, result.TotalProductos);
        }

        [Fact]
        public async Task BuildIndexViewModelAsync_ShouldCalculateKpisConsistently()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "KPIs", porcentajeGanancia: 50m, porcentajeProducto: 10m);
            await SeedCobroServicioAsync(context, funcionario, "Servicio KPI", new DateTime(2026, 4, 23, 8, 0, 0), 100m, "EFECTIVO", "Servicio KPI");
            await SeedCobroProductoAsync(context, funcionario, "Producto KPI", new DateTime(2026, 4, 23, 9, 0, 0), 200m, "TARJETA", "Producto KPI");

            var queryService = new CobroQueryService(context);
            var result = await queryService.BuildIndexViewModelAsync(new CobroFiltroViewModel { VistaTiempo = "todo" }, includeFilterOptions: false);

            Assert.Equal(100m, result.TotalServicios);
            Assert.Equal(200m, result.TotalProductos);
            Assert.Equal(300m, result.TotalGenerado);
            Assert.Equal(39m, result.TotalImpuestos);
            Assert.Equal(261m, result.TotalSinImpuestos);
            Assert.Equal(60.9m, result.PagoColaboradores);
            Assert.Equal(200.1m, result.GananciaNegocio);
            Assert.Equal(100m, result.GananciaEfectivo);
            Assert.Equal(200m, result.GananciaTarjeta);
            Assert.Equal(0m, result.GananciaSinpe);
        }

        [Fact]
        public async Task BuildIndexViewModelAsync_ShouldRespectTenantIsolation()
        {
            var currentTenantId = Guid.NewGuid();
            var foreignTenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = foreignTenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var foreignFuncionario = await SeedFuncionarioAsync(context, "Externo");
            await SeedCobroServicioAsync(context, foreignFuncionario, "Servicio Externo", new DateTime(2026, 4, 23, 8, 0, 0), 80m, "EFECTIVO", "Cliente Externo");

            tenantProvider.TenantId = currentTenantId;
            context.ChangeTracker.Clear();

            var currentFuncionario = await SeedFuncionarioAsync(context, "Interno");
            await SeedCobroServicioAsync(context, currentFuncionario, "Servicio Interno", new DateTime(2026, 4, 23, 9, 0, 0), 120m, "SINPE", "Cliente Interno");

            var queryService = new CobroQueryService(context);
            var result = await queryService.BuildIndexViewModelAsync(new CobroFiltroViewModel { VistaTiempo = "todo" }, includeFilterOptions: false);

            var row = Assert.Single(result.Cobros);
            Assert.Equal("Cliente Interno", row.NombreCliente);
            Assert.Equal(120m, result.TotalGenerado);
        }

        private static DateTime StartOfWeek(DateTime today)
        {
            var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            return today.AddDays(-diff).Date;
        }

        private static async Task<Funcionario> SeedFuncionarioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            decimal porcentajeGanancia = 40m,
            decimal porcentajeProducto = 10m)
        {
            var puesto = new Puesto
            {
                NombrePuesto = $"Puesto {Guid.NewGuid():N}",
                Detalle = "Operativo",
                Activo = true
            };

            context.Puestos.Add(puesto);
            await context.SaveChangesAsync();

            var funcionario = new Funcionario
            {
                Nombre = nombre,
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#333333",
                PorcentajeGanancia = porcentajeGanancia,
                PorcentajeProducto = porcentajeProducto,
                FechaIngreso = new DateTime(2026, 4, 1),
                Activo = true
            };

            context.Funcionarios.Add(funcionario);
            await context.SaveChangesAsync();
            return funcionario;
        }

        private static async Task SeedCobroServicioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Funcionario funcionario,
            string servicioNombre,
            DateTime fechaCobro,
            decimal monto,
            string metodoPago,
            string cliente)
        {
            var servicio = new Servicio
            {
                Nombre = servicioNombre,
                Precio = monto,
                DuracionMinutos = 45,
                Activo = true
            };

            context.Servicios.Add(servicio);
            await context.SaveChangesAsync();

            context.Cobros.Add(new Cobro
            {
                FechaCobro = fechaCobro,
                NombreCliente = cliente,
                FuncionarioId = funcionario.IdFuncionario,
                ServicioId = servicio.Id,
                Monto = monto,
                MetodoPago = metodoPago
            });

            await context.SaveChangesAsync();
        }

        private static async Task SeedCobroProductoAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            Funcionario funcionario,
            string productoNombre,
            DateTime fechaCobro,
            decimal monto,
            string metodoPago,
            string cliente)
        {
            var producto = new Producto
            {
                NombreProducto = productoNombre,
                PrecioProducto = monto,
                CantidadProducto = 5,
                Activo = true,
                FechaRegistro = new DateTime(2026, 4, 1)
            };

            context.Productos.Add(producto);
            await context.SaveChangesAsync();

            context.Cobros.Add(new Cobro
            {
                FechaCobro = fechaCobro,
                NombreCliente = cliente,
                FuncionarioId = funcionario.IdFuncionario,
                ProductoId = producto.IdProducto,
                Monto = monto,
                MetodoPago = metodoPago
            });

            await context.SaveChangesAsync();
        }
    }
}
