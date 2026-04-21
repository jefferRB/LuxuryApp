using LuxuryApp.Controllers.Calendar;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class CalendarControllerTests
    {
        [Fact]
        public async Task Create_ShouldRejectMissingFuncionarioId()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var controller = new CalendarController(context);

            var result = await controller.Create(new CitaCreateVM
            {
                Tipo = "DESCANSO",
                FechaHoraCita = new DateTime(2026, 4, 16, 9, 0, 0),
                FuncionarioId = 0,
                DuracionMinutos = 30
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("funcionario", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await context.Citas.ToListAsync());
        }

        [Fact]
        public async Task Create_ShouldRejectFuncionarioOutsideCurrentTenant()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantB };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Estilista B",
                Detalle = "Tenant B"
            });
            await context.SaveChangesAsync();

            var puestoB = await context.Puestos.SingleAsync();

            context.Funcionarios.Add(new Funcionario
            {
                Nombre = "Funcionario Tenant B",
                IdPuesto = puestoB.IdPuesto,
                ColorCalendario = "#123456",
                PorcentajeGanancia = 40,
                PorcentajeProducto = 10,
                FechaIngreso = new DateTime(2026, 4, 1),
                Activo = true
            });
            await context.SaveChangesAsync();

            var foreignFuncionarioId = await context.Funcionarios
                .IgnoreQueryFilters()
                .Where(f => f.TenantId == tenantB)
                .Select(f => f.IdFuncionario)
                .SingleAsync();

            tenantProvider.TenantId = tenantA;
            context.ChangeTracker.Clear();

            var controller = new CalendarController(context);

            var result = await controller.Create(new CitaCreateVM
            {
                Tipo = "DESCANSO",
                FechaHoraCita = new DateTime(2026, 4, 16, 10, 0, 0),
                FuncionarioId = foreignFuncionarioId,
                DuracionMinutos = 30
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("tenant actual", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await context.Citas.ToListAsync());
        }

        [Fact]
        public async Task Create_ShouldPersistDescanso_WhenFuncionarioBelongsToCurrentTenant()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Puestos.Add(new Puesto
            {
                NombrePuesto = "Recepción",
                Detalle = "Mostrador"
            });
            await context.SaveChangesAsync();

            var puesto = await context.Puestos.SingleAsync();

            context.Funcionarios.Add(new Funcionario
            {
                Nombre = "Funcionario Activo",
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#654321",
                PorcentajeGanancia = 35,
                PorcentajeProducto = 8,
                FechaIngreso = new DateTime(2026, 4, 1),
                Activo = true
            });
            await context.SaveChangesAsync();

            var funcionario = await context.Funcionarios.SingleAsync();
            var controller = new CalendarController(context);

            var result = await controller.Create(new CitaCreateVM
            {
                Tipo = "DESCANSO",
                FechaHoraCita = new DateTime(2026, 4, 16, 11, 0, 0),
                FuncionarioId = funcionario.IdFuncionario,
                DuracionMinutos = 45
            });

            Assert.IsType<OkObjectResult>(result);

            var cita = await context.Citas.SingleAsync();
            Assert.Equal(funcionario.IdFuncionario, cita.FuncionarioId);
            Assert.Equal("DESCANSO", cita.Tipo);
            Assert.Equal(45, cita.DuracionMinutos);
        }
    }
}
