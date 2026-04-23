using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Finanzas
{
    public class Categoria : ITenantEntity
    {
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }
        public int Id { get; set; }

        [Required]
        [Display(Name = "Nombre categoria")]
        [StringLength(150)]
        public string? Nombre { get; set; }

        [Required]
        [Display(Name = "Detalle")]
        [StringLength(500)]
        public string? Detalle { get; set; }

        public bool Activo { get; set; } = true;
    }
}
