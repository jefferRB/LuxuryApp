using LuxuryApp.Controllers.Finanzas;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class LiquidacionSemanalServiceTests
    {
        [Fact]
        public async Task RegistrarPagoAsync_ShouldCreateCategoriaEgresoLiquidacionAndDistribution_FromRealCobrosByMonth()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Ana", 50m, 15m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 28), 700m);

            var service = CreateService(context);

            var liquidacionId = await service.RegistrarPagoAsync(new RegistrarLiquidacionSemanalCommand
            {
                SemanaInicio = new DateTime(2026, 4, 27),
                SemanaFin = new DateTime(2026, 5, 3),
                FechaPago = new DateTime(2026, 5, 3, 9, 15, 0),
                MetodoPago = "SINPE",
                Observacion = "Pago parcial",
                CreadoPor = "user-test",
                Detalles =
                {
                    new RegistrarLiquidacionSemanalDetalleCommand
                    {
                        FuncionarioId = funcionario.IdFuncionario,
                        MontoPagado = 100m
                    }
                }
            });

            var categoria = await context.Categorias.SingleAsync();
            Assert.Equal(LiquidacionSemanalDefaults.CategoriaPagoFuncionarios, categoria.Nombre);
            Assert.True(categoria.Activo);

            var egreso = await context.Egresos.SingleAsync();
            Assert.Equal(100m, egreso.Monto);
            Assert.Equal("SINPE", egreso.MetodoPago);

            var liquidacion = await context.LiquidacionesSemanales
                .SingleAsync(l => l.Id == liquidacionId);

            Assert.Equal(egreso.IdEgreso, liquidacion.EgresoId);
            Assert.Equal(new DateTime(2026, 4, 27), liquidacion.SemanaInicio);
            Assert.Equal(new DateTime(2026, 5, 3), liquidacion.SemanaFin);
            Assert.Equal(100m, liquidacion.MontoTotal);

            var detalle = await context.LiquidacionesSemanalesDetalle.SingleAsync();
            Assert.Equal(funcionario.IdFuncionario, detalle.FuncionarioId);
            Assert.Equal(100m, detalle.MontoPagado);
            Assert.Equal(700m, detalle.MontoServicios);
            Assert.Equal(0m, detalle.MontoProductos);
            Assert.Equal(91m, detalle.Impuestos);
            Assert.Equal(609m, detalle.MontoNeto);
            Assert.Equal(204.5m, detalle.Pendiente);

            var distribuciones = await context.LiquidacionesSemanalesDistribucionMensual
                .OrderBy(d => d.Mes)
                .ToListAsync();

            Assert.Collection(
                distribuciones,
                abril =>
                {
                    Assert.Equal(2026, abril.Anio);
                    Assert.Equal(4, abril.Mes);
                    Assert.Equal(100m, abril.MontoAsignado);
                    Assert.Equal(1, abril.DiasAplicados);
                });
        }

        [Fact]
        public async Task RegistrarPagoAsync_ShouldReuseExistingCategoriaWithoutDuplicatingRows()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Categorias.Add(new Categoria
            {
                Nombre = LiquidacionSemanalDefaults.CategoriaPagoFuncionarios,
                Detalle = "Creada antes",
                Activo = false
            });
            await context.SaveChangesAsync();

            var funcionario = await SeedFuncionarioAsync(context, "Luis", 40m, 10m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 14), 500m);

            var service = CreateService(context);

            await service.RegistrarPagoAsync(new RegistrarLiquidacionSemanalCommand
            {
                SemanaInicio = new DateTime(2026, 4, 13),
                SemanaFin = new DateTime(2026, 4, 19),
                MetodoPago = "EFECTIVO",
                Detalles =
                {
                    new RegistrarLiquidacionSemanalDetalleCommand
                    {
                        FuncionarioId = funcionario.IdFuncionario,
                        MontoPagado = 50m
                    }
                }
            });

            var categorias = await context.Categorias
                .Where(c => c.Nombre == LiquidacionSemanalDefaults.CategoriaPagoFuncionarios)
                .ToListAsync();

            Assert.Single(categorias);
            Assert.True(categorias[0].Activo);
        }

        [Fact]
        public async Task RegistrarPagoAsync_ShouldRejectWhenAmountExceedsPending()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Paola", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 15), 100m);

            var service = CreateService(context);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegistrarPagoAsync(
                new RegistrarLiquidacionSemanalCommand
                {
                    SemanaInicio = new DateTime(2026, 4, 13),
                    SemanaFin = new DateTime(2026, 4, 19),
                    MetodoPago = "TARJETA",
                    Detalles =
                    {
                        new RegistrarLiquidacionSemanalDetalleCommand
                        {
                            FuncionarioId = funcionario.IdFuncionario,
                            MontoPagado = 60m
                        }
                    }
                }));

            Assert.Contains("excede el pendiente", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await context.Egresos.ToListAsync());
            Assert.Empty(await context.LiquidacionesSemanales.ToListAsync());
        }

        [Fact]
        public async Task Dashboard_ShouldSeparateCashFlowFromAnalyticalDistribution()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Mario", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 29), 700m);

            context.Categorias.Add(new Categoria
            {
                Nombre = "Alquiler",
                Detalle = "Local comercial"
            });
            await context.SaveChangesAsync();

            var categoriaAlquiler = await context.Categorias.SingleAsync(c => c.Nombre == "Alquiler");
            context.Egresos.Add(new Egreso
            {
                FechaEgreso = new DateTime(2026, 4, 20, 8, 0, 0),
                CategoriaId = categoriaAlquiler.Id,
                Monto = 50m,
                MetodoPago = "EFECTIVO",
                Detalle = "Pago de alquiler"
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            await service.RegistrarPagoAsync(new RegistrarLiquidacionSemanalCommand
            {
                SemanaInicio = new DateTime(2026, 4, 27),
                SemanaFin = new DateTime(2026, 5, 3),
                FechaPago = new DateTime(2026, 5, 3, 10, 0, 0),
                MetodoPago = "SINPE",
                Detalles =
                {
                    new RegistrarLiquidacionSemanalDetalleCommand
                    {
                        FuncionarioId = funcionario.IdFuncionario,
                        MontoPagado = 100m
                    }
                }
            });

            var controller = new DashboardController(
                ControllerTestSupport.CreateDashboardFinancieroQueryService(context));
            var result = await controller.Index(4, 2026);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<DashboardViewModel>(view.Model);

            Assert.Equal(0m, model.TotalPagadoFuncionarios);
            Assert.Equal(100m, model.TotalPagadoFuncionariosAnalitico);
            Assert.Equal(50m, model.TotalEgresos);
            Assert.Equal(150m, model.TotalEgresosAnaliticos);
        }

        [Fact]
        public async Task Dashboard_ShouldAllocateLegacyPaymentsUsingRealCobroMonths_NotCalendarDays()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Luisa", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 3, 30), 100m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 1), 100m);

            context.PagosFuncionarios.Add(new PagoFuncionario
            {
                FuncionarioId = funcionario.IdFuncionario,
                MontoPagado = 87m,
                FechaPago = new DateTime(2026, 4, 5, 9, 0, 0),
                InicioSemana = new DateTime(2026, 3, 30),
                FinSemana = new DateTime(2026, 4, 5),
                Observacion = "Pago legacy"
            });
            await context.SaveChangesAsync();

            var controller = new DashboardController(
                ControllerTestSupport.CreateDashboardFinancieroQueryService(context));
            var result = await controller.Index(4, 2026);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<DashboardViewModel>(view.Model);

            Assert.Equal(43.50m, model.TotalPagadoFuncionariosAnalitico);
        }

        [Fact]
        public async Task CobrosIndex_ShouldIncludeInactiveFuncionarioPaymentsInDevengadoView()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Inactiva", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 10), 100m);

            funcionario.Activo = false;
            await context.SaveChangesAsync();

            var controller = new CobrosController(
                ControllerTestSupport.CreateCobroService(context),
                ControllerTestSupport.CreateCobroQueryService(context),
                ControllerTestSupport.CreateComprobanteCobroService(),
                ControllerTestSupport.BusinessDateTimeProvider,
                ControllerTestSupport.CreateTenantDisplayNameService());
            var result = await controller.Index(new CobroFiltroViewModel { VistaTiempo = "todo" });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CobroIndexViewModel>(view.Model);

            Assert.Equal(43.50m, model.PagoColaboradores);
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_ShouldCombineLegacyAndLiquidationPayments_WithoutDoubleCounting()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Ana", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 14), 200m);

            context.PagosFuncionarios.Add(new PagoFuncionario
            {
                FuncionarioId = funcionario.IdFuncionario,
                MontoPagado = 20m,
                FechaPago = new DateTime(2026, 4, 18, 8, 0, 0),
                InicioSemana = new DateTime(2026, 4, 13),
                FinSemana = new DateTime(2026, 4, 19),
                Observacion = "Pago legacy"
            });

            context.Categorias.Add(new Categoria
            {
                Nombre = LiquidacionSemanalDefaults.CategoriaPagoFuncionarios,
                Detalle = "Auto"
            });
            await context.SaveChangesAsync();

            var categoria = await context.Categorias.SingleAsync();
            context.Egresos.Add(new Egreso
            {
                FechaEgreso = new DateTime(2026, 4, 19, 9, 0, 0),
                CategoriaId = categoria.Id,
                Monto = 30m,
                MetodoPago = "SINPE",
                Detalle = "Pago semanal"
            });
            await context.SaveChangesAsync();

            var egreso = await context.Egresos.SingleAsync();
            context.LiquidacionesSemanales.Add(new LiquidacionSemanal
            {
                SemanaInicio = new DateTime(2026, 4, 13),
                SemanaFin = new DateTime(2026, 4, 19),
                FechaPago = new DateTime(2026, 4, 19, 9, 0, 0),
                MontoTotal = 30m,
                Estado = LiquidacionSemanalDefaults.EstadoPagada,
                Observacion = "Liquidacion nueva",
                EgresoId = egreso.IdEgreso,
                FechaCreacion = new DateTime(2026, 4, 19, 9, 0, 0)
            });
            await context.SaveChangesAsync();

            var liquidacion = await context.LiquidacionesSemanales.SingleAsync();
            context.LiquidacionesSemanalesDetalle.Add(new LiquidacionSemanalDetalle
            {
                LiquidacionSemanalId = liquidacion.Id,
                FuncionarioId = funcionario.IdFuncionario,
                MontoServicios = 200m,
                MontoProductos = 0m,
                Impuestos = 26m,
                MontoNeto = 174m,
                MontoPagado = 30m,
                Pendiente = 37m
            });
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var resumen = await service.ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal(87m, pago.PagoFinal);
            Assert.Equal(50m, pago.MontoPagado);
            Assert.Equal(37m, pago.MontoPendiente);
            Assert.Equal(2, pago.HistorialPagos.Count);
            Assert.Contains(pago.HistorialPagos, p => p.OrigenRegistro == "LEGACY");
            Assert.Contains(pago.HistorialPagos, p => p.OrigenRegistro == "LIQUIDACION");
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_ShouldRespectTenantIsolation()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantA };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionarioA = await SeedFuncionarioAsync(context, "Ana", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionarioA.IdFuncionario, new DateTime(2026, 4, 14), 100m);

            context.ChangeTracker.Clear();
            tenantProvider.TenantId = tenantB;

            var funcionarioB = await SeedFuncionarioAsync(context, "Beto", 50m, 0m);
            await SeedCobroServicioAsync(context, funcionarioB.IdFuncionario, new DateTime(2026, 4, 14), 300m);

            context.ChangeTracker.Clear();
            tenantProvider.TenantId = tenantA;

            var service = CreateService(context);
            var resumen = await service.ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal("Ana", pago.Nombre);
            Assert.Equal(100m, resumen.TotalGeneradoServicios);
            Assert.Equal(100m, resumen.TotalGeneradoGeneral);
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_WithRebajaImpuestos_ShouldComputePagoOnNetBase()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            // Caso 1: comportamiento histórico (rebaja impuestos). 70000 / 13% / 50% => 30450.
            var funcionario = await SeedFuncionarioAsync(context, "Ana", 50m, 50m, rebajarImpuestos: true);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 14), 70000m);

            var resumen = await CreateService(context).ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal(70000m, pago.TotalGenerado);
            Assert.Equal(9100m, pago.Impuestos);
            Assert.Equal(60900m, pago.TotalNeto);
            Assert.Equal(30450m, pago.PagoFinal);
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_WithoutRebajaImpuestos_ShouldComputePagoOnGross()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            // Caso 2: NO rebaja impuestos. 70000 / 50% => 35000. KPIs del negocio no cambian.
            var funcionario = await SeedFuncionarioAsync(context, "Beto", 50m, 50m, rebajarImpuestos: false);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 14), 70000m);

            var resumen = await CreateService(context).ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal(70000m, pago.TotalGenerado);
            Assert.Equal(9100m, pago.Impuestos);
            Assert.Equal(60900m, pago.TotalNeto);
            Assert.Equal(35000m, pago.PagoFinal);
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_WithZeroPercent_ShouldPayZero()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Cero", 0m, 0m, rebajarImpuestos: false);
            await SeedCobroServicioAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 14), 70000m);

            var resumen = await CreateService(context).ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal(0m, pago.PagoFinal);
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_WithZeroProduction_ShouldPayZero()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            // Funcionario activo sin cobros en la semana => pago 0, sin fallar.
            await SeedFuncionarioAsync(context, "SinProduccion", 50m, 50m, rebajarImpuestos: false);

            var resumen = await CreateService(context).ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal(0m, pago.TotalGenerado);
            Assert.Equal(0m, pago.PagoFinal);
        }

        [Fact]
        public async Task ObtenerResumenSemanaAsync_Productos_ShouldRespectRebajaImpuestosFlag()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            // Comisión por producto: 70000 / 50% sin rebaja => 35000.
            var funcionario = await SeedFuncionarioAsync(context, "ProdSinRebaja", 0m, 50m, rebajarImpuestos: false);
            await SeedCobroProductoAsync(context, funcionario.IdFuncionario, new DateTime(2026, 4, 14), 70000m);

            var resumen = await CreateService(context).ObtenerResumenSemanaAsync(
                new DateTime(2026, 4, 13),
                new DateTime(2026, 4, 19));

            var pago = Assert.Single(resumen.Funcionarios);
            Assert.Equal(70000m, pago.TotalProductos);
            Assert.Equal(35000m, pago.PagoFinal);

            var producto = Assert.Single(pago.ProductosVendidos);
            Assert.Equal(35000m, producto.GananciaFuncionario);
        }

        private static LiquidacionSemanalService CreateService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new(
                context,
                ControllerTestSupport.BusinessDateTimeProvider,
                NullLogger<LiquidacionSemanalService>.Instance);

        private static async Task<Funcionario> SeedFuncionarioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            decimal porcentajeGanancia,
            decimal porcentajeProducto,
            bool rebajarImpuestos = true)
        {
            context.Puestos.Add(new Puesto
            {
                NombrePuesto = $"Puesto {nombre}",
                Detalle = "General",
                Activo = true
            });
            await context.SaveChangesAsync();

            var puesto = await context.Puestos.SingleAsync(p => p.NombrePuesto == $"Puesto {nombre}");

            var funcionario = new Funcionario
            {
                Nombre = nombre,
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#111111",
                PorcentajeGanancia = porcentajeGanancia,
                PorcentajeProducto = porcentajeProducto,
                RebajarImpuestosAntesDeComision = rebajarImpuestos,
                FechaIngreso = new DateTime(2026, 4, 13),
                Activo = true
            };

            context.Funcionarios.Add(funcionario);
            await context.SaveChangesAsync();
            return funcionario;
        }

        private static async Task SeedCobroProductoAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            int funcionarioId,
            DateTime fecha,
            decimal monto)
        {
            var producto = new LuxuryApp.Models.Productos.Producto
            {
                NombreProducto = $"Producto {fecha:yyyyMMddHHmmss}",
                PrecioProducto = monto,
                CantidadProducto = 100,
                Activo = true
            };
            context.Productos.Add(producto);
            await context.SaveChangesAsync();

            context.Cobros.Add(new Cobro
            {
                NombreCliente = "Cliente Test",
                FuncionarioId = funcionarioId,
                FechaCobro = fecha,
                Monto = monto,
                MetodoPago = "EFECTIVO",
                ProductoId = producto.IdProducto
            });

            await context.SaveChangesAsync();
        }

        private static async Task SeedCobroServicioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            int funcionarioId,
            DateTime fecha,
            decimal monto)
        {
            var servicio = new Servicio
            {
                Nombre = $"Servicio {fecha:yyyyMMddHHmmss}",
                Precio = monto,
                DuracionMinutos = 60,
                Activo = true
            };
            context.Servicios.Add(servicio);
            await context.SaveChangesAsync();

            context.Cobros.Add(new Cobro
            {
                NombreCliente = "Cliente Test",
                FuncionarioId = funcionarioId,
                FechaCobro = fecha,
                Monto = monto,
                MetodoPago = "EFECTIVO",
                ServicioId = servicio.Id
            });

            await context.SaveChangesAsync();
        }
    }
}
