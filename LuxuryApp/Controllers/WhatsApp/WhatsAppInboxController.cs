using System.Globalization;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.WhatsApp
{
    [Authorize(Roles = "Administrador")]
    [Route("WhatsAppInbox")]
    public class WhatsAppInboxController : Controller
    {
        private readonly IWhatsAppInboxService _inboxService;
        private readonly ITenantWhatsAppFeatureService _tenantWhatsAppFeatureService;
        private readonly ICalendarWhatsAppNotificationService _notificationService;
        private readonly ILogger<WhatsAppInboxController> _logger;

        public WhatsAppInboxController(
            IWhatsAppInboxService inboxService,
            ITenantWhatsAppFeatureService tenantWhatsAppFeatureService,
            ICalendarWhatsAppNotificationService notificationService,
            ILogger<WhatsAppInboxController> logger)
        {
            _inboxService = inboxService;
            _tenantWhatsAppFeatureService = tenantWhatsAppFeatureService;
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpGet("Inbox")]
        public async Task<IActionResult> Inbox(string date, int? funcionarioId, CancellationToken cancellationToken)
        {
            if (!TryParseLocalDate(date, out var parsedDate))
            {
                return BadRequest("La fecha solicitada no es valida.");
            }

            var whatsAppEnabled = await _tenantWhatsAppFeatureService
                .IsWhatsAppEnabledForCurrentTenantAsync(cancellationToken);

            var inbox = await _inboxService.GetInboxAsync(parsedDate, funcionarioId, whatsAppEnabled, cancellationToken);
            return Ok(inbox);
        }

        [HttpGet("Chat/{citaId:int}")]
        public async Task<IActionResult> Chat(int citaId, CancellationToken cancellationToken)
        {
            if (citaId <= 0)
            {
                return NotFound();
            }

            var logs = await _inboxService.GetCitaChatAsync(citaId, cancellationToken);
            return logs is null ? NotFound() : Ok(logs);
        }

        [HttpPost("Send/{citaId:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(int citaId, CancellationToken cancellationToken)
        {
            if (citaId <= 0)
            {
                return NotFound();
            }

            var whatsAppEnabled = await _tenantWhatsAppFeatureService
                .IsWhatsAppEnabledForCurrentTenantAsync(cancellationToken);

            if (!whatsAppEnabled)
            {
                return BadRequest("WhatsApp no está configurado para este negocio.");
            }

            try
            {
                // El servicio de notificación aplica idempotencia, consentimiento,
                // límites del tenant y filtrado por tenant (la cita debe pertenecer al tenant actual).
                await _notificationService.SendAppointmentConfirmationAsync(citaId, cancellationToken);
                return Ok(new { success = true });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando confirmacion WhatsApp manual. CitaId {CitaId}.", citaId);
                return BadRequest("No fue posible enviar el mensaje en este momento. Intente nuevamente.");
            }
        }

        private static bool TryParseLocalDate(string? value, out DateTime parsedDate) =>
            DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);
    }
}
