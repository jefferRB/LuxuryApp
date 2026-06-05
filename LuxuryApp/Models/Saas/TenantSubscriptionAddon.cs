using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.SaaS
{
    public class TenantSubscriptionAddon : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }

        public Guid PlanId { get; set; }

        [MaxLength(50)]
        public string AddonCode { get; set; } = string.Empty;

        public EstadoSuscripcion Estado { get; set; } = EstadoSuscripcion.Pendiente;

        public int? TilopayRecurringPlanId { get; set; }

        [MaxLength(100)]
        public string? ProviderSubscriptionId { get; set; }

        [MaxLength(100)]
        public string? ProviderTransactionId { get; set; }

        public decimal? PrecioMensual { get; set; }

        [MaxLength(10)]
        public string? MonedaFacturacion { get; set; }

        public int MonthlyMessageLimit { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

        public DateTime? FechaFin { get; set; }

        public DateTime? FechaProximoCobroUtc { get; set; }

        public DateTime? FechaFinGraciaUtc { get; set; }

        public DateTime? FechaCancelacionUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Tenant? Tenant { get; set; }
        public Plan? Plan { get; set; }
    }
}
