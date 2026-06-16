namespace LuxuryApp.Models.Calendar
{
    public class CalendarAppointmentDetailsResponse
    {
        public int Id { get; init; }

        public string Tipo { get; init; } = string.Empty;

        public string? NombreCliente { get; init; }

        public string? TelefonoCliente { get; init; }

        public int? ClienteId { get; init; }

        public int? ServicioId { get; init; }

        public string? ServicioNombre { get; init; }

        public bool EsServicioPersonalizado { get; init; }

        public DateTime FechaHoraCita { get; init; }

        public int FuncionarioId { get; init; }

        public string FuncionarioNombre { get; init; } = string.Empty;

        public int DuracionMinutos { get; init; }

        public bool WhatsAppConsentAtCreation { get; init; }

        public string? WhatsAppConsentSource { get; init; }

        public DateTime? WhatsAppConsentCapturedAtUtc { get; init; }

        public bool? ClienteAceptaMensajesWhatsApp { get; init; }

        public string WhatsAppConsentDisplay { get; init; } = string.Empty;

        public string EstadoConfirmacionWhatsApp { get; init; } = string.Empty;

        public DateTime? ConfirmacionWhatsAppEnviadaUtc { get; init; }

        public DateTime? RecordatorioWhatsAppTresHorasEnviadoUtc { get; init; }

        public string WhatsAppStatusDisplay { get; init; } = string.Empty;
    }
}
