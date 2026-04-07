using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Finanzas
{
    public class Categoria : ITenantEntity
    {
        public Guid TenantId { get; set; }
        public int Id { get; set; }

        [Required]
        [Display(Name = "Nombre categoria")]
        public string? Nombre { get; set; }

        [Required]
        [Display(Name = "Detalle")]
        
        public string? Detalle { get; set; }

        public bool Activo { get; set; } = true;
    }
}
