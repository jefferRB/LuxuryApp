using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class CalendarWhatsAppNotificationServiceTests
    {
        [Fact]
        public async Task QueueConfirmation_WhenTenantHasNoSettings_ShouldSkipAsTenantDisabled()
        {
            using var fixture = await Fixture.CreateAsync();
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.AsNoTracking().SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedTenantDisabled, message.Status);
            Assert.Equal(WhatsAppErrorCodes.TenantDisabled, message.ErrorCode);
        }

        [Fact]
        public async Task QueueConfirmation_WhenTenantIsDisabled_ShouldSkipAsTenantDisabled()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: false);
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedTenantDisabled, message.Status);
            Assert.Equal(0, fixture.MetaClient.SendCount);
            Assert.Equal(0, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
        }

        [Fact]
        public async Task QueueConfirmation_WhenTenantIsEnabled_ShouldCreatePendingOutboundMessage()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.Pending, message.Status);
            Assert.Equal(WhatsAppNotificationTypes.Confirmation, message.NotificationType);
        }

        [Fact]
        public async Task QueueConfirmation_WhenManualConsentIsMissing_ShouldSkipAsConsentMissing_WithoutConsumingDailyLimit()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 1);
            var citaSinConsentimiento = await fixture.SeedCitaAsync(whatsAppConsentAtCreation: false);
            var citaConConsentimiento = await fixture.SeedCitaAsync(phone: "89990000", whatsAppConsentAtCreation: true);

            await fixture.Notifications.QueueAppointmentConfirmationAsync(citaSinConsentimiento.Id);
            await fixture.Notifications.QueueAppointmentConfirmationAsync(citaConConsentimiento.Id);

            var messages = await fixture.Context.WhatsAppMessageLogs
                .OrderBy(message => message.Id)
                .ToListAsync();

            Assert.Equal(2, messages.Count);
            Assert.Equal(WhatsAppMessageStatuses.SkippedConsentMissing, messages[0].Status);
            Assert.Equal(WhatsAppErrorCodes.ConsentMissing, messages[0].ErrorCode);
            Assert.Equal(WhatsAppMessageStatuses.Pending, messages[1].Status);
            Assert.Equal(0, fixture.MetaClient.SendCount);
        }

        [Fact]
        public async Task QueueConfirmation_WhenRegisteredClientHasConsentFalse_ShouldSkipAsConsentMissing()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cliente = await fixture.SeedClienteAsync("Cliente sin consentimiento", "81112222", aceptaMensajesWhatsApp: false);
            var cita = await fixture.SeedCitaAsync(clienteId: cliente.Id, whatsAppConsentAtCreation: true);

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedConsentMissing, message.Status);
            Assert.Equal(WhatsAppErrorCodes.ConsentMissing, message.ErrorCode);
        }

        [Fact]
        public async Task QueueConfirmation_WhenRegisteredClientHasConsentTrue_ShouldCreatePendingOutboundMessage()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cliente = await fixture.SeedClienteAsync("Cliente con consentimiento", "82223333", aceptaMensajesWhatsApp: true);
            var cita = await fixture.SeedCitaAsync(clienteId: cliente.Id, whatsAppConsentAtCreation: false);

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.Pending, message.Status);
            Assert.Equal(WhatsAppNotificationTypes.Confirmation, message.NotificationType);
        }

        [Fact]
        public async Task QueueConfirmation_WhenDailyLimitWasReached_ShouldSkipWithoutDuplicatingPendingMessages()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 1);
            var firstCita = await fixture.SeedCitaAsync();
            var secondCita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(firstCita.Id);
            await fixture.Notifications.QueueAppointmentConfirmationAsync(secondCita.Id);
            await fixture.Notifications.QueueAppointmentConfirmationAsync(secondCita.Id);

            var messages = await fixture.Context.WhatsAppMessageLogs.OrderBy(message => message.Id).ToListAsync();
            Assert.Equal(2, messages.Count);
            Assert.Equal(WhatsAppMessageStatuses.Pending, messages[0].Status);
            Assert.Equal(WhatsAppMessageStatuses.SkippedDailyLimitExceeded, messages[1].Status);
            Assert.Equal(WhatsAppErrorCodes.DailyLimitExceeded, messages[1].ErrorCode);
        }

        [Fact]
        public async Task QueueConfirmation_WhenCalendarEntryIsBreak_ShouldSkipAsNotEligible()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync(tipo: "DESCANSO", phone: null);

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedNotEligible, message.Status);
        }

        [Fact]
        public async Task QueueConfirmation_WhenPhoneIsInvalid_ShouldSkipWithExplicitStatus()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync(phone: "invalid");

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedInvalidPhone, message.Status);
            Assert.Equal(WhatsAppErrorCodes.InvalidPhone, message.ErrorCode);
        }

        [Fact]
        public async Task QueueReminder_ShouldRespectTenantDailyLimit()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 1);
            var confirmation = await fixture.SeedCitaAsync();
            var reminder = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentConfirmationAsync(confirmation.Id);
            await fixture.Notifications.QueueAppointmentReminderAsync(reminder.Id);

            var reminderMessage = await fixture.Context.WhatsAppMessageLogs
                .SingleAsync(message => message.CitaId == reminder.Id);
            Assert.Equal(WhatsAppMessageStatuses.SkippedDailyLimitExceeded, reminderMessage.Status);
        }

        [Fact]
        public async Task QueueReminder_WhenRegisteredClientRevokedConsent_ShouldSkipAsConsentMissing()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cliente = await fixture.SeedClienteAsync("Cliente revocado", "83334444", aceptaMensajesWhatsApp: true);
            var cita = await fixture.SeedCitaAsync(
                clienteId: cliente.Id,
                fechaHora: new DateTime(2026, 5, 27, 12, 0, 0),
                whatsAppConsentAtCreation: true);

            cliente.AceptaMensajesWhatsApp = false;
            await fixture.Context.SaveChangesAsync();

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedConsentMissing, message.Status);
            Assert.Equal(WhatsAppErrorCodes.ConsentMissing, message.ErrorCode);
        }

        [Fact]
        public async Task UpdateSettings_WhenTenantIsDisabled_ShouldReleasePendingMessages()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync();
            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            await fixture.UpdateSettingsAsync(isEnabled: false);

            var message = await fixture.Context.WhatsAppMessageLogs.AsNoTracking().SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedTenantDisabled, message.Status);
            Assert.Equal(WhatsAppErrorCodes.TenantDisabled, message.ErrorCode);
            Assert.NotNull(message.ProcessedAtUtc);
            Assert.Null(message.ProcessingStartedAtUtc);
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenLimitWasLowered_ShouldSendOldestReservationAndSkipTheRest()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 2);
            var firstCita = await fixture.SeedCitaAsync();
            var secondCita = await fixture.SeedCitaAsync();
            await fixture.Notifications.QueueAppointmentConfirmationAsync(firstCita.Id);
            await fixture.Notifications.QueueAppointmentConfirmationAsync(secondCita.Id);

            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 1);
            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var messages = await fixture.Context.WhatsAppMessageLogs.OrderBy(message => message.Id).ToListAsync();
            Assert.Equal(WhatsAppMessageStatuses.Sent, messages[0].Status);
            Assert.Equal(WhatsAppMessageStatuses.SkippedDailyLimitExceeded, messages[1].Status);
            Assert.Equal(1, fixture.MetaClient.SendCount);
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenClientRevokesConsentBeforeSend_ShouldSkipWithoutCallingMeta()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cliente = await fixture.SeedClienteAsync("Cliente pendiente", "84445555", aceptaMensajesWhatsApp: true);
            var cita = await fixture.SeedCitaAsync(clienteId: cliente.Id, whatsAppConsentAtCreation: true);

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            cliente.AceptaMensajesWhatsApp = false;
            await fixture.Context.SaveChangesAsync();

            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var message = await fixture.Context.WhatsAppMessageLogs.AsNoTracking().SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedConsentMissing, message.Status);
            Assert.Equal(WhatsAppErrorCodes.ConsentMissing, message.ErrorCode);
            Assert.Equal(0, fixture.MetaClient.SendCount);
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenMetaReturnsAuthenticationError_ShouldFailImmediately()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync();
            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);
            fixture.MetaClient.NextSendResult = MetaWhatsAppSendResult.Failed(
                "190",
                "Meta API error HTTP 401, type=OAuthException, code=190, subcode=463, message=Error validating access token, fbtrace_id=test-trace",
                HttpStatusCode.Unauthorized,
                "{\"error\":{\"message\":\"Error validating access token\",\"type\":\"OAuthException\",\"code\":190,\"error_subcode\":463,\"fbtrace_id\":\"test-trace\"}}",
                errorType: "OAuthException",
                errorSubcode: 463,
                fbTraceId: "test-trace",
                shouldRetry: false);

            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var message = await fixture.Context.WhatsAppMessageLogs.AsNoTracking().SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.Failed, message.Status);
            Assert.Equal("190", message.ErrorCode);
            Assert.Contains("OAuthException", message.ErrorMessage);
            Assert.NotNull(message.FailedAtUtc);
            Assert.NotNull(message.ProcessedAtUtc);
            Assert.Null(message.NextAttemptAtUtc);
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenMetaReturnsTransientError_ShouldRetryWithoutErrorFieldsOnPending()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync();
            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);
            fixture.MetaClient.NextSendResult = MetaWhatsAppSendResult.Failed(
                "TIMEOUT",
                "Timeout enviando mensaje a Meta WhatsApp.",
                shouldRetry: true);

            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var message = await fixture.Context.WhatsAppMessageLogs.AsNoTracking().SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.Pending, message.Status);
            Assert.Null(message.ErrorCode);
            Assert.Null(message.ErrorMessage);
            Assert.Null(message.FailedAtUtc);
            Assert.Null(message.ProcessedAtUtc);
            Assert.NotNull(message.NextAttemptAtUtc);
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
            private readonly ServiceProvider _serviceProvider;

            private Fixture(
                Guid tenantId,
                TestTenantProvider tenantProvider,
                ProyectoIdentity.Datos.ApplicationDbContext context,
                Microsoft.Data.Sqlite.SqliteConnection connection,
                TenantWhatsAppSettingsService settings,
                FakeMetaWhatsAppClient metaClient,
                CalendarWhatsAppNotificationService notifications,
                ServiceProvider serviceProvider)
            {
                TenantId = tenantId;
                TenantProvider = tenantProvider;
                Context = context;
                _connection = connection;
                Settings = settings;
                MetaClient = metaClient;
                Notifications = notifications;
                _serviceProvider = serviceProvider;
            }

            public Guid TenantId { get; }
            public TestTenantProvider TenantProvider { get; }
            public ProyectoIdentity.Datos.ApplicationDbContext Context { get; }
            public TenantWhatsAppSettingsService Settings { get; }
            public FakeMetaWhatsAppClient MetaClient { get; }
            public CalendarWhatsAppNotificationService Notifications { get; }

            public static async Task<Fixture> CreateAsync()
            {
                var tenantId = Guid.NewGuid();
                var tenantProvider = new TestTenantProvider { TenantId = tenantId };
                var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
                var options = new StaticOptionsMonitor<MetaWhatsAppOptions>(new MetaWhatsAppOptions { Enabled = true });
                var settings = new TenantWhatsAppSettingsService(
                    context,
                    tenantProvider,
                    options,
                    NullLogger<TenantWhatsAppSettingsService>.Instance);
                var serviceProvider = new ServiceCollection().BuildServiceProvider();
                var tenantExecution = new TenantExecutionService(
                    serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<TenantExecutionService>.Instance);
                var metaClient = new FakeMetaWhatsAppClient();
                var notifications = new CalendarWhatsAppNotificationService(
                    context,
                    metaClient,
                    options,
                    new FixedBusinessDateTimeProvider(),
                    settings,
                    tenantProvider,
                    tenantExecution,
                    NullLogger<CalendarWhatsAppNotificationService>.Instance);

                context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant WhatsApp" });
                await context.SaveChangesAsync();

                return new Fixture(tenantId, tenantProvider, context, connection, settings, metaClient, notifications, serviceProvider);
            }

            public Task UpdateSettingsAsync(bool isEnabled, int dailyLimit = TenantWhatsAppSettings.DefaultDailyMessageLimit) =>
                Settings.UpdateSettingsAsync(
                    TenantId,
                    new TenantWhatsAppSettingsUpdateDto
                    {
                        IsEnabled = isEnabled,
                        DailyMessageLimit = dailyLimit
                    },
                    "platform-user");

            public async Task<LuxuryApp.Models.DataBase.ClientesModel> SeedClienteAsync(
                string nombre,
                string telefono,
                bool aceptaMensajesWhatsApp)
            {
                var cliente = new LuxuryApp.Models.DataBase.ClientesModel
                {
                    Nombre = nombre,
                    NumeroTelefono = telefono,
                    AceptaMensajesWhatsApp = aceptaMensajesWhatsApp,
                    WhatsAppConsentUpdatedAtUtc = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc),
                    WhatsAppConsentSource = "ClienteForm",
                    WhatsAppConsentTextVersion = "wa_optin_v1",
                    FrecuenciaVisita = 30,
                    FechaUltimaVisita = new DateTime(2026, 5, 1)
                };

                Context.Clientes.Add(cliente);
                await Context.SaveChangesAsync();
                return cliente;
            }

            public async Task<Cita> SeedCitaAsync(
                string tipo = "CITA",
                string? phone = "88889999",
                DateTime? fechaHora = null,
                int? clienteId = null,
                bool whatsAppConsentAtCreation = true)
            {
                var puesto = new Puesto
                {
                    NombrePuesto = $"Puesto {Guid.NewGuid():N}",
                    Detalle = "WhatsApp",
                    Activo = true
                };
                Context.Puestos.Add(puesto);
                await Context.SaveChangesAsync();

                var funcionario = new Funcionario
                {
                    Nombre = "Andrea",
                    IdPuesto = puesto.IdPuesto,
                    ColorCalendario = "#123456",
                    PorcentajeGanancia = 40m,
                    PorcentajeProducto = 10m,
                    FechaIngreso = new DateTime(2026, 5, 1),
                    Activo = true
                };
                Context.Funcionarios.Add(funcionario);
                await Context.SaveChangesAsync();

                LuxuryApp.Models.DataBase.ClientesModel? cliente = null;
                if (clienteId.HasValue)
                {
                    cliente = await Context.Clientes.FirstAsync(current => current.Id == clienteId.Value);
                }

                var cita = new Cita
                {
                    NombreCliente = tipo == "DESCANSO" ? "DESCANSO" : cliente?.Nombre ?? "Cliente WhatsApp",
                    TelefonoCliente = tipo == "DESCANSO" ? null : cliente?.NumeroTelefono ?? phone,
                    ClienteId = cliente?.Id,
                    FechaHoraCita = fechaHora ?? new DateTime(2026, 5, 27, 10, 0, 0),
                    FuncionarioId = funcionario.IdFuncionario,
                    Tipo = tipo,
                    WhatsAppConsentAtCreation = tipo == "DESCANSO"
                        ? false
                        : cliente?.AceptaMensajesWhatsApp ?? whatsAppConsentAtCreation,
                    WhatsAppConsentSource = tipo == "DESCANSO"
                        ? null
                        : cliente is not null
                            ? "ClienteRegistrado"
                            : (whatsAppConsentAtCreation ? "CitaManual" : "SinConsentimiento"),
                    WhatsAppConsentCapturedAtUtc = tipo == "DESCANSO"
                        ? null
                        : (cliente is not null || whatsAppConsentAtCreation
                            ? new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc)
                            : null)
                };
                Context.Citas.Add(cita);
                await Context.SaveChangesAsync();
                return cita;
            }

            public void Dispose()
            {
                Context.Dispose();
                _connection.Dispose();
                _serviceProvider.Dispose();
            }
        }

        private sealed class FakeMetaWhatsAppClient : IMetaWhatsAppClient
        {
            public int SendCount { get; private set; }

            public MetaWhatsAppSendResult? NextSendResult { get; set; }

            public string? NormalizePhoneNumber(string? phoneNumber) =>
                string.IsNullOrWhiteSpace(phoneNumber) ||
                string.Equals(phoneNumber, "invalid", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $"+506{new string(phoneNumber.Where(char.IsDigit).ToArray())}";

            public bool IsValidPhoneNumber(string? phoneNumber) => NormalizePhoneNumber(phoneNumber) is not null;

            public Task<MetaWhatsAppSendResult> SendConfirmationTemplateAsync(
                string recipientPhone,
                string customerName,
                string businessName,
                string appointmentDate,
                string appointmentTime,
                string professionalName,
                CancellationToken cancellationToken = default)
            {
                SendCount++;
                return Task.FromResult(ConsumeResult($"confirmation-{SendCount}"));
            }

            public Task<MetaWhatsAppSendResult> SendReminderTemplateAsync(
                string recipientPhone,
                string customerName,
                string businessName,
                string appointmentTime,
                string professionalName,
                CancellationToken cancellationToken = default)
            {
                SendCount++;
                return Task.FromResult(ConsumeResult($"reminder-{SendCount}"));
            }

            public Task<MetaWhatsAppSendResult> SendTextMessageAsync(
                string recipientPhone,
                string message,
                CancellationToken cancellationToken = default)
            {
                SendCount++;
                return Task.FromResult(ConsumeResult($"text-{SendCount}"));
            }

            public Task<MetaWhatsAppConfigurationDiagnosticResult> TestConfigurationAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new MetaWhatsAppConfigurationDiagnosticResult(
                    Success: true,
                    Configuration: MetaWhatsAppConfigurationSnapshot.Create(new MetaWhatsAppOptions
                    {
                        Enabled = true,
                        GraphApiVersion = "v25.0",
                        BaseUrl = "https://graph.facebook.com",
                        PhoneNumberId = "1049980000002485",
                        WhatsAppBusinessAccountId = "1306550000005151",
                        AccessToken = "EAAOod000000000000000000zIF7",
                        AppSecret = "00000000000000000000000000000000"
                    }),
                    PhoneNumberProbe: new MetaWhatsAppEndpointProbeResult(
                        Success: true,
                        Endpoint: "https://graph.facebook.com/v25.0/1049980000002485?fields=id,display_phone_number,verified_name",
                        HttpStatus: 200,
                        DisplayPhoneNumber: "+50688889999",
                        VerifiedName: "LuxuryCloud",
                        ErrorType: null,
                        ErrorCode: null,
                        ErrorSubcode: null,
                        ErrorMessage: null,
                        FbTraceId: null,
                        ResponsePreview: null),
                    WabaPhoneNumbersProbe: null,
                    PhoneNumberBelongsToConfiguredWaba: null));

            private MetaWhatsAppSendResult ConsumeResult(string successId)
            {
                if (NextSendResult is null)
                {
                    return MetaWhatsAppSendResult.Succeeded(successId, HttpStatusCode.OK, responseBody: null);
                }

                var result = NextSendResult;
                NextSendResult = null;
                return result;
            }
        }
    }
}
