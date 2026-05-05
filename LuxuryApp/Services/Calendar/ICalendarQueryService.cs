using LuxuryApp.Models.Calendar;

namespace LuxuryApp.Services.Calendar
{
    public interface ICalendarQueryService
    {
        Task<IReadOnlyList<CalendarAppointmentResponse>> GetAppointmentsByDayAsync(DateTime date, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CalendarMonthCountResponse>> GetCitasCountByMonthAsync(int year, int month, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CalendarUpcomingAppointmentResponse>> GetUpcomingAppointmentsAsync(DateTime date, int? funcionarioId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CalendarServiceOptionResponse>> GetServiciosActivosAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<CalendarOccupiedDateResponse>> GetFechasOcupadasAsync(
            int funcionarioId,
            DateTime? startDate,
            DateTime? endDate,
            CancellationToken cancellationToken = default);

        Task<CalendarAppointmentDetailsResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    }
}
