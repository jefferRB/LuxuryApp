namespace LuxuryApp.Models.WhatsApp
{
    /// <summary>Respuesta de la bandeja WhatsApp para un día/tenant.</summary>
    public sealed class WhatsAppInboxResponse
    {
        public bool WhatsAppEnabled { get; init; }

        public WhatsAppInboxStats Stats { get; init; } = new();

        public IReadOnlyList<WhatsAppInboxItem> Items { get; init; } = new List<WhatsAppInboxItem>();
    }

    public sealed class WhatsAppInboxStats
    {
        public int Enviados { get; init; }

        public int Confirmados { get; init; }

        public int Pendientes { get; init; }

        public int Fallidos { get; init; }
    }

    /// <summary>
    /// Una cita del día con su estado de seguimiento WhatsApp.
    /// Sirve tanto a la bandeja (tarjetas) como a la agenda del día (tabla).
    /// </summary>
    public sealed class WhatsAppInboxItem
    {
        public int CitaId { get; init; }

        public string NombreCliente { get; init; } = string.Empty;

        public string Iniciales { get; init; } = string.Empty;

        public string? Telefono { get; init; }

        public bool TieneTelefono { get; init; }

        public string ServicioNombre { get; init; } = string.Empty;

        public string FuncionarioNombre { get; init; } = string.Empty;

        public DateTime FechaHoraCita { get; init; }

        public string HoraLocal { get; init; } = string.Empty;

        // Estado de la cita (Confirmada / Pendiente / Cancelada) derivado de EstadoConfirmacionWhatsApp.
        public string EstadoCitaKey { get; init; } = string.Empty;

        public string EstadoCitaLabel { get; init; } = string.Empty;

        // Estado del mensaje WhatsApp (machine key + etiqueta visible + subtexto).
        public string WaStatusKey { get; init; } = string.Empty;

        public string WaStatusLabel { get; init; } = string.Empty;

        public string WaSubText { get; init; } = string.Empty;

        // Reglas de acción calculadas en servidor.
        public bool PuedeEnviar { get; init; }

        public bool PuedeReenviar { get; init; }
    }

    public sealed class WhatsAppChatLogItem
    {
        public DateTime FechaHoraUtc { get; init; }

        public string FechaHoraLocal { get; init; } = string.Empty;

        public string Direccion { get; init; } = string.Empty;

        public string Tipo { get; init; } = string.Empty;

        public string Estado { get; init; } = string.Empty;

        public string? Error { get; init; }

        // MetaMessageId enmascarado (nunca se exponen tokens ni payload sensible).
        public string? ReferenciaMensaje { get; init; }
    }
}
