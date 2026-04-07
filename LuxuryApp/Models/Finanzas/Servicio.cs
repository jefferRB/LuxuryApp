using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Finanzas
{
    public class Servicio : ITenantEntity
    {
        public Guid TenantId { get; set; }
        public int Id { get; set; }

        [Required]
        [Display(Name = "Servicio")]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Precio")]
        [Range(0, 999999)]
        public decimal Precio { get; set; }

        public bool Activo { get; set; } = true;

        public int? DuracionMinutos { get; set; }
    }
}
