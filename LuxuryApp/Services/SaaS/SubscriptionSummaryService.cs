using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.WhatsApp;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.SaaS
{
    /// <summary>
    /// Implementacion extraida desde BillingController.BuildCurrentSubscriptionSummaryAsync.
    /// Mantiene exactamente el mismo calculo (queries tenant-safe con IgnoreQueryFilters +
    /// filtro explicito por TenantId) para no alterar el comportamiento existente.
    /// </summary>
    public sealed class SubscriptionSummaryService : ISubscriptionSummaryService
    {
        private readonly ApplicationDbContext _context;
        private readonly SuscripcionService _suscripcionService;
        private readonly ITenantWhatsAppSettingsService _tenantWhatsAppSettingsService;

        public SubscriptionSummaryService(
            ApplicationDbContext context,
            SuscripcionService suscripcionService,
            ITenantWhatsAppSettingsService tenantWhatsAppSettingsService)
        {
            _context = context;
            _suscripcionService = suscripcionService;
            _tenantWhatsAppSettingsService = tenantWhatsAppSettingsService;
        }

        public async Task<BillingSubscriptionSummaryViewModel?> BuildAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            var subscription = await _context.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .ThenByDescending(s => s.FechaInicio)
                .FirstOrDefaultAsync(cancellationToken);

            var addon = await _context.TenantSubscriptionAddons
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(a => a.Plan)
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.UpdatedAtUtc)
                .ThenByDescending(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription is null && addon is null)
            {
                return null;
            }

            var activeFuncionarios = await _context.Funcionarios
                .AsNoTracking()
                .CountAsync(funcionario => funcionario.Activo, cancellationToken);

            var subscriptionStatus = subscription is null
                ? (EstadoSuscripcion?)null
                : _suscripcionService.GetEffectiveStatus(subscription);

            var addonStatus = addon is null
                ? (EstadoSuscripcion?)null
                : _suscripcionService.GetEffectiveStatus(addon);
            var hasActiveWhatsAppAddon = addon is not null &&
                                         addonStatus is EstadoSuscripcion.Activa or EstadoSuscripcion.Morosa or EstadoSuscripcion.Trial;
            var whatsAppSettings = hasActiveWhatsAppAddon
                ? await _tenantWhatsAppSettingsService.GetSettingsForTenantAsync(tenantId, cancellationToken)
                : null;

            var whatsAppUsage = addon is null
                ? 0
                : await _suscripcionService.GetWhatsAppUsageInCurrentPeriodAsync(
                    tenantId,
                    addon.FechaInicio,
                    addon.FechaFin,
                    cancellationToken);
            var todaysWhatsAppUsage = hasActiveWhatsAppAddon
                ? await _tenantWhatsAppSettingsService.GetTodayUsageAsync(tenantId, cancellationToken)
                : 0;

            return new BillingSubscriptionSummaryViewModel
            {
                PlanName = subscription?.Plan?.Nombre,
                PlanCode = subscription?.CodigoPlan ?? subscription?.Plan?.Codigo,
                Status = subscriptionStatus,
                StatusLabel = ResolveStatusLabel(subscriptionStatus),
                StatusTone = ResolveStatusTone(subscriptionStatus),
                CanAccessApp = subscription is not null && _suscripcionService.CanAccessApp(subscription),
                IsInGracePeriod = subscriptionStatus == EstadoSuscripcion.Morosa,
                CurrentPeriodEndUtc = subscription?.FechaFin,
                NextBillingDateUtc = subscription?.FechaProximoCobroUtc,
                GracePeriodEndsUtc = subscription?.FechaFinGraciaUtc,
                MaxFuncionarios = subscription?.MaxFuncionarios ?? subscription?.Plan?.MaxFuncionarios,
                ActiveFuncionarios = activeFuncionarios,
                WhatsAppAddonName = hasActiveWhatsAppAddon ? addon?.Plan?.Nombre : null,
                WhatsAppAddonCode = hasActiveWhatsAppAddon ? addon?.AddonCode ?? addon?.Plan?.Codigo : null,
                WhatsAppAddonStatus = hasActiveWhatsAppAddon ? addonStatus : null,
                WhatsAppAddonStatusLabel = hasActiveWhatsAppAddon ? ResolveStatusLabel(addonStatus) : null,
                WhatsAppMonthlyLimit = !hasActiveWhatsAppAddon || addon is null
                    ? null
                    : addon.MonthlyMessageLimit > 0
                        ? addon.MonthlyMessageLimit
                        : addon.Plan?.LimiteMensajesMensual,
                WhatsAppMessagesUsed = hasActiveWhatsAppAddon ? whatsAppUsage : 0,
                WhatsAppMessagesRemaining = !hasActiveWhatsAppAddon || addon is null
                    ? null
                    : Math.Max(
                        (addon.MonthlyMessageLimit > 0
                            ? addon.MonthlyMessageLimit
                            : addon.Plan?.LimiteMensajesMensual ?? 0) - whatsAppUsage,
                        0),
                WhatsAppTodayUsage = hasActiveWhatsAppAddon ? todaysWhatsAppUsage : 0,
                WhatsAppDailyLimit = hasActiveWhatsAppAddon ? whatsAppSettings?.DailyMessageLimit : null,
                WhatsAppAutomationEnabled = hasActiveWhatsAppAddon && (whatsAppSettings?.IsEnabled ?? false),
                SendAppointmentConfirmations = hasActiveWhatsAppAddon && (whatsAppSettings?.SendConfirmationOnCreate ?? false),
                SendAppointmentReminders = hasActiveWhatsAppAddon && (whatsAppSettings?.SendReminderThreeHoursBefore ?? false),
                ConfirmationHoursBefore = whatsAppSettings?.ConfirmationHoursBefore ?? Models.WhatsApp.TenantWhatsAppSettings.DefaultConfirmationHoursBefore,
                SendConfirmationImmediatelyIfInsideWindow = whatsAppSettings?.SendConfirmationImmediatelyIfInsideWindow ?? true,
                ReminderHoursBefore = whatsAppSettings?.ReminderHoursBefore ?? Models.WhatsApp.TenantWhatsAppSettings.DefaultReminderHoursBefore,
                SendReminderImmediatelyIfInsideWindow = whatsAppSettings?.SendReminderImmediatelyIfInsideWindow ?? true,
                ConfirmationIsBatch = whatsAppSettings is not null &&
                    !string.Equals(whatsAppSettings.ConfirmationScheduleMode, Models.WhatsApp.WhatsAppConfirmationScheduleModes.RelativeBeforeAppointment, StringComparison.Ordinal),
                ConfirmationBatchTime = whatsAppSettings?.ConfirmationBatchTime,
                ConfirmationBatchTarget = whatsAppSettings?.ConfirmationBatchTarget ?? Models.WhatsApp.WhatsAppConfirmationBatchTargets.TomorrowAllDay,
                ConfirmationMorningStart = whatsAppSettings?.ConfirmationMorningStart,
                ConfirmationMorningEnd = whatsAppSettings?.ConfirmationMorningEnd,
                ReminderIsBatch = whatsAppSettings is not null &&
                    string.Equals(whatsAppSettings.ReminderScheduleMode, Models.WhatsApp.WhatsAppReminderScheduleModes.DailyBatchSameDay, StringComparison.Ordinal),
                ReminderBatchTime = whatsAppSettings?.ReminderBatchTime,
                ReminderBatchTarget = whatsAppSettings?.ReminderBatchTarget ?? Models.WhatsApp.WhatsAppReminderBatchTargets.SameDayRemaining,
                QuietHoursEnabled = whatsAppSettings?.QuietHoursEnabled ?? false,
                QuietHoursStart = whatsAppSettings?.QuietHoursStart,
                QuietHoursEnd = whatsAppSettings?.QuietHoursEnd,
                WhatsAppNextBillingDateUtc = hasActiveWhatsAppAddon ? addon?.FechaProximoCobroUtc : null
            };
        }

        private static string ResolveStatusLabel(EstadoSuscripcion? status) =>
            status switch
            {
                EstadoSuscripcion.Trial => "Trial",
                EstadoSuscripcion.Activa => "Activo",
                EstadoSuscripcion.Morosa => "En gracia",
                EstadoSuscripcion.Suspendida => "Suspendido",
                EstadoSuscripcion.Cancelada => "Cancelado",
                EstadoSuscripcion.Pendiente => "Pendiente",
                EstadoSuscripcion.Fallida => "Fallido",
                EstadoSuscripcion.Vencida => "Vencido",
                _ => "Sin suscripcion"
            };

        private static string ResolveStatusTone(EstadoSuscripcion? status) =>
            status switch
            {
                EstadoSuscripcion.Trial => "info",
                EstadoSuscripcion.Activa => "success",
                EstadoSuscripcion.Morosa => "warning",
                EstadoSuscripcion.Suspendida => "danger",
                EstadoSuscripcion.Cancelada => "secondary",
                EstadoSuscripcion.Pendiente => "secondary",
                EstadoSuscripcion.Fallida => "danger",
                EstadoSuscripcion.Vencida => "warning",
                _ => "secondary"
            };
    }
}
