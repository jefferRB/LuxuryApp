using System.Net;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class CalendarWhatsAppNotificationServiceTests
    {
        [Fact]
        public async Task QueueConfirmation_WhenTenantHasNoStoredSettings_ShouldUseAddonDefaultsAndCreatePendingOutboundMessage()
        {
            using var fixture = await Fixture.CreateAsync();
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            var settings = await fixture.Settings.GetSettingsForTenantAsync(fixture.TenantId);
            Assert.Equal(WhatsAppMessageStatuses.Pending, message.Status);
            Assert.True(settings.IsEnabled);
            Assert.True(settings.SendConfirmationOnCreate);
            Assert.True(settings.SendReminderThreeHoursBefore);
            Assert.Equal(15, settings.DailyMessageLimit);
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
            Assert.Equal(WhatsAppErrorCodes.TenantDisabled, message.ErrorCode);
            Assert.Equal(0, fixture.MetaClient.SendCount);
            Assert.Equal(0, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
        }

        [Fact]
        public async Task QueueConfirmation_WhenConfirmationsWereDisabledByTenant_ShouldSkipAsUserDisabled()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(
                isEnabled: true,
                sendConfirmation: false,
                sendReminder: true);
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedUserDisabled, message.Status);
            Assert.Equal(WhatsAppErrorCodes.UserDisabled, message.ErrorCode);
        }

        [Fact]
        public async Task QueueReminder_WhenRemindersWereDisabledByTenant_ShouldSkipAsUserDisabled()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(
                isEnabled: true,
                sendConfirmation: true,
                sendReminder: false);
            var cita = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            Assert.Equal(WhatsAppMessageStatuses.SkippedUserDisabled, message.Status);
            Assert.Equal(WhatsAppErrorCodes.UserDisabled, message.ErrorCode);
        }

        [Fact]
        public async Task QueueReminder_WithoutActiveAddon_ShouldSkipAsNoActiveWhatsAppAddon()
        {
            using var fixture = await Fixture.CreateAsync(seedActiveAddon: false);
            await fixture.UpdateSettingsAsync(isEnabled: true);
            var cita = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync(current => current.CitaId == cita.Id);
            Assert.Equal(WhatsAppMessageStatuses.SkippedSubscriptionRequired, message.Status);
            Assert.Equal(WhatsAppErrorCodes.NoActiveWhatsAppAddon, message.ErrorCode);
            Assert.Equal(0, fixture.MetaClient.SendCount);
        }

        [Fact]
        public async Task QueueReminder_WithoutActiveBaseSubscription_ShouldSkipAsNoActiveBaseSubscription()
        {
            using var fixture = await Fixture.CreateAsync(seedActiveBaseSubscription: false);
            var cita = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync(current => current.CitaId == cita.Id);
            Assert.Equal(WhatsAppMessageStatuses.SkippedSubscriptionRequired, message.Status);
            Assert.Equal(WhatsAppErrorCodes.NoActiveBaseSubscription, message.ErrorCode);
        }

        [Fact]
        public async Task QueueReminder_WhenAddonExpired_ShouldSkipAsNoActiveWhatsAppAddon()
        {
            using var fixture = await Fixture.CreateAsync(addonEndsUtc: Fixture.FixedNowUtc.AddMinutes(-1));
            var cita = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync(current => current.CitaId == cita.Id);
            Assert.Equal(WhatsAppMessageStatuses.SkippedSubscriptionRequired, message.Status);
            Assert.Equal(WhatsAppErrorCodes.NoActiveWhatsAppAddon, message.ErrorCode);
        }

        [Fact]
        public async Task QueueReminder_WhenMonthlyBalanceWasExhausted_ShouldSkipAsMonthlyLimitExceeded()
        {
            using var fixture = await Fixture.CreateAsync(addonMonthlyLimit: 1);
            fixture.Context.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
            {
                TenantId = fixture.TenantId,
                Direction = WhatsAppMessageDirections.Outbound,
                NotificationType = WhatsAppNotificationTypes.Confirmation,
                Status = WhatsAppMessageStatuses.Sent,
                CreatedAtUtc = Fixture.FixedNowUtc,
                SentAtUtc = Fixture.FixedNowUtc
            });
            await fixture.Context.SaveChangesAsync();

            var cita = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);

            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync(current => current.CitaId == cita.Id);
            Assert.Equal(WhatsAppMessageStatuses.SkippedMonthlyLimitExceeded, message.Status);
            Assert.Equal(WhatsAppErrorCodes.MonthlyLimitExceeded, message.ErrorCode);
            Assert.Equal(0, fixture.MetaClient.SendCount);
        }

        [Fact]
        public async Task QueueReminder_WhenActualDailyUsageReached_ShouldSkipAsDailyLimitExceeded()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 1);
            var firstCita = await fixture.SeedCitaAsync();
            await fixture.Notifications.QueueAppointmentConfirmationAsync(firstCita.Id);
            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var reminder = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(reminder.Id);

            var reminderMessage = await fixture.Context.WhatsAppMessageLogs
                .SingleAsync(message => message.CitaId == reminder.Id);
            Assert.Equal(WhatsAppMessageStatuses.SkippedDailyLimitExceeded, reminderMessage.Status);
            Assert.Equal(WhatsAppErrorCodes.DailyLimitExceeded, reminderMessage.ErrorCode);
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenDailyLimitAllowsOnlyOne_ShouldSendOldestAndSkipTheRest()
        {
            using var fixture = await Fixture.CreateAsync();
            await fixture.UpdateSettingsAsync(isEnabled: true, dailyLimit: 1);
            var firstCita = await fixture.SeedCitaAsync();
            var secondCita = await fixture.SeedCitaAsync(phone: "89990000");

            await fixture.Notifications.QueueAppointmentConfirmationAsync(firstCita.Id);
            await fixture.Notifications.QueueAppointmentConfirmationAsync(secondCita.Id);
            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var messages = await fixture.Context.WhatsAppMessageLogs
                .OrderBy(message => message.Id)
                .ToListAsync();

            Assert.Equal(2, messages.Count);
            Assert.Equal(WhatsAppMessageStatuses.Sent, messages[0].Status);
            Assert.Equal(WhatsAppMessageStatuses.SkippedDailyLimitExceeded, messages[1].Status);
            Assert.Equal(1, fixture.MetaClient.SendCount);
            Assert.Equal(1, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenMetaAcceptsMessage_ShouldConsumeDailyAndMonthlyUsage()
        {
            using var fixture = await Fixture.CreateAsync();
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);
            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var addon = await fixture.Context.TenantSubscriptionAddons.SingleAsync();
            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            var monthlyUsage = await fixture.SubscriptionService.GetWhatsAppUsageInCurrentPeriodAsync(
                fixture.TenantId,
                addon.FechaInicio,
                addon.FechaFin);

            Assert.Equal(WhatsAppMessageStatuses.Sent, message.Status);
            Assert.Equal(1, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
            Assert.Equal(1, monthlyUsage);
        }

        [Fact]
        public async Task ProcessPendingNotifications_WhenMetaRejectsMessage_ShouldNotConsumeBalance()
        {
            using var fixture = await Fixture.CreateAsync();
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

            var addon = await fixture.Context.TenantSubscriptionAddons.SingleAsync();
            var message = await fixture.Context.WhatsAppMessageLogs.SingleAsync();
            var monthlyUsage = await fixture.SubscriptionService.GetWhatsAppUsageInCurrentPeriodAsync(
                fixture.TenantId,
                addon.FechaInicio,
                addon.FechaFin);

            Assert.Equal(WhatsAppMessageStatuses.Failed, message.Status);
            Assert.Equal(0, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
            Assert.Equal(0, monthlyUsage);
        }

        [Fact]
        public async Task QueueConfirmation_DoubleSubmitForSameAppointment_ShouldNotDuplicateConsumption()
        {
            using var fixture = await Fixture.CreateAsync();
            var cita = await fixture.SeedCitaAsync();

            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);
            await fixture.Notifications.QueueAppointmentConfirmationAsync(cita.Id);
            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var messages = await fixture.Context.WhatsAppMessageLogs.ToListAsync();
            var addon = await fixture.Context.TenantSubscriptionAddons.SingleAsync();
            var monthlyUsage = await fixture.SubscriptionService.GetWhatsAppUsageInCurrentPeriodAsync(
                fixture.TenantId,
                addon.FechaInicio,
                addon.FechaFin);

            Assert.Single(messages);
            Assert.Equal(1, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
            Assert.Equal(1, monthlyUsage);
        }

        [Fact]
        public async Task QueueReminder_DoubleExecutionForSameAppointment_ShouldNotDuplicateConsumption()
        {
            using var fixture = await Fixture.CreateAsync();
            var cita = await fixture.SeedCitaAsync(fechaHora: new DateTime(2026, 5, 26, 13, 0, 0));

            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);
            await fixture.Notifications.QueueAppointmentReminderAsync(cita.Id);
            await fixture.Notifications.ProcessPendingNotificationsAsync();

            var messages = await fixture.Context.WhatsAppMessageLogs.ToListAsync();
            var addon = await fixture.Context.TenantSubscriptionAddons.SingleAsync();
            var monthlyUsage = await fixture.SubscriptionService.GetWhatsAppUsageInCurrentPeriodAsync(
                fixture.TenantId,
                addon.FechaInicio,
                addon.FechaFin);

            Assert.Single(messages);
            Assert.Equal(1, await fixture.Settings.GetTodayUsageAsync(fixture.TenantId));
            Assert.Equal(1, monthlyUsage);
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
                SuscripcionService subscriptionService,
                TenantWhatsAppSettingsService settings,
                FakeMetaWhatsAppClient metaClient,
                CalendarWhatsAppNotificationService notifications,
                ServiceProvider serviceProvider)
            {
                TenantId = tenantId;
                TenantProvider = tenantProvider;
                Context = context;
                _connection = connection;
                SubscriptionService = subscriptionService;
                Settings = settings;
                MetaClient = metaClient;
                Notifications = notifications;
                _serviceProvider = serviceProvider;
            }

            public static DateTime FixedNowLocal => new(2026, 5, 26, 10, 30, 0);

            public static DateTime FixedNowUtc =>
                new DateTimeOffset(FixedNowLocal, TimeSpan.FromHours(-6)).UtcDateTime;

            public Guid TenantId { get; }
            public TestTenantProvider TenantProvider { get; }
            public ProyectoIdentity.Datos.ApplicationDbContext Context { get; }
            public SuscripcionService SubscriptionService { get; }
            public TenantWhatsAppSettingsService Settings { get; }
            public FakeMetaWhatsAppClient MetaClient { get; }
            public CalendarWhatsAppNotificationService Notifications { get; }

            public static async Task<Fixture> CreateAsync(
                bool seedActiveAddon = true,
                bool seedActiveBaseSubscription = true,
                int addonMonthlyLimit = 400,
                DateTime? addonEndsUtc = null)
            {
                var tenantId = Guid.NewGuid();
                var tenantProvider = new TestTenantProvider { TenantId = tenantId };
                var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
                var options = new StaticOptionsMonitor<MetaWhatsAppOptions>(new MetaWhatsAppOptions { Enabled = true });
                var cache = new MemoryCache(new MemoryCacheOptions());
                var accessCache = new TenantCommercialAccessCache(cache);
                var businessDateTimeProvider = new FixedBusinessDateTimeProvider(FixedNowLocal);
                var subscriptionService = new SuscripcionService(
                    context,
                    cache,
                    accessCache,
                    businessDateTimeProvider,
                    Options.Create(new TilopayRepeatOptions()),
                    NullLogger<SuscripcionService>.Instance);
                var settings = new TenantWhatsAppSettingsService(
                    context,
                    tenantProvider,
                    options,
                    subscriptionService,
                    businessDateTimeProvider,
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
                    businessDateTimeProvider,
                    settings,
                    tenantProvider,
                    tenantExecution,
                    NullLogger<CalendarWhatsAppNotificationService>.Instance);

                context.Tenants.Add(new Tenant { Id = tenantId, Nombre = "Tenant WhatsApp" });

                if (seedActiveBaseSubscription)
                {
                    var basePlanId = Guid.NewGuid();
                    context.Planes.Add(new Plan
                    {
                        Id = basePlanId,
                        Codigo = PlanCodes.Basic,
                        Nombre = "Basico",
                        Moneda = "CRC",
                        PrecioMensual = 8000m,
                        MaxFuncionarios = 1,
                        Activo = true
                    });

                    context.Suscripciones.Add(new Suscripcion
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PlanId = basePlanId,
                        CodigoPlan = PlanCodes.Basic,
                        Estado = EstadoSuscripcion.Activa,
                        Proveedor = PaymentProviderType.Tilopay,
                        FechaInicio = FixedNowUtc.AddDays(-3),
                        FechaFin = FixedNowUtc.AddDays(27),
                        FechaProximoCobroUtc = FixedNowUtc.AddDays(27),
                        FechaUltimaActualizacionUtc = FixedNowUtc
                    });
                }

                if (seedActiveAddon)
                {
                    var addOnPlanId = Guid.NewGuid();
                    context.Planes.Add(new Plan
                    {
                        Id = addOnPlanId,
                        Codigo = PlanCodes.WhatsApp400,
                        Nombre = "WhatsApp 400",
                        Moneda = "CRC",
                        PrecioMensual = 6000m,
                        LimiteMensajesMensual = addonMonthlyLimit,
                        Activo = true
                    });
                    context.TenantSubscriptionAddons.Add(new TenantSubscriptionAddon
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PlanId = addOnPlanId,
                        AddonCode = PlanCodes.WhatsApp400,
                        Estado = EstadoSuscripcion.Activa,
                        MonthlyMessageLimit = addonMonthlyLimit,
                        FechaInicio = FixedNowUtc.AddDays(-1),
                        FechaFin = addonEndsUtc ?? FixedNowUtc.AddDays(29),
                        CreatedAtUtc = FixedNowUtc,
                        UpdatedAtUtc = FixedNowUtc
                    });
                }

                await context.SaveChangesAsync();

                return new Fixture(
                    tenantId,
                    tenantProvider,
                    context,
                    connection,
                    subscriptionService,
                    settings,
                    metaClient,
                    notifications,
                    serviceProvider);
            }

            public Task UpdateSettingsAsync(
                bool isEnabled,
                int dailyLimit = TenantWhatsAppSettings.DefaultDailyMessageLimit,
                bool sendConfirmation = true,
                bool sendReminder = true) =>
                Settings.UpdateSettingsAsync(
                    TenantId,
                    new TenantWhatsAppSettingsUpdateDto
                    {
                        IsEnabled = isEnabled,
                        SendConfirmationOnCreate = sendConfirmation,
                        SendReminderThreeHoursBefore = sendReminder,
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
