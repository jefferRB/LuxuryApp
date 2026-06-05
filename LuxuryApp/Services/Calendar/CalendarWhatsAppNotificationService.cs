using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Calendar
{
    public sealed class CalendarWhatsAppNotificationService : ICalendarWhatsAppNotificationService
    {
        private const int MaxAttempts = 3;
        private const int PendingBatchSize = 25;

        private static readonly string[] ActiveOutboundStatuses =
        [
            WhatsAppMessageStatuses.Pending,
            WhatsAppMessageStatuses.Processing,
            WhatsAppMessageStatuses.Sent,
            WhatsAppMessageStatuses.Delivered,
            WhatsAppMessageStatuses.Read
        ];

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ApplicationDbContext _context;
        private readonly IMetaWhatsAppClient _metaClient;
        private readonly IOptionsMonitor<MetaWhatsAppOptions> _options;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantWhatsAppSettingsService _tenantSettingsService;
        private readonly ITenantProvider _tenantProvider;
        private readonly TenantExecutionService _tenantExecutionService;
        private readonly ILogger<CalendarWhatsAppNotificationService> _logger;

        public CalendarWhatsAppNotificationService(
            ApplicationDbContext context,
            IMetaWhatsAppClient metaClient,
            IOptionsMonitor<MetaWhatsAppOptions> options,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantWhatsAppSettingsService tenantSettingsService,
            ITenantProvider tenantProvider,
            TenantExecutionService tenantExecutionService,
            ILogger<CalendarWhatsAppNotificationService> logger)
        {
            _context = context;
            _metaClient = metaClient;
            _options = options;
            _businessDateTimeProvider = businessDateTimeProvider;
            _tenantSettingsService = tenantSettingsService;
            _tenantProvider = tenantProvider;
            _tenantExecutionService = tenantExecutionService;
            _logger = logger;
        }

        public async Task SendAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default)
        {
            await QueueAppointmentConfirmationAsync(citaId, cancellationToken);
            await ProcessPendingNotificationsAsync(cancellationToken);
        }

        public async Task SendAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default)
        {
            await QueueAppointmentReminderAsync(citaId, cancellationToken);
            await ProcessPendingNotificationsAsync(cancellationToken);
        }

        public Task QueueAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default) =>
            QueueAppointmentAsync(citaId, WhatsAppNotificationTypes.Confirmation, cancellationToken);

        public Task QueueAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default) =>
            QueueAppointmentAsync(citaId, WhatsAppNotificationTypes.Reminder3Hours, cancellationToken);

        public async Task ScheduleDueRemindersAsync(CancellationToken cancellationToken = default)
        {
            var options = _options.CurrentValue;
            if (!options.Enabled ||
                !options.SendReminderBeforeAppointment ||
                !_tenantProvider.HasTenant())
            {
                return;
            }

            var tenantId = _tenantProvider.GetTenantId();
            var settings = await _tenantSettingsService.GetSettingsForTenantAsync(tenantId, cancellationToken);
            if (!settings.IsEnabled || !settings.SendReminderThreeHoursBefore)
            {
                return;
            }

            var now = _businessDateTimeProvider.Now();
            var upperLimit = now.AddMinutes(GetReminderLeadTimeMinutes(options));

            var candidates = await _context.Citas
                .AsNoTracking()
                .Where(c =>
                    c.Tipo == "CITA" &&
                    c.FechaHoraCita > now &&
                    c.FechaHoraCita <= upperLimit &&
                    !c.Recordatorio3hEnviado &&
                    c.RecordatorioWhatsAppTresHorasEnviadoUtc == null &&
                    c.EstadoConfirmacionWhatsApp != WhatsAppConfirmationStates.Cancelada)
                .Select(c => new
                {
                    c.Id,
                    c.TelefonoCliente
                })
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                await QueueAppointmentReminderAsync(candidate.Id, cancellationToken);
            }
        }

        public async Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var staleProcessingCutoffUtc = nowUtc.AddMinutes(-10);

            await _context.WhatsAppMessageLogs
                .Where(message =>
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    message.Status == WhatsAppMessageStatuses.Processing &&
                    message.AttemptCount < MaxAttempts &&
                    message.ProcessingStartedAtUtc != null &&
                    message.ProcessingStartedAtUtc < staleProcessingCutoffUtc)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(message => message.Status, WhatsAppMessageStatuses.Pending)
                    .SetProperty(message => message.ProcessingStartedAtUtc, (DateTime?)null)
                    .SetProperty(message => message.NextAttemptAtUtc, nowUtc),
                    cancellationToken);

            var pendingIds = await _context.WhatsAppMessageLogs
                .AsNoTracking()
                .Where(message =>
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    message.Status == WhatsAppMessageStatuses.Pending &&
                    message.AttemptCount < MaxAttempts &&
                    (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= nowUtc) &&
                    (message.NotificationType == WhatsAppNotificationTypes.Confirmation ||
                     message.NotificationType == WhatsAppNotificationTypes.Reminder3Hours))
                .OrderBy(message => message.CreatedAtUtc)
                .Select(message => message.Id)
                .Take(PendingBatchSize)
                .ToListAsync(cancellationToken);

            foreach (var messageId in pendingIds)
            {
                var pendingMessage = await _context.WhatsAppMessageLogs
                    .AsNoTracking()
                    .Where(message => message.Id == messageId)
                    .Select(message => new
                    {
                        message.Id,
                        message.CitaId,
                        message.NotificationType
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (pendingMessage is null)
                {
                    continue;
                }

                if (pendingMessage.CitaId.HasValue)
                {
                    var citaForConsent = await _context.Citas
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Id == pendingMessage.CitaId.Value, cancellationToken);

                    if (citaForConsent is not null)
                    {
                        var consentDecision = await EvaluateConsentAsync(citaForConsent, cancellationToken);
                        if (!consentDecision.CanSend)
                        {
                            var skipped = await SkipPendingMessageForConsentAsync(
                                pendingMessage.Id,
                                pendingMessage.NotificationType,
                                citaForConsent,
                                consentDecision,
                                cancellationToken);

                            if (skipped)
                            {
                                continue;
                            }
                        }
                    }
                }

                var claimed = await _context.WhatsAppMessageLogs
                    .Where(message =>
                        message.Id == messageId &&
                        message.Status == WhatsAppMessageStatuses.Pending &&
                        message.AttemptCount < MaxAttempts)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(message => message.Status, WhatsAppMessageStatuses.Processing)
                        .SetProperty(message => message.ProcessingStartedAtUtc, nowUtc)
                        .SetProperty(message => message.LastAttemptAtUtc, nowUtc)
                        .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1),
                        cancellationToken);

                if (claimed == 0)
                {
                    continue;
                }

                var trackedMessageEntry = _context.ChangeTracker.Entries<WhatsAppMessageLog>()
                    .FirstOrDefault(entry => entry.Entity.Id == messageId);
                if (trackedMessageEntry is not null)
                {
                    trackedMessageEntry.State = EntityState.Detached;
                }

                var message = await _context.WhatsAppMessageLogs
                    .FirstOrDefaultAsync(current => current.Id == messageId, cancellationToken);

                if (message is null)
                {
                    continue;
                }

                await SendPendingLogAsync(message, cancellationToken);
            }
        }

        public async Task ProcessInboundReplyAsync(JsonElement payload, CancellationToken cancellationToken = default)
        {
            var inboundMessages = ExtractInboundMessages(payload);
            foreach (var inboundMessage in inboundMessages)
            {
                var action = ResolveReplyAction(inboundMessage);
                var candidates = await ResolveTargetCandidatesAsync(inboundMessage, cancellationToken);

                if (candidates.Count == 0)
                {
                    _logger.LogWarning(
                        "Respuesta WhatsApp sin cita asociada. MetaMessageId {MetaMessageId}. ContextMessageId {ContextMessageId}.",
                        inboundMessage.MessageId,
                        inboundMessage.ContextMessageId);
                    continue;
                }

                if (candidates.Count > 1)
                {
                    await RegisterAmbiguousInboundAsync(inboundMessage, candidates, cancellationToken);
                    continue;
                }

                var candidate = candidates[0];
                await _tenantExecutionService.RunForTenantAsync(
                    candidate.TenantId,
                    async (serviceProvider, _, ct) =>
                    {
                        var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
                        await ProcessResolvedInboundAsync(db, inboundMessage, candidate, action, ct);
                    },
                    cancellationToken);
            }
        }

        public async Task ProcessStatusUpdateAsync(JsonElement payload, CancellationToken cancellationToken = default)
        {
            var statusUpdates = ExtractStatusUpdates(payload);
            foreach (var statusUpdate in statusUpdates)
            {
                var processed = false;

                await _tenantExecutionService.RunForEachActiveTenantAsync(
                    async (serviceProvider, _, ct) =>
                    {
                        if (processed)
                        {
                            return;
                        }

                        var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
                        var message = await db.WhatsAppMessageLogs
                            .FirstOrDefaultAsync(
                                current => current.MetaMessageId == statusUpdate.MessageId &&
                                           current.Direction == WhatsAppMessageDirections.Outbound,
                                ct);

                        if (message is null)
                        {
                            return;
                        }

                        ApplyProviderStatus(message, statusUpdate);

                        var status = MapProviderStatus(statusUpdate.Status);
                        var alreadyLogged = await db.WhatsAppMessageLogs
                            .AnyAsync(
                                current => current.Direction == WhatsAppMessageDirections.Status &&
                                           current.ContextMessageId == statusUpdate.MessageId &&
                                           current.Status == status,
                                ct);

                        if (!alreadyLogged)
                        {
                            db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
                            {
                                CitaId = message.CitaId,
                                Direction = WhatsAppMessageDirections.Status,
                                NotificationType = WhatsAppNotificationTypes.Status,
                                Provider = WhatsAppProviders.Meta,
                                ContextMessageId = statusUpdate.MessageId,
                                RecipientPhoneE164 = _metaClient.NormalizePhoneNumber(statusUpdate.RecipientPhone),
                                Status = status,
                                ErrorCode = statusUpdate.ErrorCode,
                                ErrorMessage = Trim(statusUpdate.ErrorMessage, 1000),
                                PayloadJson = BuildStatusPayloadJson(statusUpdate),
                                CreatedAtUtc = DateTime.UtcNow,
                                ProcessedAtUtc = DateTime.UtcNow
                            });
                        }

                        await db.SaveChangesAsync(ct);
                        processed = true;
                    },
                    cancellationToken);

                if (!processed)
                {
                    _logger.LogWarning(
                        "Status WhatsApp sin mensaje saliente asociado. MetaMessageId {MetaMessageId}. Status {Status}.",
                        statusUpdate.MessageId,
                        statusUpdate.Status);
                }
            }
        }

        private async Task QueueAppointmentAsync(
            int citaId,
            string notificationType,
            CancellationToken cancellationToken)
        {
            var options = _options.CurrentValue;
            var cita = await _context.Citas
                .FirstOrDefaultAsync(c => c.Id == citaId, cancellationToken);

            if (cita is null)
            {
                return;
            }

            if (!IsNotifiableAppointment(cita, out var ignoredReason))
            {
                await RegisterSkippedOutboundAsync(
                    cita,
                    notificationType,
                    WhatsAppMessageStatuses.SkippedNotEligible,
                    WhatsAppErrorCodes.AppointmentNotEligible,
                    ignoredReason,
                    todayUsage: null,
                    dailyMessageLimit: null,
                    cancellationToken);
                return;
            }

            var consentDecision = await EvaluateConsentAsync(cita, cancellationToken);
            if (!consentDecision.CanSend)
            {
                await RegisterSkippedOutboundAsync(
                    cita,
                    notificationType,
                    WhatsAppMessageStatuses.SkippedConsentMissing,
                    WhatsAppErrorCodes.ConsentMissing,
                    consentDecision.Message,
                    todayUsage: null,
                    dailyMessageLimit: null,
                    cancellationToken);
                return;
            }

            var phoneE164 = _metaClient.NormalizePhoneNumber(cita.TelefonoCliente);
            if (phoneE164 is null)
            {
                await RegisterSkippedOutboundAsync(
                    cita,
                    notificationType,
                    WhatsAppMessageStatuses.SkippedInvalidPhone,
                    WhatsAppErrorCodes.InvalidPhone,
                    "Telefono invalido.",
                    todayUsage: null,
                    dailyMessageLimit: null,
                    cancellationToken);
                return;
            }

            if (notificationType == WhatsAppNotificationTypes.Reminder3Hours && !IsReminderInsideWindow(cita, options))
            {
                return;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var exists = await _context.WhatsAppMessageLogs
                .AnyAsync(message =>
                    message.CitaId == cita.Id &&
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    message.NotificationType == notificationType &&
                    ActiveOutboundStatuses.Contains(message.Status),
                    cancellationToken);

            if (exists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var decision = await _tenantSettingsService.CanSendNotificationAsync(
                cita.TenantId,
                notificationType,
                cancellationToken: cancellationToken);

            if (!decision.CanSend)
            {
                await RegisterSkippedOutboundAsync(
                    cita,
                    notificationType,
                    ResolveSkippedStatus(decision.ErrorCode),
                    decision.ErrorCode ?? WhatsAppErrorCodes.ConfigurationDisabled,
                    decision.ErrorMessage ?? "El mensaje WhatsApp fue omitido por configuracion.",
                    decision.TodayUsage,
                    decision.DailyMessageLimit,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            _context.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
            {
                CitaId = cita.Id,
                Direction = WhatsAppMessageDirections.Outbound,
                NotificationType = notificationType,
                Provider = WhatsAppProviders.Meta,
                RecipientPhoneE164 = phoneE164,
                TemplateName = ResolveTemplateName(options, notificationType),
                Status = WhatsAppMessageStatuses.Pending,
                PayloadJson = BuildQueuedPayloadJson(cita, notificationType),
                CreatedAtUtc = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Notificacion WhatsApp encolada. TenantId {TenantId}. CitaId {CitaId}. NotificationType {NotificationType}. UsoDiario {TodayUsage}. LimiteDiario {DailyMessageLimit}.",
                cita.TenantId,
                cita.Id,
                notificationType,
                decision.TodayUsage + 1,
                decision.DailyMessageLimit);
        }

        private async Task RegisterSkippedOutboundAsync(
            Cita cita,
            string notificationType,
            string status,
            string errorCode,
            string reason,
            int? todayUsage,
            int? dailyMessageLimit,
            CancellationToken cancellationToken)
        {
            if (notificationType == WhatsAppNotificationTypes.Confirmation)
            {
                cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.NoEnviada;
            }

            var alreadyIgnored = await _context.WhatsAppMessageLogs
                .AnyAsync(message =>
                    message.CitaId == cita.Id &&
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    message.NotificationType == notificationType &&
                    message.Status == status &&
                    message.ErrorCode == errorCode,
                    cancellationToken);

            if (!alreadyIgnored)
            {
                _context.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
                {
                    CitaId = cita.Id,
                    Direction = WhatsAppMessageDirections.Outbound,
                    NotificationType = notificationType,
                    Provider = WhatsAppProviders.Meta,
                    RecipientPhoneE164 = _metaClient.NormalizePhoneNumber(cita.TelefonoCliente),
                    TemplateName = ResolveTemplateName(_options.CurrentValue, notificationType),
                    Status = status,
                    ErrorCode = errorCode,
                    ErrorMessage = Trim(reason, 1000),
                    PayloadJson = BuildSkippedPayloadJson(cita, notificationType, errorCode),
                    CreatedAtUtc = DateTime.UtcNow,
                    ProcessedAtUtc = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Notificacion WhatsApp omitida. TenantId {TenantId}. CitaId {CitaId}. NotificationType {NotificationType}. Motivo {SkipReason}. UsoDiario {TodayUsage}. LimiteDiario {DailyMessageLimit}.",
                cita.TenantId,
                cita.Id,
                notificationType,
                errorCode,
                todayUsage,
                dailyMessageLimit);
        }

        private async Task<WhatsAppConsentDecision> EvaluateConsentAsync(
            Cita cita,
            CancellationToken cancellationToken)
        {
            if (cita.ClienteId.HasValue)
            {
                var cliente = await _context.Clientes
                    .AsNoTracking()
                    .Where(current => current.Id == cita.ClienteId.Value)
                    .Select(current => new
                    {
                        current.AceptaMensajesWhatsApp
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                if (cliente?.AceptaMensajesWhatsApp == true)
                {
                    return new WhatsAppConsentDecision(
                        CanSend: true,
                        Source: WhatsAppConsentSources.ClienteRegistrado,
                        HasClienteId: true,
                        Message: string.Empty);
                }

                return new WhatsAppConsentDecision(
                    CanSend: false,
                    Source: WhatsAppConsentSources.ClienteRegistrado,
                    HasClienteId: true,
                    Message: "El cliente no autorizó mensajes de WhatsApp.");
            }

            var source = ResolveConsentSource(cita);
            return cita.WhatsAppConsentAtCreation
                ? new WhatsAppConsentDecision(
                    CanSend: true,
                    Source: source,
                    HasClienteId: false,
                    Message: string.Empty)
                : new WhatsAppConsentDecision(
                    CanSend: false,
                    Source: source,
                    HasClienteId: false,
                    Message: "El cliente no autorizó mensajes de WhatsApp.");
        }

        private async Task<bool> SkipPendingMessageForConsentAsync(
            long messageId,
            string notificationType,
            Cita cita,
            WhatsAppConsentDecision consentDecision,
            CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var payloadJson = BuildSkippedPayloadJson(
                cita,
                notificationType,
                WhatsAppErrorCodes.ConsentMissing,
                consentDecision.Source,
                consentDecision.HasClienteId);

            var updated = await _context.WhatsAppMessageLogs
                .Where(message =>
                    message.Id == messageId &&
                    message.Status == WhatsAppMessageStatuses.Pending &&
                    message.AttemptCount < MaxAttempts)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(message => message.Status, WhatsAppMessageStatuses.SkippedConsentMissing)
                    .SetProperty(message => message.ErrorCode, WhatsAppErrorCodes.ConsentMissing)
                    .SetProperty(message => message.ErrorMessage, Trim(consentDecision.Message, 1000))
                    .SetProperty(message => message.ProcessedAtUtc, nowUtc)
                    .SetProperty(message => message.ProcessingStartedAtUtc, (DateTime?)null)
                    .SetProperty(message => message.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(message => message.PayloadJson, payloadJson),
                    cancellationToken);

            if (updated == 0)
            {
                return false;
            }

            if (notificationType == WhatsAppNotificationTypes.Confirmation)
            {
                await _context.Citas
                    .Where(current => current.Id == cita.Id)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(current => current.EstadoConfirmacionWhatsApp, WhatsAppConfirmationStates.NoEnviada),
                        cancellationToken);
            }

            _logger.LogInformation(
                "Notificacion WhatsApp pendiente omitida por falta de consentimiento. TenantId {TenantId}. CitaId {CitaId}. NotificationType {NotificationType}.",
                cita.TenantId,
                cita.Id,
                notificationType);

            return true;
        }

        private async Task SendPendingLogAsync(
            WhatsAppMessageLog message,
            CancellationToken cancellationToken)
        {
            var cita = await _context.Citas
                .Include(c => c.Funcionario)
                .Include(c => c.Servicio)
                .FirstOrDefaultAsync(c => c.Id == message.CitaId, cancellationToken);

            if (cita is null)
            {
                MarkSkipped(
                    message,
                    cita: null,
                    WhatsAppMessageStatuses.SkippedNotEligible,
                    WhatsAppErrorCodes.AppointmentNotEligible,
                    "Cita no encontrada.");
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            var consentDecision = await EvaluateConsentAsync(cita, cancellationToken);
            if (!consentDecision.CanSend)
            {
                MarkSkipped(
                    message,
                    cita,
                    WhatsAppMessageStatuses.SkippedConsentMissing,
                    WhatsAppErrorCodes.ConsentMissing,
                    consentDecision.Message);

                if (message.NotificationType == WhatsAppNotificationTypes.Confirmation)
                {
                    cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.NoEnviada;
                }

                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            var decision = await _tenantSettingsService.CanSendNotificationAsync(
                cita.TenantId,
                message.NotificationType,
                message.Id,
                cancellationToken);

            if (!decision.CanSend)
            {
                MarkSkipped(
                    message,
                    cita,
                    ResolveSkippedStatus(decision.ErrorCode),
                    decision.ErrorCode ?? WhatsAppErrorCodes.ConfigurationDisabled,
                    decision.ErrorMessage ?? "El mensaje WhatsApp fue omitido por configuracion.");
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Notificacion WhatsApp pendiente omitida. TenantId {TenantId}. CitaId {CitaId}. NotificationType {NotificationType}. Motivo {SkipReason}. UsoDiario {TodayUsage}. LimiteDiario {DailyMessageLimit}.",
                    cita.TenantId,
                    cita.Id,
                    message.NotificationType,
                    decision.ErrorCode,
                    decision.TodayUsage,
                    decision.DailyMessageLimit);
                return;
            }

            if (!IsNotifiableAppointment(cita, out var ignoredReason))
            {
                MarkSkipped(
                    message,
                    cita,
                    WhatsAppMessageStatuses.SkippedNotEligible,
                    WhatsAppErrorCodes.AppointmentNotEligible,
                    ignoredReason);
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            var phoneE164 = _metaClient.NormalizePhoneNumber(cita.TelefonoCliente);
            if (phoneE164 is null)
            {
                MarkSkipped(
                    message,
                    cita,
                    WhatsAppMessageStatuses.SkippedInvalidPhone,
                    WhatsAppErrorCodes.InvalidPhone,
                    "Telefono invalido.");
                cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.NoEnviada;
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            var options = _options.CurrentValue;
            if (message.NotificationType == WhatsAppNotificationTypes.Reminder3Hours && !IsReminderInsideWindow(cita, options))
            {
                MarkSkipped(
                    message,
                    cita,
                    WhatsAppMessageStatuses.SkippedNotEligible,
                    WhatsAppErrorCodes.AppointmentNotEligible,
                    "Cita fuera de la ventana de recordatorio.");
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            var tenantName = await _context.Tenants
                .AsNoTracking()
                .Where(tenant => tenant.Id == cita.TenantId)
                .Select(tenant => tenant.Nombre)
                .FirstOrDefaultAsync(cancellationToken);

            tenantName = string.IsNullOrWhiteSpace(tenantName) ? "LuxuryCloud" : tenantName;

            var sendResult = message.NotificationType switch
            {
                WhatsAppNotificationTypes.Confirmation => await _metaClient.SendConfirmationTemplateAsync(
                    phoneE164,
                    cita.NombreCliente ?? "Cliente",
                    tenantName,
                    cita.FechaHoraCita.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CR")),
                    cita.FechaHoraCita.ToString("hh:mm tt", CultureInfo.GetCultureInfo("es-CR")),
                    cita.Funcionario?.Nombre ?? "-",
                    cancellationToken),

                WhatsAppNotificationTypes.Reminder3Hours => await _metaClient.SendReminderTemplateAsync(
                    phoneE164,
                    cita.NombreCliente ?? "Cliente",
                    tenantName,
                    cita.FechaHoraCita.ToString("hh:mm tt", CultureInfo.GetCultureInfo("es-CR")),
                    cita.Funcionario?.Nombre ?? "-",
                    cancellationToken),

                _ => MetaWhatsAppSendResult.Failed("UNSUPPORTED_TYPE", "Tipo de notificacion no soportado.")
            };

            if (sendResult.Success && !string.IsNullOrWhiteSpace(sendResult.MetaMessageId))
            {
                MarkSent(message, sendResult, phoneE164);
                ApplySentState(cita, message.NotificationType, sendResult.MetaMessageId);
            }
            else
            {
                MarkFailedOrRetry(message, sendResult);
                if (message.NotificationType == WhatsAppNotificationTypes.Confirmation &&
                    message.Status == WhatsAppMessageStatuses.Failed)
                {
                    cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.ErrorEnvio;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        private static bool IsNotifiableAppointment(Cita cita, out string reason)
        {
            if (!string.Equals(cita.Tipo, "CITA", StringComparison.OrdinalIgnoreCase))
            {
                reason = "La entrada de agenda no es una cita.";
                return false;
            }

            if (string.Equals(cita.EstadoConfirmacionWhatsApp, WhatsAppConfirmationStates.Cancelada, StringComparison.OrdinalIgnoreCase))
            {
                reason = "La cita esta cancelada por WhatsApp.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool IsReminderInsideWindow(Cita cita, MetaWhatsAppOptions options)
        {
            var now = _businessDateTimeProvider.Now();
            return cita.FechaHoraCita > now &&
                   cita.FechaHoraCita <= now.AddMinutes(GetReminderLeadTimeMinutes(options));
        }

        private static void MarkSent(
            WhatsAppMessageLog message,
            MetaWhatsAppSendResult sendResult,
            string phoneE164)
        {
            var nowUtc = DateTime.UtcNow;
            message.Status = WhatsAppMessageStatuses.Sent;
            message.MetaMessageId = sendResult.MetaMessageId;
            message.RecipientPhoneE164 = phoneE164;
            message.SentAtUtc = nowUtc;
            message.ProcessedAtUtc = nowUtc;
            message.ProcessingStartedAtUtc = null;
            message.NextAttemptAtUtc = null;
            message.ErrorCode = null;
            message.ErrorMessage = null;
            message.PayloadJson = BuildSendResultPayloadJson(message, sendResult);
        }

        private static void MarkFailedOrRetry(
            WhatsAppMessageLog message,
            MetaWhatsAppSendResult sendResult)
        {
            message.PayloadJson = BuildSendResultPayloadJson(message, sendResult);
            message.ProcessingStartedAtUtc = null;
            message.MetaMessageId = null;

            var shouldRetry = sendResult.ShouldRetry && message.AttemptCount < MaxAttempts;
            if (!shouldRetry)
            {
                message.Status = WhatsAppMessageStatuses.Failed;
                message.ErrorCode = Trim(sendResult.ErrorCode ?? "SEND_ERROR", 80);
                message.ErrorMessage = Trim(sendResult.ErrorMessage ?? "No fue posible enviar el mensaje.", 1000);
                message.FailedAtUtc = DateTime.UtcNow;
                message.ProcessedAtUtc = DateTime.UtcNow;
                message.NextAttemptAtUtc = null;
                return;
            }

            message.Status = WhatsAppMessageStatuses.Pending;
            message.ErrorCode = null;
            message.ErrorMessage = null;
            message.FailedAtUtc = null;
            message.ProcessedAtUtc = null;
            message.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Min(message.AttemptCount * 2, 10));
        }

        private static void MarkSkipped(
            WhatsAppMessageLog message,
            Cita? cita,
            string status,
            string errorCode,
            string reason)
        {
            message.Status = status;
            message.ErrorCode = errorCode;
            message.ErrorMessage = Trim(reason, 1000);
            message.ProcessedAtUtc = DateTime.UtcNow;
            message.ProcessingStartedAtUtc = null;
            message.NextAttemptAtUtc = null;
            if (cita is not null)
            {
                message.PayloadJson = BuildSkippedPayloadJson(cita, message.NotificationType, errorCode);
            }
        }

        private static void ApplySentState(
            Cita cita,
            string notificationType,
            string metaMessageId)
        {
            var nowUtc = DateTime.UtcNow;
            cita.UltimoMetaMessageId = metaMessageId;

            if (notificationType == WhatsAppNotificationTypes.Confirmation)
            {
                cita.ConfirmacionEnviada = true;
                cita.ConfirmacionWhatsAppEnviadaUtc = nowUtc;
                if (!string.Equals(cita.EstadoConfirmacionWhatsApp, WhatsAppConfirmationStates.Confirmada, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(cita.EstadoConfirmacionWhatsApp, WhatsAppConfirmationStates.Cancelada, StringComparison.OrdinalIgnoreCase))
                {
                    cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.Pendiente;
                }
                return;
            }

            if (notificationType == WhatsAppNotificationTypes.Reminder3Hours)
            {
                cita.Recordatorio3hEnviado = true;
                cita.RecordatorioWhatsAppTresHorasEnviadoUtc = nowUtc;
            }
        }

        private async Task<List<WhatsAppTargetCandidate>> ResolveTargetCandidatesAsync(
            InboundWhatsAppMessage inboundMessage,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(inboundMessage.ContextMessageId))
            {
                var contextCandidates = await ResolveCandidatesByContextAsync(inboundMessage.ContextMessageId!, cancellationToken);
                if (contextCandidates.Count > 0)
                {
                    return contextCandidates;
                }
            }

            return await ResolveCandidatesByPhoneAsync(inboundMessage, cancellationToken);
        }

        private async Task<List<WhatsAppTargetCandidate>> ResolveCandidatesByContextAsync(
            string contextMessageId,
            CancellationToken cancellationToken)
        {
            var candidates = new List<WhatsAppTargetCandidate>();

            await _tenantExecutionService.RunForEachActiveTenantAsync(
                async (serviceProvider, tenantId, ct) =>
                {
                    var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
                    var match = await db.WhatsAppMessageLogs
                        .AsNoTracking()
                        .Where(message =>
                            message.MetaMessageId == contextMessageId &&
                            message.Direction == WhatsAppMessageDirections.Outbound &&
                            message.CitaId != null)
                        .Select(message => new
                        {
                            message.CitaId,
                            message.RecipientPhoneE164,
                            message.MetaMessageId
                        })
                        .FirstOrDefaultAsync(ct);

                    if (match?.CitaId is null)
                    {
                        return;
                    }

                    candidates.Add(new WhatsAppTargetCandidate(
                        tenantId,
                        match.CitaId.Value,
                        match.MetaMessageId,
                        match.RecipientPhoneE164));
                },
                cancellationToken);

            return candidates;
        }

        private async Task<List<WhatsAppTargetCandidate>> ResolveCandidatesByPhoneAsync(
            InboundWhatsAppMessage inboundMessage,
            CancellationToken cancellationToken)
        {
            var senderPhone = _metaClient.NormalizePhoneNumber(inboundMessage.From);
            if (senderPhone is null)
            {
                return [];
            }

            var now = _businessDateTimeProvider.Now();
            var upperLimit = now.AddHours(48);
            var candidates = new List<WhatsAppTargetCandidate>();

            await _tenantExecutionService.RunForEachActiveTenantAsync(
                async (serviceProvider, tenantId, ct) =>
                {
                    var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
                    var citas = await db.Citas
                        .AsNoTracking()
                        .Where(c =>
                            c.Tipo == "CITA" &&
                            c.FechaHoraCita >= now &&
                            c.FechaHoraCita <= upperLimit &&
                            c.EstadoConfirmacionWhatsApp != WhatsAppConfirmationStates.Confirmada &&
                            c.EstadoConfirmacionWhatsApp != WhatsAppConfirmationStates.Cancelada &&
                            c.TelefonoCliente != null)
                        .Select(c => new
                        {
                            c.Id,
                            c.TelefonoCliente,
                            c.FechaHoraCita
                        })
                        .ToListAsync(ct);

                    foreach (var cita in citas.OrderBy(c => c.FechaHoraCita))
                    {
                        if (string.Equals(_metaClient.NormalizePhoneNumber(cita.TelefonoCliente), senderPhone, StringComparison.Ordinal))
                        {
                            candidates.Add(new WhatsAppTargetCandidate(tenantId, cita.Id, null, senderPhone));
                        }
                    }
                },
                cancellationToken);

            return candidates;
        }

        private async Task ProcessResolvedInboundAsync(
            ApplicationDbContext db,
            InboundWhatsAppMessage inboundMessage,
            WhatsAppTargetCandidate candidate,
            WhatsAppReplyAction action,
            CancellationToken cancellationToken)
        {
            var inboundExists = await db.WhatsAppMessageLogs
                .AnyAsync(message =>
                    message.MetaMessageId == inboundMessage.MessageId &&
                    message.Direction == WhatsAppMessageDirections.Inbound,
                    cancellationToken);

            if (inboundExists)
            {
                return;
            }

            var cita = await db.Citas
                .FirstOrDefaultAsync(current => current.Id == candidate.CitaId, cancellationToken);

            if (cita is null)
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var status = WhatsAppMessageStatuses.Received;
            string? errorCode = null;
            string? errorMessage = null;

            switch (action)
            {
                case WhatsAppReplyAction.Confirm:
                    cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.Confirmada;
                    cita.ConfirmadaPorWhatsAppUtc = nowUtc;
                    cita.UltimaRespuestaWhatsAppUtc = nowUtc;
                    break;

                case WhatsAppReplyAction.Cancel:
                    cita.EstadoConfirmacionWhatsApp = WhatsAppConfirmationStates.Cancelada;
                    cita.CanceladaPorWhatsAppUtc = nowUtc;
                    cita.UltimaRespuestaWhatsAppUtc = nowUtc;
                    break;

                default:
                    status = WhatsAppMessageStatuses.Ignored;
                    errorCode = "UNRECOGNIZED_REPLY";
                    errorMessage = "Respuesta no reconocida.";
                    cita.UltimaRespuestaWhatsAppUtc = nowUtc;
                    break;
            }

            db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
            {
                CitaId = cita.Id,
                Direction = WhatsAppMessageDirections.Inbound,
                NotificationType = WhatsAppNotificationTypes.Reply,
                Provider = WhatsAppProviders.Meta,
                MetaMessageId = inboundMessage.MessageId,
                ContextMessageId = inboundMessage.ContextMessageId ?? candidate.ContextMessageId,
                SenderPhoneE164 = _metaClient.NormalizePhoneNumber(inboundMessage.From),
                RecipientPhoneE164 = candidate.RecipientPhoneE164,
                WaId = inboundMessage.WaId,
                Status = status,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                PayloadJson = BuildInboundPayloadJson(inboundMessage, action),
                CreatedAtUtc = nowUtc,
                ProcessedAtUtc = nowUtc
            });

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Respuesta WhatsApp procesada para CitaId {CitaId}. Accion {Action}.",
                cita.Id,
                action);
        }

        private async Task RegisterAmbiguousInboundAsync(
            InboundWhatsAppMessage inboundMessage,
            IReadOnlyList<WhatsAppTargetCandidate> candidates,
            CancellationToken cancellationToken)
        {
            var tenantIds = candidates
                .Select(candidate => candidate.TenantId)
                .Distinct()
                .ToList();

            if (tenantIds.Count != 1)
            {
                _logger.LogWarning(
                    "Respuesta WhatsApp ambigua entre multiples tenants. MetaMessageId {MetaMessageId}. CandidateCount {CandidateCount}.",
                    inboundMessage.MessageId,
                    candidates.Count);
                return;
            }

            await _tenantExecutionService.RunForTenantAsync(
                tenantIds[0],
                async (serviceProvider, _, ct) =>
                {
                    var db = serviceProvider.GetRequiredService<ApplicationDbContext>();
                    var exists = await db.WhatsAppMessageLogs
                        .AnyAsync(message =>
                            message.MetaMessageId == inboundMessage.MessageId &&
                            message.Direction == WhatsAppMessageDirections.Inbound,
                            ct);

                    if (exists)
                    {
                        return;
                    }

                    db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
                    {
                        Direction = WhatsAppMessageDirections.Inbound,
                        NotificationType = WhatsAppNotificationTypes.Reply,
                        Provider = WhatsAppProviders.Meta,
                        MetaMessageId = inboundMessage.MessageId,
                        ContextMessageId = inboundMessage.ContextMessageId,
                        SenderPhoneE164 = _metaClient.NormalizePhoneNumber(inboundMessage.From),
                        WaId = inboundMessage.WaId,
                        Status = WhatsAppMessageStatuses.Ignored,
                        ErrorCode = "AMBIGUOUS_REPLY",
                        ErrorMessage = "Hay mas de una cita posible para este telefono.",
                        PayloadJson = BuildInboundPayloadJson(inboundMessage, WhatsAppReplyAction.Unknown),
                        CreatedAtUtc = DateTime.UtcNow,
                        ProcessedAtUtc = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync(ct);
                },
                cancellationToken);
        }

        private static void ApplyProviderStatus(
            WhatsAppMessageLog message,
            MetaWhatsAppStatusUpdate statusUpdate)
        {
            var mappedStatus = MapProviderStatus(statusUpdate.Status);
            var eventTime = statusUpdate.TimestampUtc ?? DateTime.UtcNow;

            if (mappedStatus == WhatsAppMessageStatuses.Failed)
            {
                message.Status = WhatsAppMessageStatuses.Failed;
                message.FailedAtUtc = eventTime;
                message.ErrorCode = Trim(statusUpdate.ErrorCode, 80);
                message.ErrorMessage = Trim(statusUpdate.ErrorMessage, 1000);
                message.ProcessedAtUtc = DateTime.UtcNow;
                return;
            }

            if (mappedStatus == WhatsAppMessageStatuses.Sent &&
                (message.Status == WhatsAppMessageStatuses.Pending ||
                 message.Status == WhatsAppMessageStatuses.Processing))
            {
                message.Status = WhatsAppMessageStatuses.Sent;
            }

            if (mappedStatus == WhatsAppMessageStatuses.Sent)
            {
                message.SentAtUtc ??= eventTime;
            }
            else if (mappedStatus == WhatsAppMessageStatuses.Delivered)
            {
                message.DeliveredAtUtc ??= eventTime;
            }
            else if (mappedStatus == WhatsAppMessageStatuses.Read)
            {
                message.ReadAtUtc ??= eventTime;
            }

            message.ProcessedAtUtc = DateTime.UtcNow;
        }

        private static string MapProviderStatus(string status) =>
            NormalizeToken(status) switch
            {
                "sent" => WhatsAppMessageStatuses.Sent,
                "delivered" => WhatsAppMessageStatuses.Delivered,
                "read" => WhatsAppMessageStatuses.Read,
                "failed" => WhatsAppMessageStatuses.Failed,
                _ => WhatsAppMessageStatuses.Ignored
            };

        private static WhatsAppReplyAction ResolveReplyAction(InboundWhatsAppMessage inboundMessage)
        {
            var values = new[]
            {
                inboundMessage.Text,
                inboundMessage.ButtonText,
                inboundMessage.ButtonPayload,
                inboundMessage.InteractiveButtonId,
                inboundMessage.InteractiveButtonTitle
            };

            foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var token = NormalizeToken(value!);
                if (token is "1" or "confirmar" or "confirmo" or "si" or "confirmar_cita" or "confirm")
                {
                    return WhatsAppReplyAction.Confirm;
                }

                if (token is "2" or "cancelar" or "cancelo" or "cancelar_cita" or "cancel")
                {
                    return WhatsAppReplyAction.Cancel;
                }
            }

            return WhatsAppReplyAction.Unknown;
        }

        private static IReadOnlyList<InboundWhatsAppMessage> ExtractInboundMessages(JsonElement payload)
        {
            var messages = new List<InboundWhatsAppMessage>();

            foreach (var value in EnumerateWebhookValues(payload))
            {
                var contactsByWaId = ExtractContactsByWaId(value);
                if (!TryGetArray(value, "messages", out var messageElements))
                {
                    continue;
                }

                foreach (var message in messageElements.EnumerateArray())
                {
                    var messageId = TryGetString(message, "id");
                    if (string.IsNullOrWhiteSpace(messageId))
                    {
                        continue;
                    }

                    var from = TryGetString(message, "from");
                    var waId = from is not null && contactsByWaId.TryGetValue(from, out var contactWaId)
                        ? contactWaId
                        : from;

                    var contextMessageId = TryGetProperty(message, "context", out var context)
                        ? TryGetString(context, "id")
                        : null;

                    var text = TryGetProperty(message, "text", out var textElement)
                        ? TryGetString(textElement, "body")
                        : null;

                    string? buttonText = null;
                    string? buttonPayload = null;
                    if (TryGetProperty(message, "button", out var buttonElement))
                    {
                        buttonText = TryGetString(buttonElement, "text");
                        buttonPayload = TryGetString(buttonElement, "payload");
                    }

                    string? interactiveButtonId = null;
                    string? interactiveButtonTitle = null;
                    if (TryGetProperty(message, "interactive", out var interactiveElement) &&
                        TryGetProperty(interactiveElement, "button_reply", out var buttonReplyElement))
                    {
                        interactiveButtonId = TryGetString(buttonReplyElement, "id");
                        interactiveButtonTitle = TryGetString(buttonReplyElement, "title");
                    }

                    messages.Add(new InboundWhatsAppMessage(
                        messageId,
                        from,
                        waId,
                        contextMessageId,
                        text,
                        buttonText,
                        buttonPayload,
                        interactiveButtonId,
                        interactiveButtonTitle));
                }
            }

            return messages;
        }

        private static IReadOnlyList<MetaWhatsAppStatusUpdate> ExtractStatusUpdates(JsonElement payload)
        {
            var statuses = new List<MetaWhatsAppStatusUpdate>();

            foreach (var value in EnumerateWebhookValues(payload))
            {
                if (!TryGetArray(value, "statuses", out var statusElements))
                {
                    continue;
                }

                foreach (var status in statusElements.EnumerateArray())
                {
                    var messageId = TryGetString(status, "id");
                    if (string.IsNullOrWhiteSpace(messageId))
                    {
                        continue;
                    }

                    string? errorCode = null;
                    string? errorMessage = null;
                    if (TryGetArray(status, "errors", out var errors) && errors.GetArrayLength() > 0)
                    {
                        var firstError = errors[0];
                        errorCode = TryGetString(firstError, "code");
                        errorMessage = TryGetString(firstError, "message") ?? TryGetString(firstError, "title");
                    }

                    statuses.Add(new MetaWhatsAppStatusUpdate(
                        messageId,
                        TryGetString(status, "status") ?? string.Empty,
                        TryParseUnixTimestamp(TryGetString(status, "timestamp")),
                        TryGetString(status, "recipient_id"),
                        errorCode,
                        errorMessage));
                }
            }

            return statuses;
        }

        private static IEnumerable<JsonElement> EnumerateWebhookValues(JsonElement payload)
        {
            if (!TryGetArray(payload, "entry", out var entries))
            {
                yield break;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!TryGetArray(entry, "changes", out var changes))
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (TryGetProperty(change, "value", out var value))
                    {
                        yield return value;
                    }
                }
            }
        }

        private static Dictionary<string, string> ExtractContactsByWaId(JsonElement value)
        {
            var contacts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!TryGetArray(value, "contacts", out var contactElements))
            {
                return contacts;
            }

            foreach (var contact in contactElements.EnumerateArray())
            {
                var waId = TryGetString(contact, "wa_id");
                if (!string.IsNullOrWhiteSpace(waId))
                {
                    contacts[waId] = waId;
                }
            }

            return contacts;
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out property))
            {
                return true;
            }

            property = default;
            return false;
        }

        private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement property)
        {
            if (TryGetProperty(element, propertyName, out property) &&
                property.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            property = default;
            return false;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };
        }

        private static DateTime? TryParseUnixTimestamp(string? timestamp)
        {
            if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static string ResolveTemplateName(MetaWhatsAppOptions options, string notificationType) =>
            notificationType == WhatsAppNotificationTypes.Reminder3Hours
                ? options.ReminderTemplateName
                : options.ConfirmationTemplateName;

        private static int GetReminderLeadTimeMinutes(MetaWhatsAppOptions options) =>
            options.ReminderLeadTimeMinutes <= 0 ? 180 : options.ReminderLeadTimeMinutes;

        private static string ResolveSkippedStatus(string? errorCode) =>
            errorCode switch
            {
                WhatsAppErrorCodes.ConsentMissing => WhatsAppMessageStatuses.SkippedConsentMissing,
                WhatsAppErrorCodes.TenantDisabled => WhatsAppMessageStatuses.SkippedTenantDisabled,
                WhatsAppErrorCodes.DailyLimitExceeded => WhatsAppMessageStatuses.SkippedDailyLimitExceeded,
                WhatsAppErrorCodes.SubscriptionRequired => WhatsAppMessageStatuses.SkippedSubscriptionRequired,
                WhatsAppErrorCodes.MonthlyLimitExceeded => WhatsAppMessageStatuses.SkippedMonthlyLimitExceeded,
                WhatsAppErrorCodes.InvalidPhone => WhatsAppMessageStatuses.SkippedInvalidPhone,
                _ => WhatsAppMessageStatuses.SkippedConfiguration
            };

        private static string BuildQueuedPayloadJson(Cita cita, string notificationType) =>
            JsonSerializer.Serialize(new
            {
                phase = "queued",
                notificationType,
                citaId = cita.Id,
                citaFechaHora = cita.FechaHoraCita,
                source = ResolveConsentSource(cita),
                hasClienteId = cita.ClienteId.HasValue
            }, JsonOptions);

        private static string BuildSkippedPayloadJson(
            Cita cita,
            string notificationType,
            string reason,
            string? source = null,
            bool? hasClienteId = null) =>
            JsonSerializer.Serialize(new
            {
                phase = "skipped",
                reason,
                notificationType,
                citaId = cita.Id,
                source = source ?? ResolveConsentSource(cita),
                hasClienteId = hasClienteId ?? cita.ClienteId.HasValue
            }, JsonOptions);

        private static string BuildSendResultPayloadJson(
            WhatsAppMessageLog message,
            MetaWhatsAppSendResult sendResult) =>
            JsonSerializer.Serialize(new
            {
                phase = sendResult.Success ? "sent" : "send_failed",
                message.NotificationType,
                message.CitaId,
                sendResult.MetaMessageId,
                statusCode = sendResult.StatusCode.HasValue ? (int?)sendResult.StatusCode.Value : null,
                sendResult.ErrorCode,
                sendResult.ErrorType,
                sendResult.ErrorSubcode,
                sendResult.ErrorMessage,
                sendResult.FbTraceId,
                sendResult.ShouldRetry,
                sendResult.Endpoint,
                responseBody = sendResult.ResponseBody
            }, JsonOptions);

        private static string BuildInboundPayloadJson(
            InboundWhatsAppMessage inboundMessage,
            WhatsAppReplyAction action) =>
            JsonSerializer.Serialize(new
            {
                phase = "inbound_reply",
                inboundMessage.MessageId,
                inboundMessage.ContextMessageId,
                inboundMessage.From,
                inboundMessage.WaId,
                inboundMessage.Text,
                inboundMessage.ButtonText,
                inboundMessage.ButtonPayload,
                inboundMessage.InteractiveButtonId,
                inboundMessage.InteractiveButtonTitle,
                action = action.ToString()
            }, JsonOptions);

        private static string BuildStatusPayloadJson(MetaWhatsAppStatusUpdate statusUpdate) =>
            JsonSerializer.Serialize(new
            {
                phase = "status",
                statusUpdate.MessageId,
                statusUpdate.Status,
                statusUpdate.TimestampUtc,
                statusUpdate.RecipientPhone,
                statusUpdate.ErrorCode,
                statusUpdate.ErrorMessage
            }, JsonOptions);

        private static string ResolveConsentSource(Cita cita)
        {
            if (cita.ClienteId.HasValue)
            {
                return WhatsAppConsentSources.ClienteRegistrado;
            }

            if (!string.IsNullOrWhiteSpace(cita.WhatsAppConsentSource))
            {
                return cita.WhatsAppConsentSource!;
            }

            return cita.WhatsAppConsentAtCreation
                ? WhatsAppConsentSources.CitaManual
                : WhatsAppConsentSources.SinConsentimiento;
        }

        private static string NormalizeToken(string value)
        {
            var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace(" ", "_", StringComparison.Ordinal)
                .Replace("-", "_", StringComparison.Ordinal);
        }

        private static string? Trim(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private sealed record WhatsAppTargetCandidate(
            Guid TenantId,
            int CitaId,
            string? ContextMessageId,
            string? RecipientPhoneE164);

        private sealed record InboundWhatsAppMessage(
            string MessageId,
            string? From,
            string? WaId,
            string? ContextMessageId,
            string? Text,
            string? ButtonText,
            string? ButtonPayload,
            string? InteractiveButtonId,
            string? InteractiveButtonTitle);

        private sealed record MetaWhatsAppStatusUpdate(
            string MessageId,
            string Status,
            DateTime? TimestampUtc,
            string? RecipientPhone,
            string? ErrorCode,
            string? ErrorMessage);

        private sealed record WhatsAppConsentDecision(
            bool CanSend,
            string Source,
            bool HasClienteId,
            string Message);

        private enum WhatsAppReplyAction
        {
            Unknown,
            Confirm,
            Cancel
        }
    }
}
