using Microsoft.AspNetCore.Mvc.Rendering;

namespace LuxuryApp.Models.Finanzas
{
    public class CobroIndexViewModel
    {
        public List<Cobro> Cobros { get; set; } = new();

        public CobroFiltroViewModel Filtros { get; set; } = new();

        public List<SelectListItem> Funcionarios { get; set; } = new();
        public List<SelectListItem> MetodosPago { get; set; } = new();
        public decimal TotalCobrado { get; set; }
        public int CantidadServicios { get; set; }

        public decimal TotalServicios { get; set; }

        public decimal TotalProductos { get; set; }

        public decimal TotalGenerado { get; set; }

        public decimal TotalSinImpuestos { get; set; }

        public decimal TotalImpuestos { get; set; }

        public decimal PagoColaboradores { get; set; }

        public decimal GananciaNegocio { get; set; }

        public decimal GananciaEfectivo { get; set; }

        public decimal GananciaTarjeta { get; set; }

        public decimal GananciaSinpe { get; set; }
    }
    
}
