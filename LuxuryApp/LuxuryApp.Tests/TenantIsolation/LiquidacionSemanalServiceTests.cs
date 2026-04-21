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

            var controller = new DashboardController(context);
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

            var controller = new DashboardController(context);
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

            var controller = new CobrosController(context);
            var result = await controller.Index(new CobroFiltroViewModel { VistaTiempo = "todo" });

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<CobroIndexViewModel>(view.Model);

            Assert.Equal(43.50m, model.PagoColaboradores);
        }

        private static LiquidacionSemanalService CreateService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new(context, NullLogger<LiquidacionSemanalService>.Instance);

        private static async Task<Funcionario> SeedFuncionarioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre,
            decimal porcentajeGanancia,
            decimal porcentajeProducto)
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
                FechaIngreso = new DateTime(2026, 4, 13),
                Activo = true
            };

            context.Funcionarios.Add(funcionario);
            await context.SaveChangesAsync();
            return funcionario;
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
