namespace LuxuryApp.Models.Calendar
{
    public sealed class CalendarIndexViewModel
    {
        public bool TenantWhatsAppEnabled { get; init; }

        public CalendarHeaderStatsResponse Stats { get; init; } = new();
    }
}
