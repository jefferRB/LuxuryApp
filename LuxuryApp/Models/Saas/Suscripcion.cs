using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LuxuryApp.Models.SaaS
{
    public class Suscripcion
    {
        public Guid Id { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public Guid PlanId { get; set; }

        // Stripe
        [MaxLength(100)]
        public string? StripeCustomerId { get; set; }

        [MaxLength(100)]
        public string? StripeSubscriptionId { get; set; }

        [Required]
        public EstadoSuscripcion Estado { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.Now;

        public DateTime? FechaFin { get; set; }

        public DateTime? FechaTrialFin { get; set; }
        public bool CancelAtPeriodEnd { get; set; }

        // Relaciones
        [ForeignKey("TenantId")]
        public Tenant Tenant { get; set; }

        [ForeignKey("PlanId")]
        public Plan Plan { get; set; }

        public ICollection<HistorialSuscripcion> Historiales { get; set; }
    }
}