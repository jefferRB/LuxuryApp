using Microsoft.AspNetCore.Mvc.Rendering;

namespace LuxuryApp.Models.Finanzas
{
    public class CobroIndexViewModel
    {
        public List<Cobro> Cobros { get; set; }

        public CobroFiltroViewModel Filtros { get; set; }

        public List<SelectListItem> Funcionarios { get; set; }
        public List<SelectListItem> MetodosPago { get; set; }
        public decimal TotalCobrado { get; set; }
        public int CantidadServicios { get; set; }

        public decimal TotalImpuestos { get; set; }
        public decimal PagoColaboradores { get; set; }
        public decimal TotalNeto { get; set; }
        public decimal GananciaNegocio { get; set; }
    }
}
