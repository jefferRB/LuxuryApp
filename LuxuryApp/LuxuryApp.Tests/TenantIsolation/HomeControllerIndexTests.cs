using LuxuryApp.Controllers;
using LuxuryApp.Models.Marketing;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class HomeControllerIndexTests
    {
        private const int ClientClosedRequest = 499;

        [Fact]
        public async Task Index_WhenTokenAlreadyCancelled_ReturnsClientClosedRequestWithoutLoadingPlans()
        {
            var logger = new CapturingLogger<HomeController>();
            var content = new ConfigurablePublicSiteContentService(
                _ => Task.FromResult<IReadOnlyCollection<MarketingPlanCardViewModel>>(
                    Array.Empty<MarketingPlanCardViewModel>()));
            var controller = CreateController(logger, content);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var result = await controller.Index(cts.Token);

            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(ClientClosedRequest, statusResult.StatusCode);
            Assert.Equal(0, content.GetPlanCardsCallCount);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Index_WhenClientCancelsDuringPlanLoad_ReturnsClientClosedRequestAndDoesNotLogError()
        {
            var logger = new CapturingLogger<HomeController>();
            using var cts = new CancellationTokenSource();

            // El cliente cancela justo mientras se cargan los planes.
            var content = new ConfigurablePublicSiteContentService(token =>
            {
                cts.Cancel();
                throw new OperationCanceledException(token);
            });
            var controller = CreateController(logger, content);

            var result = await controller.Index(cts.Token);

            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(ClientClosedRequest, statusResult.StatusCode);
            Assert.Equal(1, content.GetPlanCardsCallCount);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Debug);
        }

        [Fact]
        public async Task Index_WhenPlanLoadCancelledUnrelatedToRequest_LogsWarningAndRendersLandingWithoutPlans()
        {
            var logger = new CapturingLogger<HomeController>();

            // Cancelación/timeout NO originado por el request (el token del request sigue vivo).
            var content = new ConfigurablePublicSiteContentService(
                _ => throw new TaskCanceledException("Tiempo de espera de base de datos agotado."));
            var controller = CreateController(logger, content);

            var result = await controller.Index(CancellationToken.None);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<PublicHomeViewModel>(view.Model);
            Assert.Empty(model.Plans);
            Assert.NotEmpty(model.HeroMetrics);
            Assert.NotEmpty(model.Modules);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Index_WhenPlanLoadThrowsGeneralException_LogsWarningAndRendersLandingWithoutPlans()
        {
            var logger = new CapturingLogger<HomeController>();
            var content = new ConfigurablePublicSiteContentService(
                _ => throw new InvalidOperationException("Fallo inesperado al cargar planes."));
            var controller = CreateController(logger, content);

            var result = await controller.Index(CancellationToken.None);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<PublicHomeViewModel>(view.Model);
            Assert.Empty(model.Plans);
            Assert.NotEmpty(model.HeroMetrics);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Index_WhenPlansLoadSucceeds_RendersLandingWithAtMostThreePlans()
        {
            var logger = new CapturingLogger<HomeController>();
            var plans = Enumerable.Range(0, 5)
                .Select(index => new MarketingPlanCardViewModel
                {
                    Name = $"Plan {index}",
                    MonthlyPrice = 8000m + index
                })
                .ToArray();
            var content = new ConfigurablePublicSiteContentService(
                _ => Task.FromResult<IReadOnlyCollection<MarketingPlanCardViewModel>>(plans));
            var controller = CreateController(logger, content);

            var result = await controller.Index(CancellationToken.None);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<PublicHomeViewModel>(view.Model);
            Assert.Equal(3, model.Plans.Count);
            Assert.NotEmpty(model.HeroMetrics);
            Assert.NotEmpty(model.Modules);
            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        }

        private static HomeController CreateController(
            ILogger<HomeController> logger,
            IPublicSiteContentService content)
        {
            var controller = new HomeController(logger, content);
            ControllerTestSupport.AttachHttpContext(controller);
            return controller;
        }

        private sealed class ConfigurablePublicSiteContentService : IPublicSiteContentService
        {
            private readonly Func<CancellationToken, Task<IReadOnlyCollection<MarketingPlanCardViewModel>>> _planCards;

            public ConfigurablePublicSiteContentService(
                Func<CancellationToken, Task<IReadOnlyCollection<MarketingPlanCardViewModel>>> planCards) =>
                _planCards = planCards;

            public int GetPlanCardsCallCount { get; private set; }

            public IReadOnlyCollection<MarketingMetricViewModel> GetHeroMetrics() =>
                new[] { new MarketingMetricViewModel { Value = "1", Label = "panel" } };

            public IReadOnlyCollection<MarketingModuleViewModel> GetModules() =>
                new[] { new MarketingModuleViewModel { Id = "agenda", Title = "Agenda" } };

            public Task<IReadOnlyCollection<MarketingPlanCardViewModel>> GetPlanCardsAsync(
                CancellationToken cancellationToken = default)
            {
                GetPlanCardsCallCount++;
                return _planCards(cancellationToken);
            }

            public Task<IReadOnlyCollection<MarketingPlanCardViewModel>> GetWhatsAppAddonCardsAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyCollection<MarketingPlanCardViewModel>>(
                    Array.Empty<MarketingPlanCardViewModel>());

            public Task<IReadOnlyCollection<MarketingPlanCardViewModel>> GetInternalPlanCardsAsync(
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyCollection<MarketingPlanCardViewModel>>(
                    Array.Empty<MarketingPlanCardViewModel>());

            public Task<Plan?> FindAvailablePlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
                Task.FromResult<Plan?>(null);

            public Task<string?> GetPlanNameAsync(Guid? planId, CancellationToken cancellationToken = default) =>
                Task.FromResult<string?>(null);
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
