using LuxuryApp.Models.Reservas;
using LuxuryApp.Services.Reservas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LuxuryApp.Controllers.Reservas
{
    /// <summary>
    /// Ruta PÚBLICA de reservas: /reservar/{slug}. Sin login. Resuelve el tenant por slug,
    /// fija el contexto de tenant para el request y solo expone datos públicos y seguros.
    /// No crea citas: registra una solicitud Pending que el negocio confirma desde la plataforma.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("PublicBooking")]
    [Route("reservar")]
    public sealed class PublicReservasController : Controller
    {
        private readonly IPublicBookingService _publicBookingService;

        public PublicReservasController(IPublicBookingService publicBookingService)
        {
            _publicBookingService = publicBookingService;
        }

        [HttpGet("{slug}")]
        public async Task<IActionResult> Index(string slug, CancellationToken cancellationToken)
        {
            var context = await _publicBookingService.ResolveContextAsync(slug, cancellationToken);
            if (context is null)
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                return View("NoDisponible");
            }

            Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            var page = await _publicBookingService.BuildPageAsync(context, cancellationToken);
            return View(page);
        }

        [HttpGet("{slug}/disponibilidad")]
        public async Task<IActionResult> Disponibilidad(
            string slug,
            int servicioId,
            string? fecha,
            int? funcionarioId,
            CancellationToken cancellationToken)
        {
            var context = await _publicBookingService.ResolveContextAsync(slug, cancellationToken);
            if (context is null)
            {
                return NotFound();
            }

            var result = await _publicBookingService.GetAvailabilityAsync(
                context,
                servicioId,
                fecha,
                funcionarioId,
                cancellationToken);

            return Json(result);
        }

        [HttpPost("{slug}/solicitar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Solicitar(
            string slug,
            [FromForm] PublicBookingRequestInput input,
            CancellationToken cancellationToken)
        {
            var context = await _publicBookingService.ResolveContextAsync(slug, cancellationToken);
            if (context is null)
            {
                return NotFound();
            }

            var result = await _publicBookingService.SubmitAsync(context, input, cancellationToken);
            return Json(new { success = result.Success, message = result.Message });
        }
    }
}
