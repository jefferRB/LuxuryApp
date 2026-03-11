namespace LuxuryApp.Models.Finanzas
{
    public class PagoFuncionarioVM
    {
        public int FuncionarioId { get; set; }
        public string Nombre { get; set; }

        public decimal TotalGenerado { get; set; }

        public decimal Impuestos { get; set; }

        public decimal TotalNeto { get; set; }

        public decimal Porcentaje { get; set; }

        public decimal PagoFinal { get; set; }
    }
}