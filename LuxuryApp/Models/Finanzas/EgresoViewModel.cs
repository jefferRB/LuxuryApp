using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LuxuryApp.Models.Finanzas
{
    public class EgresoViewModel
    {
        public Egreso Egreso { get; set; }

        [ValidateNever]
        public List<SelectListItem> MetodosPago { get; set; }

        [ValidateNever]
        public List<SelectListItem> Categorias { get; set; }
    }
}
