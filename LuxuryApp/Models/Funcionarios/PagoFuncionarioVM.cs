namespace LuxuryApp.Models.Funcionarios
{
    public class PagoFuncionarioVM
    {
        public int FuncionarioId { get; set; }

        public string Nombre { get; set; }

        public decimal TotalGenerado { get; set; }

        public decimal Impuestos { get; set; }

        public decimal TotalNeto { get; set; }

        public decimal Porcentaje { get; set; }

        public decimal PorcentajeProducto { get; set; }

        public decimal PagoFinal { get; set; }

        public decimal MontoPagado { get; set; }

        public decimal MontoPendiente { get; set; }

        public List<DetalleDiaVM> DetalleDias { get; set; }
        public List<PagoFuncionario> HistorialPagos { get; set; } = new();

        // 🔵 INDICADORES GENERALES
        public decimal TotalGeneradoGeneral { get; set; }

        public decimal TotalSinImpuestosGeneral { get; set; }

        public decimal TotalPagadoGeneral { get; set; }

        public decimal TotalPendienteGeneral { get; set; }

        public decimal GananciaNegocio { get; set; }
    }
}