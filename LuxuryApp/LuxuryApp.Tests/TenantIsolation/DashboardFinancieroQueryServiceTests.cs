using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Productos;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class DashboardFinancieroQueryServiceTests
    {
        [Fact]
        public async Task BuildViewModelAsync_ShouldCalculateMonthlyKpis_MethodBreakdown_AndOperationalMetrics()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Ana", 50m, 10m);
            var servicioAbril = await SeedServicioAsync(context, "Corte Abril", 100m);
            var servicioFinMes = await SeedServicioAsync(context, "Peinado Abril", 50m);
            var servicioMayo = await SeedServicioAsync(context, "Corte Mayo", 300m);
            var productoVendido = await SeedProductoAsync(context, "Shampoo Vendido", 200m, 0, activo: false);

            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 1, 0, 0, 0), 100m, "EFECTIVO", "Cliente Servicio", servicioId: servicioAbril.Id);
            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 10, 14, 0, 0), 200m, "TARJETA", "Cliente Producto", productoId: productoVendido.IdProducto);
            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 30, 23, 59, 0), 50m, "SINPE", "Cliente Cierre", servicioId: servicioFinMes.Id);
            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 5, 1, 0, 0, 0), 300m, "EFECTIVO", "Cliente Mayo", servicioId: servicioMayo.Id);

            await SeedClienteAsync(context, "Cliente Uno", "11111111");
            await SeedClienteAsync(context, "Cliente Dos", "22222222");

            await SeedCitaAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 4, 9, 0, 0));
            await SeedCitaAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 30, 23, 59, 0));
            await SeedCitaAsync(context, funcionario.IdFuncionario, new DateTime(2026, 5, 1, 0, 0, 0));

            await SeedProductoAsync(context, "Inventario A", 10m, 3, activo: true);
            await SeedProductoAsync(context, "Inventario B", 5m, 2, activo: true);
            await SeedProductoAsync(context, "Inventario Inactivo", 100m, 1, activo: false);

            var categoriaOperativa = await SeedCategoriaAsync(context, "Operativo", "Gastos operativos");
            await SeedEgresoAsync(context, categoriaOperativa.Id, new DateTime(2026, 4, 20, 8, 0, 0), 40m, "EFECTIVO", "Pago de caja");
            await SeedEgresoAsync(context, categoriaOperativa.Id, new DateTime(2026, 5, 1, 0, 0, 0), 100m, "EFECTIVO", "Pago mayo");

            var service = ControllerTestSupport.CreateDashboardFinancieroQueryService(context);
            var model = await service.BuildViewModelAsync(4, 2026);

            Assert.Equal(150m, model.TotalServicios);
            Assert.Equal(200m, model.TotalProductos);
            Assert.Equal(350m, model.TotalGenerado);
            // IVA incluido: base = 350 / 1.13 = 309.73; IVA = 40.27.
            Assert.Equal(40.27m, model.TotalImpuestos);
            Assert.Equal(309.73m, model.TotalSinImpuestos);
            Assert.Equal(40m, model.TotalEgresos);
            Assert.Equal(40m, model.TotalEgresosAnaliticos);
            Assert.Equal(100m, model.IngresosEfectivo);
            Assert.Equal(50m, model.IngresosSinpe);
            Assert.Equal(200m, model.IngresosTarjeta);
            Assert.Equal(2, model.CantidadClientes);
            Assert.Equal(2, model.CantidadCitasMes);
            Assert.Equal(40m, model.ValorInventarioProductos);
            Assert.Equal(2, model.TotalProductosInventario);
            Assert.Equal(269.73m, model.ResultadoAnalitico);
            Assert.Equal(12, model.ResultadoAnaliticoPorMes.Count);
            Assert.Equal(269.73m, model.ResultadoAnaliticoPorMes[3]);
        }

        [Fact]
        public async Task BuildViewModelAsync_ShouldBuildAnnualSeries_AndCombineLegacyAndNewFuncionarioPayments()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Mario", 50m, 0m);
            var servicioAbril1 = await SeedServicioAsync(context, "Servicio Abril 1", 100m);
            var servicioAbril2 = await SeedServicioAsync(context, "Servicio Abril 2", 100m);
            var servicioMayo1 = await SeedServicioAsync(context, "Servicio Mayo 1", 100m);
            var servicioMayo2 = await SeedServicioAsync(context, "Servicio Mayo 2", 100m);

            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 5, 9, 0, 0), 100m, "EFECTIVO", "Abril Uno", servicioId: servicioAbril1.Id);
            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 30, 10, 0, 0), 100m, "EFECTIVO", "Abril Dos", servicioId: servicioAbril2.Id);
            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 5, 1, 11, 0, 0), 100m, "EFECTIVO", "Mayo Uno", servicioId: servicioMayo1.Id);
            await SeedCobroAsync(context, funcionario.IdFuncionario, new DateTime(2026, 5, 5, 12, 0, 0), 100m, "TARJETA", "Mayo Dos", servicioId: servicioMayo2.Id);

            var categoriaPagoFuncionarios = await SeedCategoriaAsync(
                context,
                LiquidacionSemanalDefaults.CategoriaPagoFuncionarios,
                "Categoria automatica");

            var categoriaOperativa = await SeedCategoriaAsync(context, "Operativo", "Otros egresos");

            var egresoLiquidacion = await SeedEgresoAsync(
                context,
                categoriaPagoFuncionarios.Id,
                new DateTime(2026, 4, 20, 9, 0, 0),
                80m,
                "SINPE",
                "Pago funcionarios abril");

            var liquidacion = new LiquidacionSemanal
            {
                SemanaInicio = new DateTime(2026, 4, 13),
                SemanaFin = new DateTime(2026, 4, 19),
                FechaPago = new DateTime(2026, 4, 20, 9, 0, 0),
                MontoTotal = 80m,
                Estado = LiquidacionSemanalDefaults.EstadoPagada,
                EgresoId = egresoLiquidacion.IdEgreso,
                FechaCreacion = new DateTime(2026, 4, 20, 9, 0, 0)
            };
            context.LiquidacionesSemanales.Add(liquidacion);
            await context.SaveChangesAsync();

            context.LiquidacionesSemanalesDistribucionMensual.AddRange(
                new LiquidacionSemanalDistribucionMensual
                {
                    LiquidacionSemanalId = liquidacion.Id,
                    Anio = 2026,
                    Mes = 4,
                    MontoAsignado = 30m,
                    DiasAplicados = 1
                },
                new LiquidacionSemanalDistribucionMensual
                {
                    LiquidacionSemanalId = liquidacion.Id,
                    Anio = 2026,
                    Mes = 5,
                    MontoAsignado = 50m,
                    DiasAplicados = 1
                });

            context.PagosFuncionarios.Add(new PagoFuncionario
            {
                FuncionarioId = funcionario.IdFuncionario,
                MontoPagado = 87m,
                FechaPago = new DateTime(2026, 5, 4, 8, 0, 0),
                InicioSemana = new DateTime(2026, 4, 28),
                FinSemana = new DateTime(2026, 5, 4),
                Observacion = "Pago legacy"
            });

            await SeedEgresoAsync(context, categoriaOperativa.Id, new DateTime(2026, 4, 22, 10, 0, 0), 20m, "EFECTIVO", "Alquiler abril");
            await SeedEgresoAsync(context, categoriaOperativa.Id, new DateTime(2026, 5, 22, 10, 0, 0), 30m, "EFECTIVO", "Alquiler mayo");
            await context.SaveChangesAsync();

            var service = ControllerTestSupport.CreateDashboardFinancieroQueryService(context);
            var model = await service.BuildViewModelAsync(4, 2026);

            Assert.Equal(200m, model.TotalServicios);
            Assert.Equal(200m, model.TotalGenerado);
            // IVA incluido: base = 200 / 1.13 = 176.99; IVA = 23.01.
            Assert.Equal(23.01m, model.TotalImpuestos);
            Assert.Equal(176.99m, model.TotalSinImpuestos);
            Assert.Equal(80m, model.TotalPagadoFuncionarios);
            Assert.Equal(73.50m, model.TotalPagadoFuncionariosAnalitico);
            Assert.Equal(100m, model.TotalEgresos);
            Assert.Equal(93.50m, model.TotalEgresosAnaliticos);
            Assert.Equal(83.49m, model.ResultadoAnalitico);
            Assert.Equal(76.99m, model.GananciaPorMes[3]);
            Assert.Equal(83.49m, model.ResultadoAnaliticoPorMes[3]);
            Assert.Equal(146.99m, model.GananciaPorMes[4]);
            Assert.Equal(53.49m, model.ResultadoAnaliticoPorMes[4]);
            Assert.Equal(12, model.GananciaPorMes.Count);
            Assert.Equal(12, model.ResultadoAnaliticoPorMes.Count);
        }

        [Fact]
        public async Task BuildViewModelAsync_ShouldRespectTenantIsolation()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantA };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionarioA = await SeedFuncionarioAsync(context, "Ana", 50m, 0m);
            var servicioA = await SeedServicioAsync(context, "Servicio A", 120m);
            await SeedCobroAsync(context, funcionarioA.IdFuncionario, new DateTime(2026, 4, 10, 9, 0, 0), 120m, "SINPE", "Tenant A", servicioId: servicioA.Id);
            var categoriaA = await SeedCategoriaAsync(context, "Operativo A", "Tenant A");
            await SeedEgresoAsync(context, categoriaA.Id, new DateTime(2026, 4, 10, 10, 0, 0), 20m, "EFECTIVO", "Egreso A");
            await SeedClienteAsync(context, "Cliente A", "30000001");

            tenantProvider.TenantId = tenantB;
            context.ChangeTracker.Clear();

            var funcionarioB = await SeedFuncionarioAsync(context, "Beto", 50m, 0m);
            var servicioB = await SeedServicioAsync(context, "Servicio B", 999m);
            await SeedCobroAsync(context, funcionarioB.IdFuncionario, new DateTime(2026, 4, 10, 9, 0, 0), 999m, "EFECTIVO", "Tenant B", servicioId: servicioB.Id);
            var categoriaB = await SeedCategoriaAsync(context, "Operativo B", "Tenant B");
            await SeedEgresoAsync(context, categoriaB.Id, new DateTime(2026, 4, 10, 10, 0, 0), 300m, "EFECTIVO", "Egreso B");
            await SeedClienteAsync(context, "Cliente B", "30000002");

            tenantProvider.TenantId = tenantA;
            context.ChangeTracker.Clear();

            var service = ControllerTestSupport.CreateDashboardFinancieroQueryService(context);
            var model = await service.BuildViewModelAsync(4, 2026);

            Assert.Equal(120m, model.TotalServicios);
            Assert.Equal(120m, model.TotalGenerado);
            Assert.Equal(20m, model.TotalEgresos);
            Assert.Equal(1, model.CantidadClientes);
            Assert.Equal(120m, model.IngresosSinpe);
            Assert.Equal(0m, model.IngresosEfectivo);
        }

        private static async Task<Funcionario> SeedFuncionarioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            decimal porcentajeGanancia,
            decimal porcentajeProducto)
        {
            var puesto = new Puesto
            {
                NombrePuesto = $"Puesto {nombre} {Guid.NewGuid():N}",
                Detalle = "Operativo",
                Activo = true
            };

            context.Puestos.Add(puesto);
            await context.SaveChangesAsync();

            var funcionario = new Funcionario
            {
                Nombre = nombre,
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#123456",
                PorcentajeGanancia = porcentajeGanancia,
                PorcentajeProducto = porcentajeProducto,
                FechaIngreso = new DateTime(2026, 4, 1),
                Activo = true
            };

            context.Funcionarios.Add(funcionario);
            await context.SaveChangesAsync();
            return funcionario;
        }

        private static async Task<Servicio> SeedServicioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            decimal precio)
        {
            var servicio = new Servicio
            {
                Nombre = nombre,
                Precio = precio,
                DuracionMinutos = 45,
                Activo = true
            };

            context.Servicios.Add(servicio);
            await context.SaveChangesAsync();
            return servicio;
        }

        private static async Task<Producto> SeedProductoAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            decimal precio,
            int stock,
            bool activo)
        {
            var producto = new Producto
            {
                NombreProducto = nombre,
                PrecioProducto = precio,
                CantidadProducto = stock,
                Activo = activo,
                FechaRegistro = new DateTime(2026, 4, 1)
            };

            context.Productos.Add(producto);
            await context.SaveChangesAsync();
            return producto;
        }

        private static async Task SeedCobroAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            int funcionarioId,
            DateTime fechaCobro,
            decimal monto,
            string metodoPago,
            string nombreCliente,
            int? servicioId = null,
            int? productoId = null)
        {
            context.Cobros.Add(new Cobro
            {
                FechaCobro = fechaCobro,
                NombreCliente = nombreCliente,
                FuncionarioId = funcionarioId,
                ServicioId = servicioId,
                ProductoId = productoId,
                Monto = monto,
                MetodoPago = metodoPago
            });

            await context.SaveChangesAsync();
        }

        private static async Task SeedClienteAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            string telefono)
        {
            context.Clientes.Add(new ClientesModel
            {
                Nombre = nombre,
                NumeroTelefono = telefono,
                CorreoElectronico = $"{telefono}@test.local",
                FechaUltimaVisita = new DateTime(2026, 4, 1),
                FrecuenciaVisita = 30
            });

            await context.SaveChangesAsync();
        }

        private static async Task SeedCitaAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            int funcionarioId,
            DateTime fechaHora)
        {
            context.Citas.Add(new Cita
            {
                FuncionarioId = funcionarioId,
                FechaHoraCita = fechaHora,
                NombreCliente = "Cliente cita",
                TelefonoCliente = "88888888",
                Tipo = "CITA"
            });

            await context.SaveChangesAsync();
        }

        private static async Task<Categoria> SeedCategoriaAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            string detalle)
        {
            var categoria = new Categoria
            {
                Nombre = nombre,
                Detalle = detalle,
                Activo = true
            };

            context.Categorias.Add(categoria);
            await context.SaveChangesAsync();
            return categoria;
        }

        private static async Task<Egreso> SeedEgresoAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            int categoriaId,
            DateTime fechaEgreso,
            decimal monto,
            string metodoPago,
            string detalle)
        {
            var egreso = new Egreso
            {
                CategoriaId = categoriaId,
                FechaEgreso = fechaEgreso,
                Monto = monto,
                MetodoPago = metodoPago,
                Detalle = detalle
            };

            context.Egresos.Add(egreso);
            await context.SaveChangesAsync();
            return egreso;
        }
    }
}
