using LuxuryApp.Models.Reservas;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Reservas
{
    public sealed class BookingAvailabilityService : IBookingAvailabilityService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public BookingAvailabilityService(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        public async Task<IReadOnlyList<string>> GetAvailableSlotsAsync(
            int servicioId,
            DateOnly fecha,
            int? funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var settings = await _context.TenantBookingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is null || !settings.PublicBookingEnabled)
            {
                return Array.Empty<string>();
            }

            // Validar rango de fechas permitido y día laboral.
            var today = DateOnly.FromDateTime(_businessDateTimeProvider.Today());
            var maxDate = today.AddDays(Math.Max(0, settings.PublicBookingMaxDaysAhead));

            if (fecha < today || fecha > maxDate || !settings.IsWorkingDay(fecha.DayOfWeek))
            {
                return Array.Empty<string>();
            }

            var duracion = await ResolveServicioDuracionAsync(servicioId, cancellationToken);
            if (duracion is null)
            {
                return Array.Empty<string>();
            }

            var candidatos = await ResolveCandidatosAsync(funcionarioId, cancellationToken);
            if (candidatos.Count == 0)
            {
                return Array.Empty<string>();
            }

            var busyByFuncionario = await LoadBusyIntervalsAsync(candidatos, fecha, cancellationToken);

            var now = _businessDateTimeProvider.Now();
            var earliest = now.AddMinutes(Math.Max(0, settings.PublicBookingMinAdvanceMinutes));
            var intervalo = Math.Max(5, settings.SlotIntervalMinutes);

            var resultados = new List<string>();
            var cursor = settings.OpenTime;
            var cierre = settings.CloseTime;

            // Genera slots [cursor, cursor+duracion] que terminen a más tardar al cierre.
            while (true)
            {
                var inicio = fecha.ToDateTime(cursor);
                var fin = inicio.AddMinutes(duracion.Value);

                if (TimeOnly.FromDateTime(fin) > cierre || fin.Date != inicio.Date)
                {
                    break;
                }

                if (inicio >= earliest && EstaLibre(busyByFuncionario, candidatos, inicio, fin))
                {
                    resultados.Add(cursor.ToString("HH:mm"));
                }

                var siguiente = cursor.AddMinutes(intervalo);
                // Evita loop infinito si AddMinutes envuelve el día.
                if (siguiente <= cursor)
                {
                    break;
                }

                cursor = siguiente;
            }

            return resultados;
        }

        public async Task<SlotResolution> ResolveSlotAsync(
            int servicioId,
            DateTime inicio,
            int? funcionarioId,
            CancellationToken cancellationToken = default)
        {
            var duracion = await ResolveServicioDuracionAsync(servicioId, cancellationToken);
            if (duracion is null)
            {
                return SlotResolution.NoDisponible("El servicio ya no está disponible.");
            }

            var fecha = DateOnly.FromDateTime(inicio);
            var fin = inicio.AddMinutes(duracion.Value);

            // Revalida día laboral y ventana horaria del negocio en backend (no se confía en el
            // frontend: un POST manipulado podría pedir un día no laboral u hora fuera de jornada).
            // Punto único compartido por la solicitud pública y la confirmación del admin.
            var settings = await _context.TenantBookingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (settings is not null)
            {
                if (!settings.IsWorkingDay(fecha.DayOfWeek))
                {
                    return SlotResolution.NoDisponible("Ese día no está disponible para reservas.");
                }

                var horaInicio = TimeOnly.FromDateTime(inicio);
                var horaFin = TimeOnly.FromDateTime(fin);
                if (fin.Date != inicio.Date || horaInicio < settings.OpenTime || horaFin > settings.CloseTime)
                {
                    return SlotResolution.NoDisponible("Ese horario está fuera de la jornada del negocio.");
                }
            }

            var candidatos = await ResolveCandidatosAsync(funcionarioId, cancellationToken);
            if (candidatos.Count == 0)
            {
                return SlotResolution.NoDisponible("No hay funcionarios disponibles para esta reserva.");
            }

            var busyByFuncionario = await LoadBusyIntervalsAsync(candidatos, fecha, cancellationToken);

            foreach (var candidato in candidatos)
            {
                if (!Solapa(busyByFuncionario, candidato, inicio, fin))
                {
                    return new SlotResolution
                    {
                        Disponible = true,
                        FuncionarioId = candidato,
                        DuracionMinutos = duracion.Value
                    };
                }
            }

            return SlotResolution.NoDisponible("Ese horario ya no está disponible.");
        }

        private async Task<int?> ResolveServicioDuracionAsync(int servicioId, CancellationToken cancellationToken)
        {
            if (servicioId <= 0)
            {
                return null;
            }

            var servicio = await _context.Servicios
                .AsNoTracking()
                .Where(s => s.Id == servicioId && s.Activo)
                .Select(s => new { s.DuracionMinutos })
                .SingleOrDefaultAsync(cancellationToken);

            if (servicio is null)
            {
                return null;
            }

            return servicio.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes;
        }

        private async Task<IReadOnlyList<int>> ResolveCandidatosAsync(
            int? funcionarioId,
            CancellationToken cancellationToken)
        {
            if (funcionarioId.HasValue && funcionarioId.Value > 0)
            {
                var existe = await _context.Funcionarios
                    .AsNoTracking()
                    .AnyAsync(f => f.IdFuncionario == funcionarioId.Value && f.Activo, cancellationToken);

                return existe ? new[] { funcionarioId.Value } : Array.Empty<int>();
            }

            return await _context.Funcionarios
                .AsNoTracking()
                .Where(f => f.Activo)
                .Select(f => f.IdFuncionario)
                .ToListAsync(cancellationToken);
        }

        private async Task<Dictionary<int, List<(DateTime Inicio, DateTime Fin)>>> LoadBusyIntervalsAsync(
            IReadOnlyList<int> funcionarioIds,
            DateOnly fecha,
            CancellationToken cancellationToken)
        {
            // Rango amplio: incluye citas que empezaron el día anterior (duraciones largas)
            // y cualquier cita del día solicitado.
            var rangoInicio = fecha.ToDateTime(TimeOnly.MinValue).AddDays(-1);
            var rangoFin = fecha.ToDateTime(TimeOnly.MinValue).AddDays(1);

            var citas = await _context.Citas
                .AsNoTracking()
                .Where(c =>
                    funcionarioIds.Contains(c.FuncionarioId) &&
                    c.FechaHoraCita >= rangoInicio &&
                    c.FechaHoraCita < rangoFin)
                .Select(c => new
                {
                    c.FuncionarioId,
                    c.FechaHoraCita,
                    Duracion = c.Tipo == "DESCANSO"
                        ? (c.DuracionMinutos ?? CalendarCommandService.DefaultDurationMinutes)
                        : (c.DuracionMinutos ?? (c.Servicio != null ? c.Servicio.DuracionMinutos : null) ?? CalendarCommandService.DefaultDurationMinutes)
                })
                .ToListAsync(cancellationToken);

            var map = new Dictionary<int, List<(DateTime, DateTime)>>();
            foreach (var cita in citas)
            {
                if (!map.TryGetValue(cita.FuncionarioId, out var lista))
                {
                    lista = new List<(DateTime, DateTime)>();
                    map[cita.FuncionarioId] = lista;
                }

                lista.Add((cita.FechaHoraCita, cita.FechaHoraCita.AddMinutes(cita.Duracion)));
            }

            return map;
        }

        private static bool EstaLibre(
            Dictionary<int, List<(DateTime Inicio, DateTime Fin)>> busyByFuncionario,
            IReadOnlyList<int> candidatos,
            DateTime inicio,
            DateTime fin)
        {
            foreach (var candidato in candidatos)
            {
                if (!Solapa(busyByFuncionario, candidato, inicio, fin))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Solapa(
            Dictionary<int, List<(DateTime Inicio, DateTime Fin)>> busyByFuncionario,
            int funcionarioId,
            DateTime inicio,
            DateTime fin)
        {
            if (!busyByFuncionario.TryGetValue(funcionarioId, out var ocupados))
            {
                return false;
            }

            foreach (var (ocupadoInicio, ocupadoFin) in ocupados)
            {
                if (inicio < ocupadoFin && fin > ocupadoInicio)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
