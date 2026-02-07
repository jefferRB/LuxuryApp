using Microsoft.AspNetCore.Mvc.Rendering;

namespace LuxuryApp.Models.Finanzas
{
    public class EgresoIndexViewModel
    {
        public List<Egreso> Egresos { get; set; }

        public EgresoFiltroViewModel Filtros { get; set; }

        public List<SelectListItem> MetodosPago { get; set; }

        public List<SelectListItem> Categorias { get; set; }


        public decimal TotalEgresos { get; set; }

        public int CantidadRegistros { get; set; }
    }
}
