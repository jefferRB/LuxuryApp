using LuxuryApp.Models.SaaS;

namespace LuxuryApp.Models.Platform
{
    public sealed class PlatformTenantRowViewModel
    {
        public Guid TenantId { get; init; }
        public string TenantName { get; init; } = string.Empty;
        public bool TenantActive { get; init; }
        public TenantCommercialAccessMode CommercialAccessMode { get; init; }
        public Guid? ForcedPlanId { get; init; }
        public string? ForcedPlanName { get; init; }
        public string? OwnerEmail { get; init; }
        public string? CommercialNotes { get; init; }
        public bool CanAccessApp { get; init; }
        public string? EffectivePlanName { get; init; }
        public string Reason { get; init; } = string.Empty;
        public bool WhatsAppEnabled { get; init; }
        public bool SendWhatsAppConfirmationOnCreate { get; init; }
        public bool SendWhatsAppReminderThreeHoursBefore { get; init; }
        public int WhatsAppDailyMessageLimit { get; init; }
        public int WhatsAppTodayUsage { get; init; }
        public string WhatsAppTimeZoneId { get; init; } = string.Empty;
        public string? WhatsAppNotes { get; init; }
        public string? WhatsAppLastErrorCode { get; init; }
        public string? WhatsAppLastErrorMessage { get; init; }
        public DateTime? WhatsAppLastErrorAtUtc { get; init; }
        public string? WhatsAppAddonCode { get; init; }
        public bool WhatsAppAddonIsManual { get; init; }
        public DateTime? WhatsAppAddonFechaFin { get; init; }
        public int? WhatsAppAddonMonthlyLimit { get; init; }
    }
}
