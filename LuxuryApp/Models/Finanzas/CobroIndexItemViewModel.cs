namespace LuxuryApp.Models.Finanzas
{
    public sealed class CobroIndexItemViewModel
    {
        public int IdCobro { get; init; }
        public DateTime FechaCobro { get; init; }
        public string NombreCliente { get; init; } = string.Empty;
        public string FuncionarioNombre { get; init; } = string.Empty;
        public string Detalle { get; init; } = string.Empty;
        public decimal Monto { get; init; }
        public string MetodoPago { get; init; } = string.Empty;
        public bool EsServicio { get; init; }
        public bool EsProducto => !EsServicio;
    }
}
