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
    }
}
