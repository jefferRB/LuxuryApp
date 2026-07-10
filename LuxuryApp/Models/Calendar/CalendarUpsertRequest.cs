namespace LuxuryApp.Models.Calendar
{
    public class CalendarUpsertRequest
    {
        public string? NombreCliente { get; init; }

        public string? TelefonoCliente { get; init; }

        public int? ClienteId { get; init; }

        public int? ServicioId { get; init; }

        public bool EsServicioPersonalizado { get; init; }

        public string? ServicioNombrePersonalizado { get; init; }

        public DateTime FechaHoraCita { get; init; }

        public int FuncionarioId { get; init; }

        public string Tipo { get; init; } = "CITA";

        public int? DuracionMinutos { get; init; }

        public bool WhatsAppConsentAtCreation { get; init; }

        public string? WhatsAppConsentSource { get; init; }

        public DateTime? WhatsAppConsentCapturedAtUtc { get; init; }

        // Autorización de WhatsApp otorgada desde el formulario de la cita para un cliente
        // existente. Se aplica al Cliente dentro de la misma transacción que guarda la cita.
        public bool AutorizarWhatsAppAlGuardar { get; init; }

        // Usuario autenticado que capturó la autorización (auditoría). Se resuelve en el
        // servidor desde los claims; nunca proviene del cuerpo enviado por el navegador.
        public string? WhatsAppConsentCapturedByUserId { get; init; }

        public bool Duplicar { get; init; }

        public IReadOnlyList<string> FechasDuplicadas { get; init; } = Array.Empty<string>();
    }
}
