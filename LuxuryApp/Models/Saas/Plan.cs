using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Saas;

namespace LuxuryApp.Models.SaaS
{
    public class Plan
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nombre { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ProviderProductId { get; set; }

        [MaxLength(100)]
        public string? ProviderPriceId { get; set; }

        [MaxLength(100)]
        public string Moneda { get; set; } = "CRC";

        [Required]
        public decimal PrecioMensual { get; set; }
        public bool Activo { get; set; } = true;
        public bool EsPlanValidacion { get; set; } = false;
        public int? MaxFuncionarios { get; set; } // null = ilimitado

        // Navegación
        public ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();
    }
}
