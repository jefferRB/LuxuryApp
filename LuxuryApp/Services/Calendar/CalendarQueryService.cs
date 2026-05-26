using LuxuryApp.Models.Calendar;
using LuxuryApp.Services.BusinessTime;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Calendar
{
    public sealed class CalendarQueryService : ICalendarQueryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public CalendarQueryService(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        public async Task<IReadOnlyList<CalendarAppointmentResponse>> GetAppointmentsByDayAsync(
            DateTime date,
            CancellationToken cancellationToken = default)
        {
            var (startDay, endDay) = ResolveDayRange(date);

            return await _context.Citas
                .AsNoTracking()
                .Where(c => c.FechaHoraCita >= startDay && c.FechaHoraCita < endDay)
                .OrderBy(c => c.FuncionarioId)
                .ThenBy(c => c.FechaHoraCita)
                .Select(c => new CalendarAppointmentResponse
                {
                    Id = c.Id,
                    Tipo = c.Tipo,
                    NombreCliente = c.NombreCliente,
                    TelefonoCliente = c.TelefonoCliente,
                    FechaHoraCita = c.FechaHoraCita,
                    DuracionMinutos = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes)
                        : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? CalendarCommandService.DefaultDurationMinutes),
                    FuncionarioId = c.FuncionarioId,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    ColorCalendario = c.Funcionario != null ? c.Funcionario.ColorCalendario : string.Empty,
                    ServicioId = c.ServicioId,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : null
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CalendarMonthCountResponse>> GetCitasCountByMonthAsync(
            int year,
            int month,
            CancellationToken cancellationToken = default)
        {
            var startMonth = new DateTime(year, month, 1);
            var endMonth = startMonth.AddMonths(1);

            return await _context.Citas
                .AsNoTracking()
                .Where(c => c.Tipo == "CITA" && c.FechaHoraCita >= startMonth && c.FechaHoraCita < endMonth)
                .GroupBy(c => c.FechaHoraCita.Day)
                .OrderBy(group => group.Key)
                .Select(group => new CalendarMonthCountResponse
                {
                    Day = group.Key,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CalendarUpcomingAppointmentResponse>> GetUpcomingAppointmentsAsync(
            DateTime date,
            int? funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var (startDay, endDay) = ResolveDayRange(date);
            var now = _businessDateTimeProvider.Now();

            var query = _context.Citas
                .AsNoTracking()
                .Where(c =>
                    c.Tipo == "CITA" &&
                    c.FechaHoraCita >= startDay &&
                    c.FechaHoraCita < endDay &&
                    c.FechaHoraCita >= now);

            if (funcionarioId.HasValue && funcionarioId.Value > 0)
            {
                query = query.Where(c => c.FuncionarioId == funcionarioId.Value);
            }

            return await query
                .OrderBy(c => c.FechaHoraCita)
                .Select(c => new CalendarUpcomingAppointmentResponse
                {
                    Id = c.Id,
                    NombreCliente = c.NombreCliente,
                    TelefonoCliente = c.TelefonoCliente,
                    FechaHoraCita = c.FechaHoraCita,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : string.Empty,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CalendarServiceOptionResponse>> GetServiciosActivosAsync(CancellationToken cancellationToken = default)
        {
            return await _context.Servicios
                .AsNoTracking()
                .Where(s => s.Activo)
                .OrderBy(s => s.Nombre)
                .Select(s => new CalendarServiceOptionResponse
                {
                    Id = s.Id,
                    Nombre = s.Nombre,
                    DuracionMinutos = s.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<CalendarOccupiedDateResponse>> GetFechasOcupadasAsync(
            int funcionarioId,
            DateTime? startDate,
            DateTime? endDate,
            CancellationToken cancellationToken = default)
        {
            var (startRange, endRange) = ResolveOccupiedRange(startDate, endDate);

            var citas = await _context.Citas
                .AsNoTracking()
                .Where(c =>
                    c.FuncionarioId == funcionarioId &&
                    c.FechaHoraCita >= startRange &&
                    c.FechaHoraCita < endRange)
                .OrderBy(c => c.FechaHoraCita)
                .Select(c => new OccupiedDateProjection
                {
                    FechaHoraCita = c.FechaHoraCita,
                    Duracion = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes)
                        : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? CalendarCommandService.DefaultDurationMinutes),
                    FuncionarioId = c.FuncionarioId
                })
                .ToListAsync(cancellationToken);

            return citas
                .Select(c => new CalendarOccupiedDateResponse
                {
                    Fecha = c.FechaHoraCita.ToString("yyyy-MM-dd"),
                    Hora = c.FechaHoraCita.ToString("HH:mm"),
                    Duracion = c.Duracion,
                    FuncionarioId = c.FuncionarioId
                })
                .ToList();
        }

        public Task<CalendarAppointmentDetailsResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            _context.Citas
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CalendarAppointmentDetailsResponse
                {
                    Id = c.Id,
                    Tipo = c.Tipo,
                    NombreCliente = c.NombreCliente,
                    TelefonoCliente = c.TelefonoCliente,
                    ServicioId = c.ServicioId,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : null,
                    FechaHoraCita = c.FechaHoraCita,
                    FuncionarioId = c.FuncionarioId,
                    DuracionMinutos = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes)
                        : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? CalendarCommandService.DefaultDurationMinutes)
                })
                .SingleOrDefaultAsync(cancellationToken);

        private static (DateTime StartDay, DateTime EndDay) ResolveDayRange(DateTime date)
        {
            var startDay = date.Date;
            return (startDay, startDay.AddDays(1));
        }

        private (DateTime StartRange, DateTime EndRange) ResolveOccupiedRange(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate?.Date ?? _businessDateTimeProvider.Today().AddMonths(-1);
            var endExclusive = endDate?.Date.AddDays(1) ?? start.AddMonths(3);

            if (endExclusive <= start)
            {
                endExclusive = start.AddDays(1);
            }

            return (start, endExclusive);
        }

        private sealed class OccupiedDateProjection
        {
            public DateTime FechaHoraCita { get; init; }

            public int Duracion { get; init; }

            public int FuncionarioId { get; init; }
        }
    }
}
