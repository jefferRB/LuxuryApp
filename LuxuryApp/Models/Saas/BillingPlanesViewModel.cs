namespace LuxuryApp.Models.SaaS
{
    public sealed class BillingPlanesViewModel
    {
        public IReadOnlyCollection<Plan> Plans { get; init; } = Array.Empty<Plan>();
        public TenantCommercialAccessResult? CurrentAccess { get; init; }
    }
}
