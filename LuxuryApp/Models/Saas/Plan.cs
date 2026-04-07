using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Saas;

namespace LuxuryApp.Models.SaaS
{
    public class Plan
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Nombre { get; set; } // Free, Pro, Premium

        [MaxLength(100)]
        public string StripeProductId { get; set; }

        [MaxLength(100)]
        public string StripePriceId { get; set; }

        [MaxLength(100)]
        public string Moneda { get; set; }


        [Required]
        public decimal PrecioMensual { get; set; }

        public bool Activo { get; set; } = true;

        // Navegación
        public ICollection<PlanFeature> PlanFeatures { get; set; }
    }
}