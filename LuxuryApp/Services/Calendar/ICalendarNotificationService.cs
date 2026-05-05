namespace LuxuryApp.Services.Calendar
{
    public interface ICalendarNotificationService
    {
        Task<bool> TrySendConfirmationAsync(
            string telefonoCliente,
            string nombreCliente,
            DateTime fechaHoraCita,
            string servicioNombre,
            string funcionarioNombre,
            CancellationToken cancellationToken = default);
    }
}
