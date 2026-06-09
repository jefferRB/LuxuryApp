using LuxuryApp.Models.SaaS;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.SaaS;
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
            WhatsAppMessageStatuses.Sent,
            WhatsAppMessageStatuses.Delivered,
            WhatsAppMessageStatuses.Read
        ];

        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly IOptionsMonitor<MetaWhatsAppOptions> _options;
        private readonly SuscripcionService _suscripcionService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantCommercialAccessResolver _commercialAccessResolver;
        private readonly ILogger<TenantWhatsAppSettingsService> _logger;

        public TenantWhatsAppSettingsService(
            ApplicationDbContext context,
            ITenantProvider tenantProvider,
            IOptionsMonitor<MetaWhatsAppOptions> options,
            SuscripcionService suscripcionService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantCommercialAccessResolver commercialAccessResolver,
            ILogger<TenantWhatsAppSettingsService> logger)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _options = options;
            _suscripcionService = suscripcionService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _commercialAccessResolver = commercialAccessResolver;
            _logger = logger;
        }

        public async Task<TenantWhatsAppSettingsSnapshot> GetSettingsForTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentTenant(tenantId);

            var addon = await GetActiveAddonAsync(tenantId, cancellationToken);
            var settings = await _context.TenantWhatsAppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            return CreateSnapshot(tenantId, settings, addon);
        }

        public async Task<TenantWhatsAppSettingsSnapshot> EnsureDefaultSettingsAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            EnsureCurrentTenant(tenantId);

            var addon = await GetActiveAddonAsync(tenantId, cancellationToken);
            var settings = await _context.TenantWhatsAppSettings
                .FirstOrDefaultAsync(current => current.TenantId == tenantId, cancellationToken);

            if (settings is not null)
            {
                return CreateSnapshot(tenantId, settings, addon);
            }

            var nowUtc = _businessDateTimeProvider.NowOffset().UtcDateTime;
            var defaultDailyLimit = _suscripcionService.ResolveWhatsAppDailyMessageLimit(
                addon,
                TenantWhatsAppSettings.DefaultDailyMessageLimit);
            settings = new TenantWhatsAppSettings
            {
                TenantId = tenantId,
                IsEnabled = addon is not null,
                SendConfirmationOnCreate = true,
                SendReminderThreeHoursBefore = true,
                DailyMessageLimit = defaultDailyLimit,
                TimeZoneId = TenantWhatsAppSettings.DefaultTimeZoneId,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            _context.TenantWhatsAppSettings.Add(settings);
            await _context.SaveChangesAsync(cancellationToken);
            return CreateSnapshot(tenantId, settings, addon);
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

            if (!_options.CurrentValue.Enabled)
            {
                var currentSettings = await GetSettingsForTenantAsync(tenantId, cancellationToken);
                var currentTodayUsage = await GetTodayUsageAsync(
                    tenantId,
                    currentSettings.TimeZoneId,
                    reservedMessageLogId,
                    cancellationToken);

                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.ConfigurationDisabled,
                    "Meta WhatsApp esta deshabilitado globalmente.",
                    currentTodayUsage,
                    currentSettings.DailyMessageLimit);
            }

            var addon = await GetActiveAddonAsync(tenantId, cancellationToken);
            var settings = await GetSettingsForTenantAsync(tenantId, cancellationToken);
            var todayUsage = await GetTodayUsageAsync(
                tenantId,
                settings.TimeZoneId,
                reservedMessageLogId,
                cancellationToken);

            if (addon is null)
            {
                _logger.LogWarning(
                    "WhatsApp sin add-on activo. TenantId {TenantId}. NotificationType {NotificationType}.",
                    tenantId,
                    notificationType);

                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.NoActiveWhatsAppAddon,
                    "Tu cuenta necesita un paquete activo de WhatsApp para enviar recordatorios automaticos.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            var commercialAccess = await _commercialAccessResolver.ResolveAsync(tenantId, cancellationToken: cancellationToken);
            if (!commercialAccess.CanAccessApp)
            {
                _logger.LogWarning(
                    "WhatsApp sin plan base valido. TenantId {TenantId}. Razon: {Reason}. NotificationType {NotificationType}.",
                    tenantId,
                    commercialAccess.Reason,
                    notificationType);

                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.NoActiveBaseSubscription,
                    "Tu cuenta necesita un plan base activo de LuxuryCloud antes de enviar automatizaciones de WhatsApp.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            if (commercialAccess.AccessSource is TenantCommercialAccessSource.TenantExempt or TenantCommercialAccessSource.TenantInternal)
            {
                _logger.LogInformation(
                    "WhatsApp: suscripcion base valida por acceso patrocinado/exento con plan forzado. TenantId {TenantId}. Plan {Plan}. NotificationType {NotificationType}.",
                    tenantId,
                    commercialAccess.EffectivePlanName,
                    notificationType);
            }
            else if (commercialAccess.AccessSource == TenantCommercialAccessSource.PromotionalGrant)
            {
                _logger.LogInformation(
                    "WhatsApp: suscripcion base valida por acceso comercial temporal. TenantId {TenantId}. Plan {Plan}. NotificationType {NotificationType}.",
                    tenantId,
                    commercialAccess.EffectivePlanName,
                    notificationType);
            }
            else
            {
                _logger.LogDebug(
                    "WhatsApp: suscripcion base valida por suscripcion activa pagada. TenantId {TenantId}. Plan {Plan}. NotificationType {NotificationType}.",
                    tenantId,
                    commercialAccess.EffectivePlanName,
                    notificationType);
            }

            var monthlyLimit = addon.MonthlyMessageLimit > 0
                ? addon.MonthlyMessageLimit
                : addon.Plan?.LimiteMensajesMensual ?? 0;
            var monthlyUsage = monthlyLimit <= 0
                ? 0
                : await _suscripcionService.GetWhatsAppUsageInCurrentPeriodAsync(
                    tenantId,
                    addon.FechaInicio,
                    addon.FechaFin,
                    cancellationToken);

            if (monthlyLimit <= 0)
            {
                _logger.LogWarning(
                    "WhatsApp add-on sin limite mensual configurado. TenantId {TenantId}. AddonId {AddonId}.",
                    tenantId,
                    addon.Id);

                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.ConfigurationDisabled,
                    "El paquete activo de WhatsApp no tiene un limite mensual valido configurado.",
                    todayUsage,
                    settings.DailyMessageLimit);
            }

            if (!settings.IsEnabled)
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.TenantDisabled,
                    "WhatsApp esta deshabilitado para el tenant.",
                    todayUsage,
                    settings.DailyMessageLimit,
                    monthlyUsage,
                    monthlyLimit);
            }

            if (notificationType == WhatsAppNotificationTypes.Confirmation &&
                (!settings.SendConfirmationOnCreate || !_options.CurrentValue.SendConfirmationOnCreate))
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.UserDisabled,
                    "Las confirmaciones de WhatsApp estan deshabilitadas para el tenant.",
                    todayUsage,
                    settings.DailyMessageLimit,
                    monthlyUsage,
                    monthlyLimit);
            }

            if (notificationType == WhatsAppNotificationTypes.Reminder3Hours &&
                (!settings.SendReminderThreeHoursBefore || !_options.CurrentValue.SendReminderBeforeAppointment))
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.UserDisabled,
                    "Los recordatorios de WhatsApp estan deshabilitados para el tenant.",
                    todayUsage,
                    settings.DailyMessageLimit,
                    monthlyUsage,
                    monthlyLimit);
            }

            if (monthlyUsage >= monthlyLimit)
            {
                _logger.LogWarning(
                    "WhatsApp sin saldo mensual disponible. TenantId {TenantId}. Used {Used}. Limit {Limit}.",
                    tenantId,
                    monthlyUsage,
                    monthlyLimit);

                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.MonthlyLimitExceeded,
                    "Tu paquete actual de WhatsApp ya consumio todos los mensajes de este periodo mensual.",
                    todayUsage,
                    settings.DailyMessageLimit,
                    monthlyUsage,
                    monthlyLimit);
            }

            if (todayUsage >= settings.DailyMessageLimit)
            {
                return TenantWhatsAppSendDecision.Denied(
                    WhatsAppErrorCodes.DailyLimitExceeded,
                    "El tenant alcanzo su limite diario de mensajes WhatsApp.",
                    todayUsage,
                    settings.DailyMessageLimit,
                    monthlyUsage,
                    monthlyLimit);
            }

            return TenantWhatsAppSendDecision.Allowed(
                todayUsage,
                settings.DailyMessageLimit,
                monthlyUsage,
                monthlyLimit);
        }

        public Task<int> GetTodayUsageAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default) =>
            GetTodayUsageForCurrentTenantAsync(tenantId, reservedMessageLogId: null, cancellationToken);

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

        private async Task<int> GetTodayUsageForCurrentTenantAsync(
            Guid tenantId,
            long? reservedMessageLogId,
            CancellationToken cancellationToken)
        {
            EnsureCurrentTenant(tenantId);

            var settings = await GetSettingsForTenantAsync(tenantId, cancellationToken);
            return await GetTodayUsageAsync(
                tenantId,
                settings.TimeZoneId,
                reservedMessageLogId,
                cancellationToken);
        }

        private async Task<int> GetTodayUsageAsync(
            Guid tenantId,
            string timeZoneId,
            long? reservedMessageLogId,
            CancellationToken cancellationToken)
        {
            var timeZone = ResolveTimeZone(timeZoneId);
            var tenantNow = TimeZoneInfo.ConvertTime(_businessDateTimeProvider.NowOffset(), timeZone);
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
                    (message.SentAtUtc ?? message.DeliveredAtUtc ?? message.ReadAtUtc ?? message.CreatedAtUtc) >= startUtc &&
                    (message.SentAtUtc ?? message.DeliveredAtUtc ?? message.ReadAtUtc ?? message.CreatedAtUtc) < endUtc);

            if (reservedMessageLogId.HasValue)
            {
                usageQuery = usageQuery.Where(message => message.Id != reservedMessageLogId.Value);
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

        private TenantWhatsAppSettingsSnapshot CreateSnapshot(
            Guid tenantId,
            TenantWhatsAppSettings? settings,
            TenantSubscriptionAddon? addon)
        {
            var effectiveDailyLimit = settings is not null && settings.DailyMessageLimit > 0
                ? settings.DailyMessageLimit
                : _suscripcionService.ResolveWhatsAppDailyMessageLimit(
                    addon,
                    TenantWhatsAppSettings.DefaultDailyMessageLimit);

            if (settings is null)
            {
                return addon is null
                    ? TenantWhatsAppSettingsSnapshot.CreateDefault(tenantId)
                    : TenantWhatsAppSettingsSnapshot.CreateEnabledDefaultsForAddon(tenantId, effectiveDailyLimit);
            }

            return new TenantWhatsAppSettingsSnapshot(
                settings.TenantId,
                Exists: true,
                IsEnabled: settings.IsEnabled,
                SendConfirmationOnCreate: settings.SendConfirmationOnCreate,
                SendReminderThreeHoursBefore: settings.SendReminderThreeHoursBefore,
                DailyMessageLimit: effectiveDailyLimit,
                TimeZoneId: string.IsNullOrWhiteSpace(settings.TimeZoneId)
                    ? TenantWhatsAppSettings.DefaultTimeZoneId
                    : settings.TimeZoneId,
                Notes: settings.Notes);
        }

        private async Task<TenantSubscriptionAddon?> GetActiveAddonAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            var addon = await _context.TenantSubscriptionAddons
                .AsNoTracking()
                .Include(current => current.Plan)
                .Where(current => current.TenantId == tenantId)
                .OrderByDescending(current => current.UpdatedAtUtc)
                .ThenByDescending(current => current.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            return addon is not null && _suscripcionService.IsWhatsAppAddonActive(addon)
                ? addon
                : null;
        }

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
