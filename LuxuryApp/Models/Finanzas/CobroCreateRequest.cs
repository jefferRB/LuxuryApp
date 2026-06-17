namespace LuxuryApp.Models.Finanzas
{
    public sealed class CobroCreateRequest
    {
        public DateTime FechaCobro { get; init; }
        public string NombreCliente { get; init; } = string.Empty;
        public int? ClienteId { get; init; }
        public int FuncionarioId { get; init; }
        public int? ServicioId { get; init; }
        public int? ProductoId { get; init; }
        public decimal Monto { get; init; }
        public string MetodoPago { get; init; } = string.Empty;
        public string? Observaciones { get; init; }
        public bool ActualizarNotasServicio { get; init; }
        public string? NotasServicioTexto { get; init; }

        /// <summary>
        /// Cita de origen (opcional). Cuando viene del portal del funcionario, asocia el cobro
        /// a la cita para impedir doble cobro. El servicio valida pertenencia y unicidad.
        /// </summary>
        public int? CitaId { get; init; }
    }
}
