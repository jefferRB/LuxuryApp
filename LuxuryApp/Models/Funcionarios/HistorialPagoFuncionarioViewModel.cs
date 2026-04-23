namespace LuxuryApp.Models.Funcionarios
{
    public sealed class HistorialPagoFuncionarioViewModel
    {
        public int ReferenciaId { get; init; }

        public int FuncionarioId { get; init; }

        public decimal MontoPagado { get; init; }

        public DateTime FechaPago { get; init; }

        public DateTime InicioSemana { get; init; }

        public DateTime FinSemana { get; init; }

        public string? Observacion { get; init; }

        public string OrigenRegistro { get; init; } = string.Empty;
    }
}
