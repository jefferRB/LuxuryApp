namespace LuxuryApp.Models.Finanzas
{
    public sealed class CobroCreateRequest
    {
        public DateTime FechaCobro { get; init; }
        public string NombreCliente { get; init; } = string.Empty;
        public int FuncionarioId { get; init; }
        public int? ServicioId { get; init; }
        public int? ProductoId { get; init; }
        public decimal Monto { get; init; }
        public string MetodoPago { get; init; } = string.Empty;
        public string? Observaciones { get; init; }
    }
}
