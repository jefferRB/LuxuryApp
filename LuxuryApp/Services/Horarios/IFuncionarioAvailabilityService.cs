using LuxuryApp.Models.Horarios;

namespace LuxuryApp.Services.Horarios
{
    /// <summary>Origen de un intervalo ocupado. Estable: viaja en respuestas JSON.</summary>
    public static class BusyIntervalSources
    {
        public const string Cita = "CITA";
        public const string Descanso = "DESCANSO";
        public const string BloqueoRecurrente = "BLOQUEO_RECURRENTE";
    }

    /// <summary>
    /// Intervalo en el que un colaborador NO está disponible. Las horas son locales del negocio,
    /// igual que <c>Cita.FechaHoraCita</c>.
    /// </summary>
    public sealed record BusyInterval(
        DateTime Inicio,
        DateTime Fin,
        string Origen,
        string? Titulo = null,
        int? ReferenciaId = null)
    {
        public bool Solapa(DateTime inicio, DateTime fin) => inicio < Fin && fin > Inicio;

        public bool EsBloqueoRecurrente => Origen == BusyIntervalSources.BloqueoRecurrente;
    }

    /// <summary>Resultado de comprobar un horario concreto.</summary>
    public sealed record AvailabilityCheckResult(bool Disponible, string? Motivo, BusyInterval? Conflicto)
    {
        public static AvailabilityCheckResult Libre() => new(true, null, null);

        public static AvailabilityCheckResult Ocupado(string motivo, BusyInterval conflicto) =>
            new(false, motivo, conflicto);
    }

    /// <summary>
    /// Fuente ÚNICA de disponibilidad de colaboradores. Combina citas/descansos con los bloqueos
    /// recurrentes, para que reservas públicas, creación manual, reprogramación, búsqueda de
    /// espacios y calendario respondan exactamente lo mismo.
    ///
    /// <para>
    /// Antes de este servicio la validación de solapamiento vivía duplicada en
    /// <c>CalendarCommandService</c> y <c>BookingAvailabilityService</c>. Ahora ambas la consumen.
    /// </para>
    /// </summary>
    public interface IFuncionarioAvailabilityService
    {
        /// <summary>
        /// Intervalos ocupados por colaborador en el rango [desde, hasta] (ambos inclusive).
        /// Una sola consulta por origen: no hay N+1.
        /// </summary>
        Task<Dictionary<int, List<BusyInterval>>> GetBusyIntervalsAsync(
            IReadOnlyCollection<int> funcionarioIds,
            DateOnly desde,
            DateOnly hasta,
            int? excludeCitaId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Comprueba un horario concreto de un colaborador. Es el punto que usan la creación de
        /// citas, la edición, el movimiento y el cambio de duración.
        /// </summary>
        Task<AvailabilityCheckResult> CheckAsync(
            int funcionarioId,
            DateTime inicio,
            int duracionMinutos,
            int? excludeCitaId = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Ocurrencias de bloqueos recurrentes del rango, para pintarlas en el calendario.
        /// Si <paramref name="funcionarioIds"/> es null se usan todos los colaboradores activos.
        /// </summary>
        Task<IReadOnlyList<RecurringScheduleOccurrence>> GetRecurringBlocksAsync(
            DateOnly desde,
            DateOnly hasta,
            IReadOnlyCollection<int>? funcionarioIds = null,
            CancellationToken cancellationToken = default);
    }
}
