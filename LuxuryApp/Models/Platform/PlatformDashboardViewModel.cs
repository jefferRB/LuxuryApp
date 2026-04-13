using LuxuryApp.Models.SaaS;

namespace LuxuryApp.Models.Platform
{
    public sealed class PlatformDashboardViewModel
    {
        public int TotalTenants { get; init; }
        public int TotalUsers { get; init; }
        public int TotalActiveSubscriptions { get; init; }
        public int TotalPromotionalCodes { get; init; }
        public IReadOnlyCollection<Plan> AvailablePlans { get; init; } = Array.Empty<Plan>();
        public IReadOnlyCollection<PlatformTenantRowViewModel> Tenants { get; init; } = Array.Empty<PlatformTenantRowViewModel>();
        public IReadOnlyCollection<PlatformRecentUserViewModel> RecentUsers { get; init; } = Array.Empty<PlatformRecentUserViewModel>();
        public IReadOnlyCollection<PlatformRecentPaymentViewModel> RecentPayments { get; init; } = Array.Empty<PlatformRecentPaymentViewModel>();
    }
}
