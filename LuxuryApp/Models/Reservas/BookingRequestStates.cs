namespace LuxuryApp.Models.Reservas
{
    /// <summary>
    /// Estados de una solicitud de reserva online. Se guardan como string legible en BD
    /// para que las consultas y el panel privado sean fáciles de interpretar.
    /// </summary>
    public static class BookingRequestStates
    {
        public const string Pending = "Pending";
        public const string Confirmed = "Confirmed";
        public const string Rejected = "Rejected";
        public const string Expired = "Expired";
        public const string CancelledByClient = "CancelledByClient";

        public static readonly IReadOnlyCollection<string> All =
        [
            Pending,
            Confirmed,
            Rejected,
            Expired,
            CancelledByClient
        ];

        public static bool IsKnown(string? estado) =>
            !string.IsNullOrWhiteSpace(estado) && All.Contains(estado);
    }

    /// <summary>
    /// Origen de la solicitud. En Fase 1 solo existe el link público, pero se deja
    /// preparado para futuros canales (WhatsApp, recepción, etc.).
    /// </summary>
    public static class BookingRequestOrigins
    {
        public const string PublicLink = "PublicLink";
    }

    /// <summary>
    /// Modos de operación de las reservas online. En Fase 1 solo aprobación manual:
    /// el cliente solicita y el negocio confirma o rechaza desde la plataforma privada.
    /// </summary>
    public static class PublicBookingModes
    {
        public const string ManualApproval = "ManualApproval";
    }
}
