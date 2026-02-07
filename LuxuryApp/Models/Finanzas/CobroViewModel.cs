using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LuxuryApp.Models.Finanzas
{
    public class CobroViewModel
    {
        public Cobro Cobro { get; set; }
        [ValidateNever]
        public List<SelectListItem> Barberos { get; set; }
        [ValidateNever]
        public List<SelectListItem> Servicios { get; set; }
        [ValidateNever]
        public List<SelectListItem> MetodosPago { get; set; }
        [ValidateNever]
        public List<SelectListItem> Productos { get; set; }
        [ValidateNever]
        public List<DetalleCobroProducto> ProductosVendidos { get; set; } = new();
    }
}
