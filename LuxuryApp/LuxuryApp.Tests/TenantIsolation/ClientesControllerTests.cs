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
        public async Task Editar_ShouldAllowPhoneChange_AndUpdateRelatedRows()
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

            context.ClienteImagenes.Add(new ClienteImagenesModel
            {
                ClienteId = cliente.Id,
                NumeroTelefono = cliente.NumeroTelefono,
                Imagen = new byte[] { 1, 2, 3 },
                Fecha = new DateTime(2026, 4, 2)
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

            var visita = await context.ClienteVisitas.SingleAsync(v => v.ClienteId == cliente.Id);
            var imagen = await context.ClienteImagenes.SingleAsync(i => i.ClienteId == cliente.Id);

            Assert.Equal("79992222", visita.NumeroTelefono);
            Assert.Equal("79992222", imagen.NumeroTelefono);
        }

        private static ClientesController CreateController(ProyectoIdentity.Datos.ApplicationDbContext context, Guid tenantId)
        {
            var controller = new ClientesController(
                context,
                ControllerTestSupport.CreateRecordatorioService(),
                new FakeWebHostEnvironment(),
                new FakeEmailService(),
                NullLogger<ClientesController>.Instance);

            ControllerTestSupport.AttachHttpContext(
                controller,
                ControllerTestSupport.BuildTenantPrincipal("user-clientes", tenantId));

            return controller;
        }
    }
}
