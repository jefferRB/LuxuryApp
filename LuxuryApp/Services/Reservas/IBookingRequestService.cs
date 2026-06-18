using LuxuryApp.Models.Reservas;

namespace LuxuryApp.Services.Reservas
{
    public sealed class BookingActionResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int? CitaId { get; init; }

        public static BookingActionResult Ok(string message, int? citaId = null) =>
            new() { Success = true, Message = message, CitaId = citaId };

        public static BookingActionResult Fail(string message) =>
            new() { Success = false, Message = message };
    }

    public interface IBookingRequestService
    {
        Task<BookingRequestsPageViewModel> BuildPageAsync(
            string? estado,
            string? rango,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirma una solicitud: revalida disponibilidad, crea la cita real reutilizando el
        /// servicio del calendario (que dispara el flujo de WhatsApp existente si aplica) y marca
        /// la solicitud como Confirmed. funcionarioIdOverride permite asignar un funcionario cuando
        /// la solicitud era "cualquiera".
        /// </summary>
        Task<BookingActionResult> ConfirmAsync(
            int requestId,
            int? funcionarioIdOverride,
            string? userId,
            CancellationToken cancellationToken = default);

        Task<BookingActionResult> RejectAsync(
            int requestId,
            string? reason,
            string? userId,
            CancellationToken cancellationToken = default);
    }
}
