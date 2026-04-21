namespace LuxuryApp.Services.Funcionarios
{
    public class RegistrarLiquidacionSemanalCommand
    {
        public DateTime SemanaInicio { get; set; }
        public DateTime SemanaFin { get; set; }
        public DateTime? FechaPago { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
        public string? Observacion { get; set; }
        public string? CreadoPor { get; set; }
        public List<RegistrarLiquidacionSemanalDetalleCommand> Detalles { get; set; } = new();
    }

    public class RegistrarLiquidacionSemanalDetalleCommand
    {
        public int FuncionarioId { get; set; }
        public decimal MontoPagado { get; set; }
    }
}
