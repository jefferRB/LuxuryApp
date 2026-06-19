namespace LuxuryApp.Models.Platform
{
    public sealed class PlatformWhatsAppAddonState
    {
        // From TenantWhatsAppSettings
        public bool SettingsEnabled { get; init; }
        public bool SendConfirmationOnCreate { get; init; }
        public bool SendReminderThreeHoursBefore { get; init; }
        public int DailyMessageLimit { get; init; }
        public int TodayUsage { get; init; }
        public int MonthlyUsage30d { get; init; }
        public string TimeZoneId { get; init; } = "America/Costa_Rica";
        public string? Notes { get; init; }

        // From TenantSubscriptionAddons
        public bool AddonActive { get; init; }
        public string? AddonCode { get; init; }
        public bool AddonIsManual { get; init; }
        public DateTime? AddonFechaFin { get; init; }
        public int? AddonMonthlyLimit { get; init; }

        // From WhatsAppMessageLogs
        public string? LastErrorCode { get; init; }
        public string? LastErrorMessage { get; init; }
        public DateTime? LastErrorAtUtc { get; init; }
        public DateTime? LastMessageSentUtc { get; init; }
    }
}
