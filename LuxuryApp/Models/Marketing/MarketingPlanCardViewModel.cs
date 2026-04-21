namespace LuxuryApp.Models.Marketing
{
    public sealed class MarketingPlanCardViewModel
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string BillingLabel { get; init; } = string.Empty;
        public decimal MonthlyPrice { get; init; }
        public string CurrencyCode { get; init; } = "CRC";
        public string StaffLabel { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string? BadgeText { get; init; }
        public bool IsFeatured { get; init; }
        public bool IsValidationPlan { get; init; }
        public IReadOnlyCollection<string> Highlights { get; init; } = Array.Empty<string>();
    }
}
