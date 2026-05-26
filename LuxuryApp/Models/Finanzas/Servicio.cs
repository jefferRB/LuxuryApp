using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Finanzas
{
    public class Servicio : ITenantEntity
    {
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }
        public int Id { get; set; }

        [Required]
        [Display(Name = "Servicio")]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Precio")]
        [DecimalRange(0.01, 999999, ErrorMessage = "Debe indicar un precio mayor a cero y dentro del rango permitido.")]
        public decimal Precio { get; set; }

        public bool Activo { get; set; } = true;

        public int? DuracionMinutos { get; set; }
    }
}
