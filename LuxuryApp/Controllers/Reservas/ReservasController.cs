using System.Security.Claims;
using LuxuryApp.Models.Reservas;
using LuxuryApp.Services.Reservas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Reservas
{
    /// <summary>
    /// Panel privado de "Solicitudes de reserva" y su configuración. Solo el dueño del negocio
    /// (Administrador). Todas las operaciones son tenant-scoped por el global query filter.
    /// </summary>
    [Authorize(Roles = "Administrador")]
    public sealed class ReservasController : Controller
    {
        private readonly IBookingRequestService _bookingRequestService;
        private readonly IBookingSettingsService _bookingSettingsService;
        private readonly IBookingCatalogService _bookingCatalogService;

        public ReservasController(
            IBookingRequestService bookingRequestService,
            IBookingSettingsService bookingSettingsService,
            IBookingCatalogService bookingCatalogService)
        {
            _bookingRequestService = bookingRequestService;
            _bookingSettingsService = bookingSettingsService;
            _bookingCatalogService = bookingCatalogService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? estado, string? rango, CancellationToken cancellationToken)
        {
            var model = await _bookingRequestService.BuildPageAsync(estado, rango, cancellationToken);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Lista(string? estado, string? rango, CancellationToken cancellationToken)
        {
            var model = await _bookingRequestService.BuildPageAsync(estado, rango, cancellationToken);
            return PartialView("_SolicitudesList", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirmar(int id, int? funcionarioId, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new { success = false, message = "Solicitud inválida." });
            }

            var result = await _bookingRequestService.ConfirmAsync(id, funcionarioId, CurrentUserId(), cancellationToken);
            return result.Success
                ? Ok(new { success = true, message = result.Message, citaId = result.CitaId, whatsAppStatus = result.WhatsAppStatus })
                : BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rechazar(int id, string? motivo, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new { success = false, message = "Solicitud inválida." });
            }

            var result = await _bookingRequestService.RejectAsync(id, motivo, CurrentUserId(), cancellationToken);
            return result.Success
                ? Ok(new { success = true, message = result.Message })
                : BadRequest(new { success = false, message = result.Message });
        }

        [HttpGet]
        public async Task<IActionResult> Servicios(CancellationToken cancellationToken)
        {
            var model = await _bookingCatalogService.BuildManagementAsync(cancellationToken);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarServicios(
            [FromBody] BookingCatalogSaveInput input,
            CancellationToken cancellationToken)
        {
            if (input is null)
            {
                return BadRequest(new { success = false, message = "Datos inválidos." });
            }

            await _bookingCatalogService.SaveAsync(input, CurrentUserId(), cancellationToken);
            return Ok(new { success = true, message = "Servicios publicados actualizados." });
        }

        [HttpGet]
        public async Task<IActionResult> Configuracion(CancellationToken cancellationToken)
        {
            var model = await _bookingSettingsService.BuildSettingsViewModelAsync(cancellationToken);
            model.LinkPublico = BookingLinkBuilder.Build(Request, model.PublicBookingSlug);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Configuracion(BookingSettingsViewModel model, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                var refreshed = await _bookingSettingsService.BuildSettingsViewModelAsync(cancellationToken);
                model.NombreNegocio = refreshed.NombreNegocio;
                model.LinkPublico = BookingLinkBuilder.Build(Request, model.PublicBookingSlug);
                return View(model);
            }

            try
            {
                await _bookingSettingsService.SaveSettingsAsync(model, CurrentUserId(), cancellationToken);
                TempData["ReservasConfigOk"] = "Configuración de reservas guardada.";
                return RedirectToAction(nameof(Configuracion));
            }
            catch (BookingValidationException ex)
            {
                ModelState.AddModelError(ex.Field ?? string.Empty, ex.Message);
                var refreshed = await _bookingSettingsService.BuildSettingsViewModelAsync(cancellationToken);
                model.NombreNegocio = refreshed.NombreNegocio;
                model.LinkPublico = BookingLinkBuilder.Build(Request, model.PublicBookingSlug);
                return View(model);
            }
        }

        private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
