namespace LuxuryApp.Models.SaaS
{
    public sealed class TenantCommercialAccessResult
    {
        public bool CanAccessApp { get; init; }
        public bool RequiresBilling { get; init; }
        public bool IsPlatformSuperAdmin { get; init; }
        public Guid TenantId { get; init; }
        public Guid? EffectivePlanId { get; init; }
        public string? EffectivePlanName { get; init; }
        public TenantCommercialAccessMode CommercialAccessMode { get; init; }
        public TenantCommercialAccessSource AccessSource { get; init; }
        public string Reason { get; init; } = string.Empty;
        public DateTime? AccessEndsUtc { get; init; }
        public bool HasCommercialHistory { get; init; }
    }
}
