namespace LuxuryApp.Models.Funcionarios
{
    public class PagosSemanaResumen
    {
        public DateTime InicioSemana { get; set; }
        public DateTime FinSemana { get; set; }
        public List<PagoFuncionarioVM> Funcionarios { get; set; } = new();
        public decimal TotalGeneradoServicios { get; set; }
        public decimal TotalGeneradoProductos { get; set; }
        public decimal TotalGeneradoGeneral { get; set; }
        public decimal TotalImpuestosGeneral { get; set; }
        public decimal TotalSinImpuestosGeneral { get; set; }
        public decimal TotalPagadoGeneral { get; set; }
        public decimal TotalPendienteGeneral { get; set; }
        public decimal GananciaNegocio { get; set; }
    }
}
