namespace LuxuryApp.Services.WhatsApp
{
    public sealed record TenantWhatsAppSendDecision(
        bool CanSend,
        string? ErrorCode,
        string? ErrorMessage,
        int TodayUsage,
        int DailyMessageLimit)
    {
        public static TenantWhatsAppSendDecision Allowed(int todayUsage, int dailyMessageLimit) =>
            new(true, null, null, todayUsage, dailyMessageLimit);

        public static TenantWhatsAppSendDecision Denied(
            string errorCode,
            string errorMessage,
            int todayUsage,
            int dailyMessageLimit) =>
            new(false, errorCode, errorMessage, todayUsage, dailyMessageLimit);
    }
}
