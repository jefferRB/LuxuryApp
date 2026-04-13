using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.SaaS
{
    public class Tenant
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Nombre { get; set; } = string.Empty;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public bool Activo { get; set; } = true;

        public TenantCommercialAccessMode CommercialAccessMode { get; set; } = TenantCommercialAccessMode.RequiresSubscription;

        public Guid? ForcedPlanId { get; set; }

        [MaxLength(250)]
        public string? CommercialNotes { get; set; }

        public DateTime? CommercialUpdatedUtc { get; set; }

        [MaxLength(450)]
        public string? CommercialUpdatedByUserId { get; set; }

        public Plan? ForcedPlan { get; set; }

        // Navegación
        public ICollection<Suscripcion> Suscripciones { get; set; } = new List<Suscripcion>();
        public ICollection<TenantCommercialAccessGrant> CommercialAccessGrants { get; set; } = new List<TenantCommercialAccessGrant>();
    }
}
