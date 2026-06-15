using System.Text.Json;

namespace LuxuryApp.Services.Calendar
{
    public interface ICalendarWhatsAppNotificationService
    {
        Task SendAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default);

        Task SendAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default);

        Task QueueAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default);

        Task QueueAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Al crear una cita: encola el recordatorio de inmediato si la cita ya está dentro de la
        /// ventana del recordatorio y el tenant lo tiene activo (modo relativo + envío inmediato).
        /// </summary>
        Task QueueImmediateReminderOnCreateAsync(int citaId, CancellationToken cancellationToken = default);

        Task ProcessInboundReplyAsync(JsonElement payload, CancellationToken cancellationToken = default);

        Task ProcessStatusUpdateAsync(JsonElement payload, CancellationToken cancellationToken = default);

        Task ScheduleDueRemindersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Genera los lotes diarios de confirmaciones/recordatorios para tenants en modo
        /// "a hora fija" cuando ya pasó la hora configurada y no se ha corrido hoy. Idempotente.
        /// </summary>
        Task GenerateDailyBatchAsync(CancellationToken cancellationToken = default);

        Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default);

        Task RescheduleConfirmationIfPendingAsync(int citaId, DateTime newFechaHoraCita, CancellationToken cancellationToken = default);

        Task CancelPendingNotificationsAsync(int citaId, CancellationToken cancellationToken = default);
    }
}
