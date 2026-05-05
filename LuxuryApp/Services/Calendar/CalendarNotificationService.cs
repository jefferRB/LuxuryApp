using LuxuryApp.Services;
using Microsoft.Extensions.Configuration;

namespace LuxuryApp.Services.Calendar
{
    public sealed class CalendarNotificationService : ICalendarNotificationService
    {
        private readonly WhatsAppService _whatsAppService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CalendarNotificationService> _logger;

        public CalendarNotificationService(
            WhatsAppService whatsAppService,
            IConfiguration configuration,
            ILogger<CalendarNotificationService> logger)
        {
            _whatsAppService = whatsAppService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> TrySendConfirmationAsync(
            string telefonoCliente,
            string nombreCliente,
            DateTime fechaHoraCita,
            string servicioNombre,
            string funcionarioNombre,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(telefonoCliente))
            {
                return false;
            }

            var template = _configuration["TwilioTemplates:Confirmacion"];
            if (string.IsNullOrWhiteSpace(template))
            {
                _logger.LogWarning("No se encontro el template de confirmacion de WhatsApp para agenda.");
                return false;
            }

            try
            {
                await _whatsAppService.SendTemplateAsync(
                    NormalizeTelefono(telefonoCliente),
                    template,
                    new Dictionary<string, object>
                    {
                        { "1", nombreCliente },
                        { "2", fechaHoraCita.ToString("dd/MM/yyyy") },
                        { "3", fechaHoraCita.ToString("hh:mm tt") },
                        { "4", servicioNombre },
                        { "5", funcionarioNombre }
                    });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Fallo el envio de confirmacion de WhatsApp para la cita de {NombreCliente} el {FechaHoraCita:yyyy-MM-dd HH:mm}.",
                    nombreCliente,
                    fechaHoraCita);

                return false;
            }
        }

        private static string NormalizeTelefono(string telefono) =>
            telefono
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("(", string.Empty, StringComparison.Ordinal)
                .Replace(")", string.Empty, StringComparison.Ordinal);
    }
}
