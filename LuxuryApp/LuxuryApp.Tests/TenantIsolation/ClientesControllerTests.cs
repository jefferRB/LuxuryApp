using LuxuryApp.Controllers.DataBase;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class ClientesControllerTests
    {
        [Fact]
        public async Task Create_ShouldPersistClientWithOptionalEmail_AndAllowSamePhoneAcrossTenants()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantB };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Clientes.Add(new ClientesModel
            {
                Nombre = "Cliente Tenant B",
                NumeroTelefono = "88889999",
                CorreoElectronico = "tenantb@test.local",
                FrecuenciaVisita = 30,
                FechaUltimaVisita = new DateTime(2026, 4, 1)
            });
            await context.SaveChangesAsync();

            tenantProvider.TenantId = tenantA;
            context.ChangeTracker.Clear();

            var controller = CreateController(context, tenantA);

            var result = await controller.Create(new ClientesModel
            {
                Nombre = "Cliente Tenant A",
                NumeroTelefono = "88889999",
                CorreoElectronico = string.Empty,
                FrecuenciaVisita = 15,
                FechaUltimaVisita = new DateTime(2026, 4, 13)
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(ClientesController.Index), redirect.ActionName);

            var clienteActual = await context.Clientes.SingleAsync(c => c.Nombre == "Cliente Tenant A");
            Assert.Null(clienteActual.CorreoElectronico);

            var visita = await context.ClienteVisitas.SingleAsync(v => v.ClienteId == clienteActual.Id);
            Assert.Equal("88889999", visita.NumeroTelefono);

            var totalConMismoTelefono = await context.Clientes
                .IgnoreQueryFilters()
                .CountAsync(c => c.NumeroTelefono == "88889999");

            Assert.Equal(2, totalConMismoTelefono);
        }

        [Fact]
        public async Task Editar_ShouldAllowPhoneChange_AndUpdateRelatedVisitRows()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var cliente = new ClientesModel
            {
                Nombre = "Cliente Demo",
                NumeroTelefono = "70001111",
                CorreoElectronico = "demo@test.local",
                FrecuenciaVisita = 30,
                FechaUltimaVisita = new DateTime(2026, 4, 1)
            };

            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();

            context.ClienteVisitas.Add(new ClienteVisitas
            {
                ClienteId = cliente.Id,
                NumeroTelefono = cliente.NumeroTelefono,
                FechaVisita = cliente.FechaUltimaVisita
            });

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context, tenantId);

            var result = await controller.Editar(new ClientesModel
            {
                Id = cliente.Id,
                Nombre = "Cliente Demo",
                NumeroTelefono = "79992222",
                CorreoElectronico = string.Empty,
                FrecuenciaVisita = 30,
                FechaUltimaVisita = new DateTime(2026, 4, 1)
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(ClientesController.Buscar), redirect.ActionName);
            Assert.Equal("79992222", redirect.RouteValues?["criterio"]);

            var clienteActualizado = await context.Clientes.SingleAsync(c => c.Id == cliente.Id);
            Assert.Equal("79992222", clienteActualizado.NumeroTelefono);
            Assert.Null(clienteActualizado.CorreoElectronico);

            var visitas = await context.ClienteVisitas
                .Where(v => v.ClienteId == cliente.Id)
                .OrderBy(v => v.Id)
                .ToListAsync();

            Assert.All(visitas, visita => Assert.Equal("79992222", visita.NumeroTelefono));
        }

        [Fact]
        public async Task Index_ShouldReturnPagedOrderedProjection()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Clientes.AddRange(
                new ClientesModel
                {
                    Nombre = "Zoe",
                    NumeroTelefono = "1001",
                    FechaUltimaVisita = new DateTime(2026, 4, 1),
                    FrecuenciaVisita = 30
                },
                new ClientesModel
                {
                    Nombre = "Ana",
                    NumeroTelefono = "1002",
                    FechaUltimaVisita = new DateTime(2026, 4, 2),
                    FrecuenciaVisita = 30
                },
                new ClientesModel
                {
                    Nombre = "Luis",
                    NumeroTelefono = "1003",
                    FechaUltimaVisita = new DateTime(2026, 4, 3),
                    FrecuenciaVisita = 30
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context, tenantId);

            var result = await controller.Index(pageNumber: 2, pageSize: 2);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ClientesIndexViewModel>(view.Model);

            Assert.Equal(3, model.TotalCount);
            Assert.Equal(2, model.PageNumber);
            Assert.Equal(2, model.PageSize);
            Assert.Equal(2, model.TotalPages);

            var cliente = Assert.Single(model.Clientes);
            Assert.Equal("Zoe", cliente.Nombre);
            Assert.Equal("1001", cliente.NumeroTelefono);
        }

        [Fact]
        public async Task RegistrarServicios_ShouldPersistDescriptionOnly()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var cliente = new ClientesModel
            {
                Nombre = "Cliente Servicios",
                NumeroTelefono = "74445555",
                FechaUltimaVisita = new DateTime(2026, 4, 10),
                FrecuenciaVisita = 20
            };

            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context, tenantId);

            var result = await controller.RegistrarServicios(new ServicioRealizadoViewModel
            {
                ClienteId = cliente.Id,
                DescripcionServicios = "Corte premium y perfilado"
            });

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(ClientesController.Buscar), redirect.ActionName);
            Assert.Equal("74445555", redirect.RouteValues?["criterio"]);

            var clienteActualizado = await context.Clientes.SingleAsync(c => c.Id == cliente.Id);
            Assert.Equal("Corte premium y perfilado", clienteActualizado.DescripcionServiciosRealizados);
        }

        [Fact]
        public async Task Buscar_ShouldPrioritizeExactPhoneMatch_WhenCriteriaLooksLikePhone()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Clientes.AddRange(
                new ClientesModel
                {
                    Nombre = "Ana Uno",
                    NumeroTelefono = "88889999",
                    FechaUltimaVisita = new DateTime(2026, 4, 1),
                    FrecuenciaVisita = 30
                },
                new ClientesModel
                {
                    Nombre = "Ana Dos",
                    NumeroTelefono = "77776666",
                    FechaUltimaVisita = new DateTime(2026, 4, 2),
                    FrecuenciaVisita = 30
                });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context, tenantId);

            var result = await controller.Buscar("8888-9999");

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<BuscarClienteViewModel>(view.Model);

            Assert.True(model.EsBusquedaTelefonica);
            Assert.Single(model.ClientesEncontrados);
            Assert.NotNull(model.ClienteSeleccionado);
            Assert.Equal("88889999", model.ClienteSeleccionado!.NumeroTelefono);
        }

        [Fact]
        public async Task Buscar_ShouldLimitNameResults_AndRequireRefinement()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var clientes = Enumerable.Range(1, 51)
                .Select(index => new ClientesModel
                {
                    Nombre = $"Ana {index:00}",
                    NumeroTelefono = $"8000{index:0000}",
                    FechaUltimaVisita = new DateTime(2026, 4, 1),
                    FrecuenciaVisita = 30
                });

            context.Clientes.AddRange(clientes);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var controller = CreateController(context, tenantId);

            var result = await controller.Buscar("Ana");

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<BuscarClienteViewModel>(view.Model);

            Assert.False(model.EsBusquedaTelefonica);
            Assert.True(model.ResultadosLimitados);
            Assert.Equal(50, model.ClientesEncontrados.Count);
            Assert.Contains("refina la búsqueda", model.Mensaje, StringComparison.OrdinalIgnoreCase);
            Assert.Null(model.ClienteSeleccionado);
        }

        [Fact]
        public async Task Autocompletado_ShouldReturnEmpty_WhenTermIsBelowMinimumLength()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var controller = CreateController(context, tenantId);

            var result = await controller.Autocompletado("A");

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
            Assert.Empty(payload);
        }

        private static ClientesController CreateController(ProyectoIdentity.Datos.ApplicationDbContext context, Guid tenantId)
        {
            var controller = new ClientesController(
                context,
                ControllerTestSupport.BusinessDateTimeProvider,
                NullLogger<ClientesController>.Instance);

            ControllerTestSupport.AttachHttpContext(
                controller,
                ControllerTestSupport.BuildTenantPrincipal("user-clientes", tenantId));

            return controller;
        }
    }
}
