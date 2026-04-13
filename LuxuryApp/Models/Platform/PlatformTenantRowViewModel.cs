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
        public bool CanAccessApp { get; init; }
        public string? EffectivePlanName { get; init; }
        public string Reason { get; init; } = string.Empty;
    }
}
