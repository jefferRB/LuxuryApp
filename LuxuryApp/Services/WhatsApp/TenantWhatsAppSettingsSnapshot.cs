using LuxuryApp.Models.WhatsApp;

namespace LuxuryApp.Services.WhatsApp
{
    public sealed record TenantWhatsAppSettingsSnapshot(
        Guid TenantId,
        bool Exists,
        bool IsEnabled,
        bool SendConfirmationOnCreate,
        bool SendReminderThreeHoursBefore,
        int DailyMessageLimit,
        string TimeZoneId,
        string? Notes)
    {
        public static TenantWhatsAppSettingsSnapshot CreateDefault(Guid tenantId) =>
            new(
                tenantId,
                Exists: false,
                IsEnabled: false,
                SendConfirmationOnCreate: true,
                SendReminderThreeHoursBefore: true,
                TenantWhatsAppSettings.DefaultDailyMessageLimit,
                TenantWhatsAppSettings.DefaultTimeZoneId,
                Notes: null);

        public static TenantWhatsAppSettingsSnapshot CreateEnabledDefaultsForAddon(
            Guid tenantId,
            int dailyMessageLimit) =>
            new(
                tenantId,
                Exists: false,
                IsEnabled: true,
                SendConfirmationOnCreate: true,
                SendReminderThreeHoursBefore: true,
                dailyMessageLimit > 0 ? dailyMessageLimit : TenantWhatsAppSettings.DefaultDailyMessageLimit,
                TenantWhatsAppSettings.DefaultTimeZoneId,
                Notes: null);
    }
}
