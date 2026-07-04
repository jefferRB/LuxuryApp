using LuxuryApp.Services.Billing;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Platform
{
    /// <summary>
    /// Diagnóstico interno del módulo Billing. SOLO plataforma (super admin): nunca debe
    /// exponerse a tenants porque agrega datos cross-tenant.
    /// </summary>
    [Authorize(Policy = PlatformAuthorizationPolicies.PlatformSuperAdmin)]
    [Route("Platform/BillingHealth")]
    public sealed class PlatformBillingHealthController : Controller
    {
        private readonly IBillingHealthService _healthService;

        public PlatformBillingHealthController(IBillingHealthService healthService)
        {
            _healthService = healthService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var snapshot = await _healthService.BuildAsync(cancellationToken);
            return View(snapshot);
        }

        /// <summary>Misma fotografía en JSON para monitoreo externo autenticado.</summary>
        [HttpGet("json")]
        public async Task<IActionResult> Snapshot(CancellationToken cancellationToken)
        {
            var snapshot = await _healthService.BuildAsync(cancellationToken);
            return Json(snapshot);
        }
    }
}
