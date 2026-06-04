namespace LuxuryApp.Models.Calendar
{
    public class CalendarUpsertRequest
    {
        public string? NombreCliente { get; init; }

        public string? TelefonoCliente { get; init; }

        public int? ClienteId { get; init; }

        public int? ServicioId { get; init; }

        public DateTime FechaHoraCita { get; init; }

        public int FuncionarioId { get; init; }

        public string Tipo { get; init; } = "CITA";

        public int? DuracionMinutos { get; init; }

        public bool WhatsAppConsentAtCreation { get; init; }

        public string? WhatsAppConsentSource { get; init; }

        public DateTime? WhatsAppConsentCapturedAtUtc { get; init; }

        public bool Duplicar { get; init; }

        public IReadOnlyList<string> FechasDuplicadas { get; init; } = Array.Empty<string>();
    }
}
