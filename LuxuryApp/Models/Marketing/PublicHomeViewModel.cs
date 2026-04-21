namespace LuxuryApp.Models.Marketing
{
    public sealed class PublicHomeViewModel
    {
        public IReadOnlyCollection<MarketingMetricViewModel> HeroMetrics { get; init; } = Array.Empty<MarketingMetricViewModel>();
        public IReadOnlyCollection<MarketingModuleViewModel> Modules { get; init; } = Array.Empty<MarketingModuleViewModel>();
        public IReadOnlyCollection<MarketingPlanCardViewModel> Plans { get; init; } = Array.Empty<MarketingPlanCardViewModel>();
    }
}
