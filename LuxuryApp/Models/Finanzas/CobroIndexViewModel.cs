using Microsoft.AspNetCore.Mvc.Rendering;

namespace LuxuryApp.Models.Finanzas
{
    public class CobroIndexViewModel
    {
        public List<Cobro> Cobros { get; set; }

        public CobroFiltroViewModel Filtros { get; set; }

        public List<SelectListItem> Barberos { get; set; }

        public List<SelectListItem> MetodosPago { get; set; }
        public decimal TotalCobrado { get; set; }
        public int CantidadServicios { get; set; }
    }
}
