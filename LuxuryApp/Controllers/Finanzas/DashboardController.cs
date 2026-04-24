using LuxuryApp.Services.Finanzas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]
    public class DashboardController : Controller
    {
        private readonly IDashboardFinancieroQueryService _dashboardFinancieroQueryService;

        public DashboardController(IDashboardFinancieroQueryService dashboardFinancieroQueryService)
        {
            _dashboardFinancieroQueryService = dashboardFinancieroQueryService;
        }

        public async Task<IActionResult> Index(int? mes, int? anio)
        {
            var vm = await _dashboardFinancieroQueryService.BuildViewModelAsync(mes, anio);
            return View(vm);
        }
    }
}
