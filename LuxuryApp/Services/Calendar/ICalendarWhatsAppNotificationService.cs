using System.Text.Json;

namespace LuxuryApp.Services.Calendar
{
    public interface ICalendarWhatsAppNotificationService
    {
        Task SendAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default);

        Task SendAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default);

        Task QueueAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default);

        Task QueueAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default);

        Task ProcessInboundReplyAsync(JsonElement payload, CancellationToken cancellationToken = default);

        Task ProcessStatusUpdateAsync(JsonElement payload, CancellationToken cancellationToken = default);

        Task ScheduleDueRemindersAsync(CancellationToken cancellationToken = default);

        Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default);
    }
}
