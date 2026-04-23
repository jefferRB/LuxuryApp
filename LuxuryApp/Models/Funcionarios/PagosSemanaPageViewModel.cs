namespace LuxuryApp.Models.Funcionarios
{
    public sealed class PagosSemanaPageViewModel
    {
        public DateTime InicioSemana { get; init; }

        public DateTime FinSemana { get; init; }

        public DateTime FechaPagoSugerida { get; init; }

        public IReadOnlyList<string> MetodosPago { get; init; } = Array.Empty<string>();

        public IReadOnlyList<PagoFuncionarioVM> Funcionarios { get; init; } =
            Array.Empty<PagoFuncionarioVM>();

        public decimal TotalGeneradoServicios { get; init; }

        public decimal TotalGeneradoProductos { get; init; }

        public decimal TotalGeneradoGeneral { get; init; }

        public decimal TotalImpuestosGeneral { get; init; }

        public decimal TotalSinImpuestosGeneral { get; init; }

        public decimal TotalPagadoGeneral { get; init; }

        public decimal TotalPendienteGeneral { get; init; }

        public decimal GananciaNegocio { get; init; }
    }
}
