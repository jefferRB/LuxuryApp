using LuxuryApp.Models.Horarios;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Horarios
{
    /// <summary>
    /// Implementación de la disponibilidad unificada.
    ///
    /// <para>
    /// Zona horaria: los bloqueos recurrentes se guardan como hora local del negocio y las citas
    /// también (<c>Cita.FechaHoraCita</c> es hora de pared, sin offset). Por eso las ocurrencias se
    /// expanden directamente a <c>DateTime</c> local y se comparan sin conversión. La conversión a
    /// UTC solo ocurre donde el flujo actual ya la hace (por ejemplo el envío de WhatsApp).
    /// </para>
    /// </summary>
    public sealed class FuncionarioAvailabilityService : IFuncionarioAvailabilityService
    {
        /// <summary>Duración por defecto de una cita sin duración explícita (igual que el calendario).</summary>
        internal const int DefaultDurationMinutes = 30;

        private readonly ApplicationDbContext _context;

        public FuncionarioAvailabilityService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Dictionary<int, List<BusyInterval>>> GetBusyIntervalsAsync(
            IReadOnlyCollection<int> funcionarioIds,
            DateOnly desde,
            DateOnly hasta,
            int? excludeCitaId = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(funcionarioIds);

            var map = new Dictionary<int, List<BusyInterval>>();
            if (funcionarioIds.Count == 0 || hasta < desde)
            {
                return map;
            }

            // Ventana amplia: incluye citas que arrancaron el día anterior (duraciones largas).
            var rangoInicio = desde.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var rangoFin = hasta.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var citas = await _context.Citas
                .AsNoTracking()
                .Where(cita =>
                    funcionarioIds.Contains(cita.FuncionarioId) &&
                    cita.FechaHoraCita >= rangoInicio &&
                    cita.FechaHoraCita < rangoFin &&
                    (!excludeCitaId.HasValue || cita.Id != excludeCitaId.Value))
                .Select(cita => new
                {
                    cita.Id,
                    cita.FuncionarioId,
                    cita.FechaHoraCita,
                    cita.Tipo,
                    Duracion = cita.Tipo == "DESCANSO"
                        ? (cita.DuracionMinutos ?? DefaultDurationMinutes)
                        : (cita.DuracionMinutos ?? (cita.Servicio != null ? cita.Servicio.DuracionMinutos : null) ?? DefaultDurationMinutes)
                })
                .ToListAsync(cancellationToken);

            foreach (var cita in citas)
            {
                Add(map, cita.FuncionarioId, new BusyInterval(
                    cita.FechaHoraCita,
                    cita.FechaHoraCita.AddMinutes(cita.Duracion),
                    cita.Tipo == "DESCANSO" ? BusyIntervalSources.Descanso : BusyIntervalSources.Cita,
                    null,
                    cita.Id));
            }

            var bloqueos = await GetRecurringBlocksAsync(desde, hasta, funcionarioIds, cancellationToken);
            foreach (var bloqueo in bloqueos)
            {
                Add(map, bloqueo.FuncionarioId, new BusyInterval(
                    bloqueo.Inicio,
                    bloqueo.Fin,
                    BusyIntervalSources.BloqueoRecurrente,
                    bloqueo.Titulo,
                    bloqueo.RuleId));
            }

            return map;
        }

        public async Task<AvailabilityCheckResult> CheckAsync(
            int funcionarioId,
            DateTime inicio,
            int duracionMinutos,
            int? excludeCitaId = null,
            CancellationToken cancellationToken = default)
        {
            if (duracionMinutos <= 0)
            {
                return AvailabilityCheckResult.Libre();
            }

            var fin = inicio.AddMinutes(duracionMinutos);
            var desde = DateOnly.FromDateTime(inicio);
            var hasta = DateOnly.FromDateTime(fin);

            var map = await GetBusyIntervalsAsync(
                new[] { funcionarioId },
                desde,
                hasta,
                excludeCitaId,
                cancellationToken);

            if (!map.TryGetValue(funcionarioId, out var ocupados))
            {
                return AvailabilityCheckResult.Libre();
            }

            // El bloqueo recurrente gana en el mensaje: es más útil decir "coincide con Almuerzo"
            // que un genérico "ya hay una cita".
            var conflicto = ocupados
                .Where(intervalo => intervalo.Solapa(inicio, fin))
                .OrderByDescending(intervalo => intervalo.EsBloqueoRecurrente)
                .FirstOrDefault();

            if (conflicto is null)
            {
                return AvailabilityCheckResult.Libre();
            }

            var motivo = conflicto.Origen switch
            {
                BusyIntervalSources.BloqueoRecurrente =>
                    $"Ese horario está bloqueado por «{conflicto.Titulo ?? "bloqueo recurrente"}» " +
                    $"({conflicto.Inicio:HH:mm} a {conflicto.Fin:HH:mm}).",
                BusyIntervalSources.Descanso => "Ya existe un descanso en ese horario.",
                _ => "Ya existe una cita o descanso en ese horario."
            };

            return AvailabilityCheckResult.Ocupado(motivo, conflicto);
        }

        public async Task<IReadOnlyList<RecurringScheduleOccurrence>> GetRecurringBlocksAsync(
            DateOnly desde,
            DateOnly hasta,
            IReadOnlyCollection<int>? funcionarioIds = null,
            CancellationToken cancellationToken = default)
        {
            if (hasta < desde)
            {
                return Array.Empty<RecurringScheduleOccurrence>();
            }

            // Solo las reglas activas cuya vigencia toca el rango.
            var reglas = await _context.RecurringScheduleRules
                .AsNoTracking()
                .Include(regla => regla.Colaboradores)
                .Where(regla =>
                    regla.Activa &&
                    regla.VigenteDesde <= hasta &&
                    (regla.VigenteHasta == null || regla.VigenteHasta >= desde))
                .ToListAsync(cancellationToken);

            if (reglas.Count == 0)
            {
                return Array.Empty<RecurringScheduleOccurrence>();
            }

            var reglaIds = reglas.Select(regla => regla.Id).ToList();

            var excepciones = await _context.RecurringScheduleExceptions
                .AsNoTracking()
                .Where(exception =>
                    reglaIds.Contains(exception.RuleId) &&
                    exception.Fecha >= desde &&
                    exception.Fecha <= hasta)
                .ToListAsync(cancellationToken);

            var excepcionesPorRegla = excepciones
                .GroupBy(exception => exception.RuleId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var regla in reglas)
            {
                regla.Excepciones = excepcionesPorRegla.TryGetValue(regla.Id, out var propias)
                    ? propias
                    : new List<RecurringScheduleException>();
            }

            // Alcance global = colaboradores ACTIVOS evaluados dinámicamente. Un colaborador creado
            // hoy queda cubierto sin tocar la regla; uno inactivo deja de bloquear su agenda.
            var candidatos = funcionarioIds is { Count: > 0 }
                ? await _context.Funcionarios
                    .AsNoTracking()
                    .Where(funcionario => funcionarioIds.Contains(funcionario.IdFuncionario))
                    .Select(funcionario => funcionario.IdFuncionario)
                    .ToListAsync(cancellationToken)
                : await _context.Funcionarios
                    .AsNoTracking()
                    .Where(funcionario => funcionario.Activo)
                    .Select(funcionario => funcionario.IdFuncionario)
                    .ToListAsync(cancellationToken);

            if (candidatos.Count == 0)
            {
                return Array.Empty<RecurringScheduleOccurrence>();
            }

            return RecurringScheduleOccurrenceCalculator.Expand(reglas, candidatos, desde, hasta);
        }

        private static void Add(Dictionary<int, List<BusyInterval>> map, int funcionarioId, BusyInterval intervalo)
        {
            if (!map.TryGetValue(funcionarioId, out var lista))
            {
                lista = new List<BusyInterval>();
                map[funcionarioId] = lista;
            }

            lista.Add(intervalo);
        }
    }
}
