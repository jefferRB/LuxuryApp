using LuxuryApp.Models.Marketing;
using LuxuryApp.Models.SaaS;

namespace LuxuryApp.Services.PublicSite
{
    public interface IPublicSiteContentService
    {
        IReadOnlyCollection<MarketingMetricViewModel> GetHeroMetrics();
        IReadOnlyCollection<MarketingModuleViewModel> GetModules();
        Task<IReadOnlyCollection<MarketingPlanCardViewModel>> GetPlanCardsAsync(
            CancellationToken cancellationToken = default);
        Task<Plan?> FindAvailablePlanAsync(Guid planId, CancellationToken cancellationToken = default);
        Task<string?> GetPlanNameAsync(Guid? planId, CancellationToken cancellationToken = default);
    }
}
