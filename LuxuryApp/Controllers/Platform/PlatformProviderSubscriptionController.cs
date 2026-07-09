using System.Security.Claims;
using LuxuryApp.Models.Identity;
using LuxuryApp.Services.Billing;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Platform
{
    /// <summary>
    /// Operaciones de gestión del suscriptor de TiloPay (pausar/reactivar/cancelar) sobre el
    /// suscriptor recurrente de un tenant. SOLO plataforma (super admin). Todo queda auditado.
    /// </summary>
    [Authorize(Policy = PlatformAuthorizationPolicies.PlatformSuperAdmin)]
    [Route("Platform/ProviderSubscription")]
    public sealed class PlatformProviderSubscriptionController : Controller
    {
        private readonly IProviderSubscriptionManager _manager;
        private readonly UserManager<AppUsuario> _userManager;

        public PlatformProviderSubscriptionController(
            IProviderSubscriptionManager manager,
            UserManager<AppUsuario> userManager)
        {
            _manager = manager;
            _userManager = userManager;
        }

        [HttpPost("pause")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Pause(Guid tenantId, CancellationToken cancellationToken) =>
            RunAsync(tenantId, (m, actor, email) => m.PauseAsync(tenantId, actor, email, cancellationToken));

        [HttpPost("reactivate")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reactivate(Guid tenantId, CancellationToken cancellationToken) =>
            RunAsync(tenantId, (m, actor, email) => m.ReactivateAsync(tenantId, actor, email, cancellationToken));

        [HttpPost("cancel")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Cancel(Guid tenantId, CancellationToken cancellationToken) =>
            RunAsync(tenantId, (m, actor, email) => m.CancelAsync(tenantId, actor, email, cancellationToken));

        private async Task<IActionResult> RunAsync(
            Guid tenantId,
            Func<IProviderSubscriptionManager, string, string, Task<ProviderSubscriptionActionResult>> operation)
        {
            if (tenantId == Guid.Empty)
            {
                return BadRequest("tenantId requerido.");
            }

            var actor = await _userManager.GetUserAsync(User);
            var actorId = actor?.Id ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            var actorEmail = actor?.Email ?? User.Identity?.Name ?? "system";

            var result = await operation(_manager, actorId, actorEmail);

            TempData[result.Succeeded ? "PlatformSuccess" : "PlatformError"] = result.Message;
            return RedirectToAction("Index", "PlatformBillingHealth");
        }
    }
}
