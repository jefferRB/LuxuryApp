using LuxuryApp.Models.Marketing;

namespace LuxuryApp.Models.SaaS
{
    public sealed class BillingPlanesViewModel
    {
        public IReadOnlyCollection<MarketingPlanCardViewModel> PlanCards { get; init; } = Array.Empty<MarketingPlanCardViewModel>();
        public TenantCommercialAccessResult? CurrentAccess { get; init; }
        public bool IsAuthenticated { get; init; }
        public Guid? SelectedPlanId { get; init; }
    }
}
