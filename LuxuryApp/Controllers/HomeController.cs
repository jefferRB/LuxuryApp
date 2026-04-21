using System.Diagnostics;
using LuxuryApp.Models;
using LuxuryApp.Models.Marketing;
using LuxuryApp.Services.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IPublicSiteContentService _publicSiteContentService;

        public HomeController(
            ILogger<HomeController> logger,
            IPublicSiteContentService publicSiteContentService)
        {
            _logger = logger;
            _publicSiteContentService = publicSiteContentService;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var plans = await _publicSiteContentService.GetPlanCardsAsync(cancellationToken);
            var model = new PublicHomeViewModel
            {
                HeroMetrics = _publicSiteContentService.GetHeroMetrics(),
                Modules = _publicSiteContentService.GetModules(),
                Plans = plans.Take(3).ToArray()
            };

            return View(model);
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }
        

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
