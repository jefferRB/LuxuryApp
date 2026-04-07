using Microsoft.AspNetCore.Mvc;
using LuxuryApp.Models.SaaS;

namespace LuxuryApp.Controllers
{
    public class BillingController : Controller
    {
        private readonly ILogger<BillingController> _logger;

        public BillingController(ILogger<BillingController> logger)
        {
            _logger = logger;
        }

        // 🚫 Sin suscripción
        public IActionResult SinSuscripcion()
        {
            return View();
        }

        // ⚠️ Plan vencido o moroso
        public IActionResult PlanVencido()
        {
            return View();
        }

        // 💰 Planes disponibles
        public IActionResult Planes()
        {
            return View();
        }

        // ✅ Stripe success
        public IActionResult Exito()
        {
            return View();
        }

        // ❌ Stripe cancel
        public IActionResult Cancelado()
        {
            return View();
        }
    }
}