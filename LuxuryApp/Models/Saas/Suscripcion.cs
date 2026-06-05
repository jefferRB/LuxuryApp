using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.SaaS
{
    public class Suscripcion : ITenantEntity
    {
        public Guid Id { get; set; }

        [Required]
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }

        [Required]
        public Guid PlanId { get; set; }

        public PaymentProviderType Proveedor { get; set; } = PaymentProviderType.None;

        [MaxLength(100)]
        public string? ProviderCustomerId { get; set; }

        [MaxLength(100)]
        public string? ProviderSubscriptionId { get; set; }

        [MaxLength(100)]
        public string? ProviderTransactionId { get; set; }

        [MaxLength(100)]
        public string? ProviderPaymentLinkId { get; set; }

        [MaxLength(100)]
        public string? ProviderReference { get; set; }

        [MaxLength(100)]
        public string? UltimoEventoProveedorId { get; set; }

        [MaxLength(50)]
        public string? CodigoPlan { get; set; }

        public int? TilopayRecurringPlanId { get; set; }

        [Required]
        public EstadoSuscripcion Estado { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.Now;

        public DateTime? FechaFin { get; set; }

        public DateTime? FechaTrialFin { get; set; }

        public DateTime? FechaProximoCobroUtc { get; set; }

        public DateTime? FechaFinGraciaUtc { get; set; }

        public DateTime? FechaCancelacionUtc { get; set; }

        public decimal? PrecioMensual { get; set; }

        [MaxLength(10)]
        public string? MonedaFacturacion { get; set; }

        public int? MaxFuncionarios { get; set; }

        public bool CancelAtPeriodEnd { get; set; }

        public DateTime? FechaUltimoPagoUtc { get; set; }

        public DateTime? FechaUltimaActualizacionUtc { get; set; }

        [MaxLength(250)]
        public string? MotivoEstado { get; set; }

        [ForeignKey("TenantId")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("PlanId")]
        public Plan? Plan { get; set; }

        public ICollection<HistorialSuscripcion> Historiales { get; set; } = new List<HistorialSuscripcion>();
    }
}
