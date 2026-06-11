using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Tests.Support;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class WhatsAppInboxServiceTests
    {
        private static readonly DateTime AppointmentDate = new(2026, 4, 24, 9, 0, 0);

        [Fact]
        public async Task GetInboxAsync_ShouldReturnOnlyCurrentTenantAppointments()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantA };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionarioA = await SeedFuncionarioAsync(context, "Ana");
            var servicioA = await SeedServicioAsync(context, "Corte", 45);
            await SeedCitaAsync(context, funcionarioA.IdFuncionario, AppointmentDate, servicioA.Id, consent: true);

            tenantProvider.TenantId = tenantB;
            context.ChangeTracker.Clear();

            var funcionarioB = await SeedFuncionarioAsync(context, "Externa");
            var servicioB = await SeedServicioAsync(context, "Color", 60);
            await SeedCitaAsync(context, funcionarioB.IdFuncionario, AppointmentDate.AddHours(1), servicioB.Id, consent: true);

            tenantProvider.TenantId = tenantA;
            context.ChangeTracker.Clear();

            var service = new WhatsAppInboxService(context, ControllerTestSupport.BusinessDateTimeProvider);

            var inbox = await service.GetInboxAsync(AppointmentDate, funcionarioId: null, whatsAppEnabled: true);

            Assert.Single(inbox.Items);
            Assert.Equal("Ana", inbox.Items[0].FuncionarioNombre);
        }

        [Fact]
        public async Task GetInboxAsync_ShouldMarkConsentedAppointmentAsSendable()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Sofia");
            var servicio = await SeedServicioAsync(context, "Corte", 30);
            await SeedCitaAsync(context, funcionario.IdFuncionario, AppointmentDate, servicio.Id, consent: true);

            var service = new WhatsAppInboxService(context, ControllerTestSupport.BusinessDateTimeProvider);

            var inbox = await service.GetInboxAsync(AppointmentDate, funcionarioId: null, whatsAppEnabled: true);

            var item = Assert.Single(inbox.Items);
            Assert.Equal("not_sent", item.WaStatusKey);
            Assert.True(item.PuedeEnviar);
            Assert.False(item.PuedeReenviar);
        }

        [Fact]
        public async Task GetInboxAsync_ShouldNotAllowSendingWithoutConsent()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Sofia");
            var servicio = await SeedServicioAsync(context, "Corte", 30);
            await SeedCitaAsync(context, funcionario.IdFuncionario, AppointmentDate, servicio.Id, consent: false);

            var service = new WhatsAppInboxService(context, ControllerTestSupport.BusinessDateTimeProvider);

            var inbox = await service.GetInboxAsync(AppointmentDate, funcionarioId: null, whatsAppEnabled: true);

            var item = Assert.Single(inbox.Items);
            Assert.Equal("no_consent", item.WaStatusKey);
            Assert.False(item.PuedeEnviar);
        }

        [Fact]
        public async Task GetInboxAsync_ShouldDisableActionsWhenWhatsAppDisabledForTenant()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var funcionario = await SeedFuncionarioAsync(context, "Sofia");
            var servicio = await SeedServicioAsync(context, "Corte", 30);
            await SeedCitaAsync(context, funcionario.IdFuncionario, AppointmentDate, servicio.Id, consent: true);

            var service = new WhatsAppInboxService(context, ControllerTestSupport.BusinessDateTimeProvider);

            var inbox = await service.GetInboxAsync(AppointmentDate, funcionarioId: null, whatsAppEnabled: false);

            var item = Assert.Single(inbox.Items);
            Assert.False(inbox.WhatsAppEnabled);
            Assert.False(item.PuedeEnviar);
            Assert.False(item.PuedeReenviar);
        }

        [Fact]
        public async Task GetCitaChatAsync_ShouldReturnNullForAnotherTenantCita()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantB };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var foreignFuncionario = await SeedFuncionarioAsync(context, "Privado");
            var foreignServicio = await SeedServicioAsync(context, "Servicio Privado", 30);
            var foreignCita = await SeedCitaAsync(context, foreignFuncionario.IdFuncionario, AppointmentDate, foreignServicio.Id, consent: true);

            tenantProvider.TenantId = tenantA;
            context.ChangeTracker.Clear();

            var service = new WhatsAppInboxService(context, ControllerTestSupport.BusinessDateTimeProvider);

            var chat = await service.GetCitaChatAsync(foreignCita.Id);

            Assert.Null(chat);
        }

        private static async Task<Funcionario> SeedFuncionarioAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            string nombre)
        {
            var puesto = new Puesto
            {
                NombrePuesto = $"Puesto {Guid.NewGuid():N}",
                Detalle = "Calendario",
                Activo = true
            };

            context.Puestos.Add(puesto);
            await context.SaveChangesAsync();

            var funcionario = new Funcionario
            {
                Nombre = nombre,
                IdPuesto = puesto.IdPuesto,
                ColorCalendario = "#654321",
                PorcentajeGanancia = 35m,
                PorcentajeProducto = 8m,
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
            int duracionMinutos)
        {
            var servicio = new Servicio
            {
                Nombre = nombre,
                Precio = 30m,
                DuracionMinutos = duracionMinutos,
                Activo = true
            };

            context.Servicios.Add(servicio);
            await context.SaveChangesAsync();
            return servicio;
        }

        private static async Task<Cita> SeedCitaAsync(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            int funcionarioId,
            DateTime fechaHora,
            int servicioId,
            bool consent)
        {
            var cita = new Cita
            {
                NombreCliente = $"Cliente {Guid.NewGuid():N}",
                TelefonoCliente = "70000000",
                ServicioId = servicioId,
                FechaHoraCita = fechaHora,
                FuncionarioId = funcionarioId,
                Tipo = "CITA",
                WhatsAppConsentAtCreation = consent,
                WhatsAppConsentSource = consent ? WhatsAppConsentSources.CitaManual : WhatsAppConsentSources.SinConsentimiento
            };

            context.Citas.Add(cita);
            await context.SaveChangesAsync();
            return cita;
        }
    }
}
