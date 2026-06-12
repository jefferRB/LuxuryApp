using LuxuryApp.Models.Calendar;

namespace LuxuryApp.Services.Calendar
{
    public interface ICalendarCommandService
    {
        Task<CalendarAppointmentResponse> CreateAsync(CalendarUpsertRequest request, CancellationToken cancellationToken = default);

        Task<CalendarAppointmentResponse> UpdateAsync(int id, CalendarUpsertRequest request, CancellationToken cancellationToken = default);

        Task MoveAsync(int id, CalendarMoveRequest request, CancellationToken cancellationToken = default);

        Task ResizeDurationAsync(int id, int duracionMinutos, CancellationToken cancellationToken = default);

        Task DeleteAsync(int id, CancellationToken cancellationToken = default);

        Task ProcessVisitsAsync(CancellationToken cancellationToken = default);
    }
}
