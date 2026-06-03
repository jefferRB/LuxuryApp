using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.WhatsApp
{
    public sealed class TenantWhatsAppSettingsService : ITenantWhatsAppSettingsService
    {
        private static readonly string[] CountedStatuses =
        [
            WhatsAppMessageStatuses.Pending,
            WhatsAppMessageStatuses.Processing,
            WhatsAppMessageStatuses.Sent
        ];

        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly IOptionsMonitor<MetaWhatsAppOptions> _options;
        private readonly ILogger<TenantWhatsAppSettingsService> _logger;

        public TenantWhatsAppSettingsService(
            ApplicationDbContext context,
            ITenantProvider tenantProvider,
            IOptionsMonitor<MetaWhatsAppOptions> options,
            ILogger<TenantWhatsAppSettingsService> logger)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _options = options;
            _logger = logger;
        }

        public async Task<TenantWhatsAppSettingsSnapshot> GetSettingsForTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentTenant(tenantId);

            var settings = await _context.TenantWhatsAppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            return settings is null
                ? TenantWhatsAppSettingsSnapshot.CreateDefault(tenantId)
                : ToSnapshot(settings);
        }

        public async Task<TenantWhatsAppSettingsSnapshot> EnsureDefaultSettingsAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentTenant(tenantId);

            var settings = await _context.TenantWhatsAppSettings
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (settings is not null)
            {
                return ToSnapshot(settings);
            }

            settings = new TenantWhatsAppSettings
            {
                TenantId = tenantId
            };

            _context.TenantWhatsAppSettings.Add(settings);
            await _context.SaveChangesAsync(cancellationToken);
            return ToSnapshot(settings);
        }

        public async Task<bool> IsWhatsAppEnabledForTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            var settings = await GetSettingsForTenantAsync(tenantId, cancellationToken);
            return settings.IsEnabled;
        }

        public async Task<TenantWhatsAppSendDecision> CanSendNotificationAsync(
            Guid tenantId,
            string notificationType,
            long? reservedMessageLogId = null,
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentTenant(tenantId);

            var settings = await GetSettingsForTenantAsync(tenantId, cancellationToken);
            var todayUsage = await GetTodayUsageAsync(tenantId, reservedMessageLogId, cancellationToken);

            if (!_options.CurrentValue.Enabled)
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.ConfigurationDisabled,
                    "Meta WhatsApp esta deshabilitado globalmente.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            if (!settings.IsEnabled)
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.TenantDisabled,
                    "WhatsApp esta deshabilitado para el tenant.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            if (notificationType == WhatsAppNotificationTypes.Confirmation &&
                (!settings.SendConfirmationOnCreate || !_options.CurrentValue.SendConfirmationOnCreate))
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.NotificationTypeDisabled,
                    "Las confirmaciones de WhatsApp estan deshabilitadas para el tenant.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            if (notificationType == WhatsAppNotificationTypes.Reminder3Hours &&
                (!settings.SendReminderThreeHoursBefore || !_options.CurrentValue.SendReminderBeforeAppointment))
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.NotificationTypeDisabled,
                    "Los recordatorios de WhatsApp estan deshabilitados para el tenant.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            if (todayUsage >= settings.DailyMessageLimit)
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.DailyLimitExceeded,
                    "El tenant alcanzo su limite diario de mensajes WhatsApp.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            return TenantWhatsAppSendDecision.Allowed(todayUsage, settings.DailyMessageLimit);
        }

        public Task<int> GetTodayUsageAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            GetTodayUsageAsync(tenantId, reservedMessageLogId: null, cancellationToken);

        public async Task UpdateSettingsAsync(
            Guid tenantId,
            TenantWhatsAppSettingsUpdateDto dto,
            string? updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentTenant(tenantId);
            ArgumentNullException.ThrowIfNull(dto);

            if (dto.DailyMessageLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dto.DailyMessageLimit), "El limite diario no puede ser negativo.");
            }

            var timeZoneId = NormalizeTimeZoneId(dto.TimeZoneId);
            var settings = await _context.TenantWhatsAppSettings
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (settings is null)
            {
                settings = new TenantWhatsAppSettings
                {
                    TenantId = tenantId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.TenantWhatsAppSettings.Add(settings);
            }

            settings.IsEnabled = dto.IsEnabled;
            settings.SendConfirmationOnCreate = dto.SendConfirmationOnCreate;
            settings.SendReminderThreeHoursBefore = dto.SendReminderThreeHoursBefore;
            settings.DailyMessageLimit = dto.DailyMessageLimit;
            settings.TimeZoneId = timeZoneId;
            settings.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            settings.UpdatedAtUtc = DateTime.UtcNow;
            settings.UpdatedByUserId = string.IsNullOrWhiteSpace(updatedByUserId) ? null : updatedByUserId;

            await _context.SaveChangesAsync(cancellationToken);

            if (!settings.IsEnabled)
            {
                var nowUtc = DateTime.UtcNow;
                await _context.WhatsAppMessageLogs
                    .Where(message =>
                        message.Direction == WhatsAppMessageDirections.Outbound &&
                        (message.Status == WhatsAppMessageStatuses.Pending ||
                         message.Status == WhatsAppMessageStatuses.Processing))
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(message => message.Status, WhatsAppMessageStatuses.SkippedTenantDisabled)
                        .SetProperty(message => message.ErrorCode, WhatsAppErrorCodes.TenantDisabled)
                        .SetProperty(message => message.ErrorMessage, "WhatsApp fue deshabilitado para el tenant antes de procesar el mensaje.")
                        .SetProperty(message => message.ProcessedAtUtc, nowUtc)
                        .SetProperty(message => message.ProcessingStartedAtUtc, (DateTime?)null)
                        .SetProperty(message => message.NextAttemptAtUtc, (DateTime?)null),
                        cancellationToken);
            }
        }

        private async Task<int> GetTodayUsageAsync(
            Guid tenantId,
            long? reservedMessageLogId,
            CancellationToken cancellationToken)
        {
            EnsureCurrentTenant(tenantId);

            var settings = await GetSettingsForTenantAsync(tenantId, cancellationToken);
            var timeZone = ResolveTimeZone(settings.TimeZoneId);
            var tenantNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(tenantNow.Date, DateTimeKind.Unspecified),
                timeZone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(tenantNow.Date.AddDays(1), DateTimeKind.Unspecified),
                timeZone);

            var usageQuery = _context.WhatsAppMessageLogs
                .AsNoTracking()
                .Where(message =>
                    message.TenantId == tenantId &&
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    (message.NotificationType == WhatsAppNotificationTypes.Confirmation ||
                     message.NotificationType == WhatsAppNotificationTypes.Reminder3Hours) &&
                    CountedStatuses.Contains(message.Status) &&
                    message.CreatedAtUtc >= startUtc &&
                    message.CreatedAtUtc < endUtc);

            if (reservedMessageLogId.HasValue)
            {
                var reservedMessage = await _context.WhatsAppMessageLogs
                    .AsNoTracking()
                    .Where(message => message.Id == reservedMessageLogId.Value)
                    .Select(message => new
                    {
                        message.Id,
                        message.CreatedAtUtc
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (reservedMessage is not null)
                {
                    usageQuery = usageQuery.Where(message =>
                        message.CreatedAtUtc < reservedMessage.CreatedAtUtc ||
                        (message.CreatedAtUtc == reservedMessage.CreatedAtUtc &&
                         message.Id < reservedMessage.Id));
                }
            }

            return await usageQuery.CountAsync(cancellationToken);
        }

        private void EnsureCurrentTenant(Guid tenantId)
        {
            if (tenantId == Guid.Empty)
            {
                throw new ArgumentException("El TenantId de WhatsApp no puede estar vacio.", nameof(tenantId));
            }

            if (!_tenantProvider.HasTenant() || _tenantProvider.GetTenantId() != tenantId)
            {
                throw new InvalidOperationException("La configuracion de WhatsApp solo puede consultarse dentro del contexto de su tenant.");
            }
        }

        private static TenantWhatsAppSettingsSnapshot ToSnapshot(TenantWhatsAppSettings settings) =>
            new(
                settings.TenantId,
                Exists: true,
                settings.IsEnabled,
                settings.SendConfirmationOnCreate,
                settings.SendReminderThreeHoursBefore,
                settings.DailyMessageLimit,
                settings.TimeZoneId,
                settings.Notes);

        private string NormalizeTimeZoneId(string? configuredTimeZoneId)
        {
            var timeZoneId = string.IsNullOrWhiteSpace(configuredTimeZoneId)
                ? TenantWhatsAppSettings.DefaultTimeZoneId
                : configuredTimeZoneId.Trim();

            var resolved = ResolveTimeZone(timeZoneId);
            return resolved.Id;
        }

        private TimeZoneInfo ResolveTimeZone(string? configuredTimeZoneId)
        {
            var candidates = new[]
            {
                configuredTimeZoneId,
                TenantWhatsAppSettings.DefaultTimeZoneId,
                "Central America Standard Time",
                TimeZoneInfo.Utc.Id
            };

            foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(candidate!);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            _logger.LogWarning(
                "No fue posible resolver la zona horaria WhatsApp {TimeZoneId}. Se usara UTC.",
                configuredTimeZoneId);
            return TimeZoneInfo.Utc;
        }
    }
}
