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
        private readonly IBillingReconciliationService _reconciliationService;

        public PlatformBillingHealthController(
            IBillingHealthService healthService,
            IBillingReconciliationService reconciliationService)
        {
            _healthService = healthService;
            _reconciliationService = reconciliationService;
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

        /// <summary>
        /// Fuerza un reintento seguro de la cancelación del suscriptor viejo de un cambio de plan.
        /// Idempotente y con la MISMA lógica del worker: ignora el backoff (esa es la razón de ser
        /// del botón) pero nunca las guardas de elegibilidad, así que no puede cancelar un
        /// suscriptor que esté cobrando ni actuar sobre datos incompletos.
        /// Existe para que soporte destrabe un caso sin editar la BD ni entrar a TiloPay a mano.
        /// </summary>
        [HttpPost("retry-old-cancellation/{intentId:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryOldProviderCancellation(Guid intentId, CancellationToken cancellationToken)
        {
            var outcome = await _reconciliationService.ForceOldSubscriberCancellationRetryAsync(
                intentId,
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "platform",
                User.Identity?.Name ?? "platform",
                cancellationToken);

            TempData[outcome.Status == PlanChangeCancellationRetryStatus.Cancelled ? "Success" : "Error"] =
                outcome.Status switch
                {
                    PlanChangeCancellationRetryStatus.Cancelled =>
                        "Suscriptor viejo cancelado y verificado en TiloPay.",
                    PlanChangeCancellationRetryStatus.AttemptedStillPending =>
                        $"Se intentó cancelar pero el suscriptor viejo sigue pendiente. {outcome.Message}",
                    PlanChangeCancellationRetryStatus.NotPending =>
                        "Ese cambio de plan no tiene cancelación pendiente.",
                    _ => outcome.Message
                };

            return RedirectToAction(nameof(Index));
        }
    }
}
