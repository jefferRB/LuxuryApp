using LuxuryApp.Services.WhatsApp;

namespace LuxuryApp.Tests.Support
{
    internal sealed class FakeTenantWhatsAppFeatureService : ITenantWhatsAppFeatureService
    {
        public bool IsEnabled { get; set; }

        public Task<bool> IsWhatsAppEnabledForCurrentTenantAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled);

        public Task<bool> HasWhatsAppAddonAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled);
    }
}
