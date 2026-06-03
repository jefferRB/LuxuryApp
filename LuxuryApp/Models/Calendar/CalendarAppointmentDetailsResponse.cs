namespace LuxuryApp.Models.Calendar
{
    public class CalendarAppointmentDetailsResponse
    {
        public int Id { get; init; }

        public string Tipo { get; init; } = string.Empty;

        public string? NombreCliente { get; init; }

        public string? TelefonoCliente { get; init; }

        public int? ServicioId { get; init; }

        public string? ServicioNombre { get; init; }

        public DateTime FechaHoraCita { get; init; }

        public int FuncionarioId { get; init; }

        public int DuracionMinutos { get; init; }

        public string EstadoConfirmacionWhatsApp { get; init; } = string.Empty;

        public DateTime? ConfirmacionWhatsAppEnviadaUtc { get; init; }

        public DateTime? RecordatorioWhatsAppTresHorasEnviadoUtc { get; init; }
    }
}
