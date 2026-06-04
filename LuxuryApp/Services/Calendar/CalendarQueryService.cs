using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.WhatsApp;
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

            var appointments = await _context.Citas
                .AsNoTracking()
                .Where(c => c.FechaHoraCita >= startDay && c.FechaHoraCita < endDay)
                .OrderBy(c => c.FuncionarioId)
                .ThenBy(c => c.FechaHoraCita)
                .Select(c => new AppointmentProjection
                {
                    Id = c.Id,
                    Tipo = c.Tipo,
                    NombreCliente = c.NombreCliente,
                    TelefonoCliente = c.TelefonoCliente,
                    ClienteId = c.ClienteId,
                    FechaHoraCita = c.FechaHoraCita,
                    DuracionMinutos = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes)
                        : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? CalendarCommandService.DefaultDurationMinutes),
                    FuncionarioId = c.FuncionarioId,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    ColorCalendario = c.Funcionario != null ? c.Funcionario.ColorCalendario : string.Empty,
                    ServicioId = c.ServicioId,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : null,
                    EstadoConfirmacionWhatsApp = c.EstadoConfirmacionWhatsApp,
                    ConfirmacionWhatsAppEnviadaUtc = c.ConfirmacionWhatsAppEnviadaUtc,
                    RecordatorioWhatsAppTresHorasEnviadoUtc = c.RecordatorioWhatsAppTresHorasEnviadoUtc,
                    WhatsAppConsentAtCreation = c.WhatsAppConsentAtCreation,
                    WhatsAppConsentSource = c.WhatsAppConsentSource,
                    WhatsAppConsentCapturedAtUtc = c.WhatsAppConsentCapturedAtUtc,
                    ClienteAceptaMensajesWhatsApp = c.Cliente != null ? (bool?)c.Cliente.AceptaMensajesWhatsApp : null
                })
                .ToListAsync(cancellationToken);

            var latestLogs = await LoadLatestOutboundLogMapAsync(
                appointments.Select(appointment => appointment.Id),
                cancellationToken);

            return appointments
                .Select(appointment => new CalendarAppointmentResponse
                {
                    Id = appointment.Id,
                    Tipo = appointment.Tipo,
                    NombreCliente = appointment.NombreCliente,
                    TelefonoCliente = appointment.TelefonoCliente,
                    ClienteId = appointment.ClienteId,
                    FechaHoraCita = appointment.FechaHoraCita,
                    DuracionMinutos = appointment.DuracionMinutos,
                    FuncionarioId = appointment.FuncionarioId,
                    FuncionarioNombre = appointment.FuncionarioNombre,
                    ColorCalendario = appointment.ColorCalendario,
                    ServicioId = appointment.ServicioId,
                    ServicioNombre = appointment.ServicioNombre,
                    WhatsAppConsentAtCreation = appointment.WhatsAppConsentAtCreation,
                    WhatsAppConsentSource = appointment.WhatsAppConsentSource,
                    WhatsAppConsentCapturedAtUtc = appointment.WhatsAppConsentCapturedAtUtc,
                    EstadoConfirmacionWhatsApp = appointment.EstadoConfirmacionWhatsApp,
                    ConfirmacionWhatsAppEnviadaUtc = appointment.ConfirmacionWhatsAppEnviadaUtc,
                    RecordatorioWhatsAppTresHorasEnviadoUtc = appointment.RecordatorioWhatsAppTresHorasEnviadoUtc,
                    WhatsAppStatusDisplay = BuildWhatsAppStatusDisplay(
                        appointment,
                        latestLogs.GetValueOrDefault(appointment.Id))
                })
                .ToList();
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

            var appointments = await query
                .OrderBy(c => c.FechaHoraCita)
                .Select(c => new AppointmentProjection
                {
                    Id = c.Id,
                    Tipo = c.Tipo,
                    NombreCliente = c.NombreCliente,
                    TelefonoCliente = c.TelefonoCliente,
                    ClienteId = c.ClienteId,
                    FechaHoraCita = c.FechaHoraCita,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : string.Empty,
                    FuncionarioNombre = c.Funcionario != null ? c.Funcionario.Nombre : string.Empty,
                    EstadoConfirmacionWhatsApp = c.EstadoConfirmacionWhatsApp,
                    ConfirmacionWhatsAppEnviadaUtc = c.ConfirmacionWhatsAppEnviadaUtc,
                    RecordatorioWhatsAppTresHorasEnviadoUtc = c.RecordatorioWhatsAppTresHorasEnviadoUtc,
                    WhatsAppConsentAtCreation = c.WhatsAppConsentAtCreation,
                    WhatsAppConsentSource = c.WhatsAppConsentSource,
                    ClienteAceptaMensajesWhatsApp = c.Cliente != null ? (bool?)c.Cliente.AceptaMensajesWhatsApp : null
                })
                .ToListAsync(cancellationToken);

            var latestLogs = await LoadLatestOutboundLogMapAsync(
                appointments.Select(appointment => appointment.Id),
                cancellationToken);

            return appointments
                .Select(appointment => new CalendarUpcomingAppointmentResponse
                {
                    Id = appointment.Id,
                    NombreCliente = appointment.NombreCliente,
                    TelefonoCliente = appointment.TelefonoCliente,
                    FechaHoraCita = appointment.FechaHoraCita,
                    ServicioNombre = appointment.ServicioNombre ?? string.Empty,
                    FuncionarioNombre = appointment.FuncionarioNombre,
                    EstadoConfirmacionWhatsApp = appointment.EstadoConfirmacionWhatsApp,
                    ConfirmacionWhatsAppEnviadaUtc = appointment.ConfirmacionWhatsAppEnviadaUtc,
                    RecordatorioWhatsAppTresHorasEnviadoUtc = appointment.RecordatorioWhatsAppTresHorasEnviadoUtc,
                    WhatsAppStatusDisplay = BuildWhatsAppStatusDisplay(
                        appointment,
                        latestLogs.GetValueOrDefault(appointment.Id))
                })
                .ToList();
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

        public async Task<CalendarAppointmentDetailsResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var appointment = await _context.Citas
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new AppointmentProjection
                {
                    Id = c.Id,
                    Tipo = c.Tipo,
                    NombreCliente = c.NombreCliente,
                    TelefonoCliente = c.TelefonoCliente,
                    ClienteId = c.ClienteId,
                    ServicioId = c.ServicioId,
                    ServicioNombre = c.Servicio != null ? c.Servicio.Nombre : null,
                    FechaHoraCita = c.FechaHoraCita,
                    FuncionarioId = c.FuncionarioId,
                    DuracionMinutos = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes)
                        : ((c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? CalendarCommandService.DefaultDurationMinutes),
                    WhatsAppConsentAtCreation = c.WhatsAppConsentAtCreation,
                    WhatsAppConsentSource = c.WhatsAppConsentSource,
                    WhatsAppConsentCapturedAtUtc = c.WhatsAppConsentCapturedAtUtc,
                    ClienteAceptaMensajesWhatsApp = c.Cliente != null ? (bool?)c.Cliente.AceptaMensajesWhatsApp : null,
                    EstadoConfirmacionWhatsApp = c.EstadoConfirmacionWhatsApp,
                    ConfirmacionWhatsAppEnviadaUtc = c.ConfirmacionWhatsAppEnviadaUtc,
                    RecordatorioWhatsAppTresHorasEnviadoUtc = c.RecordatorioWhatsAppTresHorasEnviadoUtc
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (appointment is null)
            {
                return null;
            }

            var latestLogs = await LoadLatestOutboundLogMapAsync(new[] { appointment.Id }, cancellationToken);
            var latestLog = latestLogs.GetValueOrDefault(appointment.Id);

            return new CalendarAppointmentDetailsResponse
            {
                Id = appointment.Id,
                Tipo = appointment.Tipo,
                NombreCliente = appointment.NombreCliente,
                TelefonoCliente = appointment.TelefonoCliente,
                ClienteId = appointment.ClienteId,
                ServicioId = appointment.ServicioId,
                ServicioNombre = appointment.ServicioNombre,
                FechaHoraCita = appointment.FechaHoraCita,
                FuncionarioId = appointment.FuncionarioId,
                DuracionMinutos = appointment.DuracionMinutos,
                WhatsAppConsentAtCreation = appointment.WhatsAppConsentAtCreation,
                WhatsAppConsentSource = appointment.WhatsAppConsentSource,
                WhatsAppConsentCapturedAtUtc = appointment.WhatsAppConsentCapturedAtUtc,
                ClienteAceptaMensajesWhatsApp = appointment.ClienteAceptaMensajesWhatsApp,
                WhatsAppConsentDisplay = BuildWhatsAppConsentDisplay(appointment),
                EstadoConfirmacionWhatsApp = appointment.EstadoConfirmacionWhatsApp,
                ConfirmacionWhatsAppEnviadaUtc = appointment.ConfirmacionWhatsAppEnviadaUtc,
                RecordatorioWhatsAppTresHorasEnviadoUtc = appointment.RecordatorioWhatsAppTresHorasEnviadoUtc,
                WhatsAppStatusDisplay = BuildWhatsAppStatusDisplay(appointment, latestLog)
            };
        }

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

        private async Task<Dictionary<int, OutboundLogProjection>> LoadLatestOutboundLogMapAsync(
            IEnumerable<int> citaIds,
            CancellationToken cancellationToken)
        {
            var ids = citaIds
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
            {
                return new Dictionary<int, OutboundLogProjection>();
            }

            var logs = await _context.WhatsAppMessageLogs
                .AsNoTracking()
                .Where(message =>
                    message.CitaId.HasValue &&
                    ids.Contains(message.CitaId.Value) &&
                    message.Direction == WhatsAppMessageDirections.Outbound &&
                    (message.NotificationType == WhatsAppNotificationTypes.Confirmation ||
                     message.NotificationType == WhatsAppNotificationTypes.Reminder3Hours))
                .OrderByDescending(message => message.CreatedAtUtc)
                .ThenByDescending(message => message.Id)
                .Select(message => new OutboundLogProjection
                {
                    CitaId = message.CitaId!.Value,
                    Status = message.Status,
                    ErrorCode = message.ErrorCode,
                    NotificationType = message.NotificationType
                })
                .ToListAsync(cancellationToken);

            return logs
                .GroupBy(log => log.CitaId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private static string BuildWhatsAppConsentDisplay(AppointmentProjection appointment)
        {
            if (!string.Equals(appointment.Tipo, "CITA", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (appointment.ClienteId.HasValue)
            {
                return appointment.ClienteAceptaMensajesWhatsApp == true
                    ? "WhatsApp autorizado para este cliente."
                    : "WhatsApp no autorizado para este cliente. No se enviarán confirmaciones ni recordatorios.";
            }

            return appointment.WhatsAppConsentAtCreation
                ? "WhatsApp autorizado para esta cita manual."
                : "WhatsApp no autorizado para esta cita. No se enviarán confirmaciones ni recordatorios.";
        }

        private static string BuildWhatsAppStatusDisplay(
            AppointmentProjection appointment,
            OutboundLogProjection? latestLog)
        {
            if (!string.Equals(appointment.Tipo, "CITA", StringComparison.OrdinalIgnoreCase))
            {
                return "WhatsApp: no aplica";
            }

            if (IsConsentMissing(latestLog) || !HasEffectiveConsent(appointment))
            {
                return "WhatsApp: no enviado. Motivo: cliente sin autorización.";
            }

            if (appointment.EstadoConfirmacionWhatsApp == WhatsAppConfirmationStates.ErrorEnvio ||
                string.Equals(latestLog?.Status, WhatsAppMessageStatuses.Failed, StringComparison.Ordinal))
            {
                return "WhatsApp: error de envío";
            }

            if (string.Equals(latestLog?.Status, WhatsAppMessageStatuses.Pending, StringComparison.Ordinal) ||
                string.Equals(latestLog?.Status, WhatsAppMessageStatuses.Processing, StringComparison.Ordinal))
            {
                return "WhatsApp: pendiente de envío";
            }

            if (latestLog is not null && IsSkippedStatus(latestLog.Status))
            {
                return "WhatsApp: no enviado";
            }

            if (appointment.RecordatorioWhatsAppTresHorasEnviadoUtc.HasValue ||
                (latestLog is not null &&
                 string.Equals(latestLog.NotificationType, WhatsAppNotificationTypes.Reminder3Hours, StringComparison.Ordinal) &&
                 IsSentStatus(latestLog.Status)))
            {
                return "WhatsApp: recordatorio 3h enviado";
            }

            if (appointment.ConfirmacionWhatsAppEnviadaUtc.HasValue ||
                (latestLog is not null &&
                 string.Equals(latestLog.NotificationType, WhatsAppNotificationTypes.Confirmation, StringComparison.Ordinal) &&
                 IsSentStatus(latestLog.Status)))
            {
                return "WhatsApp: confirmación enviada";
            }

            return "WhatsApp: autorizado";
        }

        private static bool HasEffectiveConsent(AppointmentProjection appointment) =>
            appointment.ClienteId.HasValue
                ? appointment.ClienteAceptaMensajesWhatsApp == true
                : appointment.WhatsAppConsentAtCreation;

        private static bool IsConsentMissing(OutboundLogProjection? latestLog) =>
            latestLog is not null &&
            (string.Equals(latestLog.Status, WhatsAppMessageStatuses.SkippedConsentMissing, StringComparison.Ordinal) ||
             string.Equals(latestLog.ErrorCode, WhatsAppErrorCodes.ConsentMissing, StringComparison.Ordinal));

        private static bool IsSentStatus(string? status) =>
            string.Equals(status, WhatsAppMessageStatuses.Sent, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.Delivered, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.Read, StringComparison.Ordinal);

        private static bool IsSkippedStatus(string? status) =>
            string.Equals(status, WhatsAppMessageStatuses.SkippedConsentMissing, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.SkippedInvalidPhone, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.SkippedConfiguration, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.SkippedTenantDisabled, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.SkippedDailyLimitExceeded, StringComparison.Ordinal) ||
            string.Equals(status, WhatsAppMessageStatuses.SkippedNotEligible, StringComparison.Ordinal);

        private sealed class OccupiedDateProjection
        {
            public DateTime FechaHoraCita { get; init; }

            public int Duracion { get; init; }

            public int FuncionarioId { get; init; }
        }

        private sealed class AppointmentProjection
        {
            public int Id { get; init; }

            public string Tipo { get; init; } = string.Empty;

            public string? NombreCliente { get; init; }

            public string? TelefonoCliente { get; init; }

            public int? ClienteId { get; init; }

            public int? ServicioId { get; init; }

            public string? ServicioNombre { get; init; }

            public DateTime FechaHoraCita { get; init; }

            public int FuncionarioId { get; init; }

            public int DuracionMinutos { get; init; }

            public string FuncionarioNombre { get; init; } = string.Empty;

            public string ColorCalendario { get; init; } = string.Empty;

            public bool WhatsAppConsentAtCreation { get; init; }

            public string? WhatsAppConsentSource { get; init; }

            public DateTime? WhatsAppConsentCapturedAtUtc { get; init; }

            public bool? ClienteAceptaMensajesWhatsApp { get; init; }

            public string EstadoConfirmacionWhatsApp { get; init; } = string.Empty;

            public DateTime? ConfirmacionWhatsAppEnviadaUtc { get; init; }

            public DateTime? RecordatorioWhatsAppTresHorasEnviadoUtc { get; init; }
        }

        private sealed class OutboundLogProjection
        {
            public int CitaId { get; init; }

            public string Status { get; init; } = string.Empty;

            public string? ErrorCode { get; init; }

            public string NotificationType { get; init; } = string.Empty;
        }
    }
}
