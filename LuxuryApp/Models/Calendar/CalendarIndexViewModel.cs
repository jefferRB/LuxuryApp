namespace LuxuryApp.Models.Calendar
{
    public sealed class CalendarIndexViewModel
    {
        public bool HasWhatsAppAddon { get; init; }

        public bool TenantWhatsAppEnabled { get; init; }

        public CalendarHeaderStatsResponse Stats { get; init; } = new();
    }
}
