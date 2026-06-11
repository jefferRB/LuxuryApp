using LuxuryApp.Models.WhatsApp;

namespace LuxuryApp.Services.WhatsApp
{
    public interface IWhatsAppInboxService
    {
        Task<WhatsAppInboxResponse> GetInboxAsync(
            DateTime date,
            int? funcionarioId,
            bool whatsAppEnabled,
            CancellationToken cancellationToken = default);

        /// <summary>Historial de mensajes/logs de una cita (para "Ver chat"). Devuelve null si la cita no pertenece al tenant.</summary>
        Task<IReadOnlyList<WhatsAppChatLogItem>?> GetCitaChatAsync(int citaId, CancellationToken cancellationToken = default);
    }
}
