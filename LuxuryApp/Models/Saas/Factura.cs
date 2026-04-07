using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LuxuryApp.Models.SaaS
{
    public class Factura
    {
        public Guid Id { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [MaxLength(100)]
        public string StripeInvoiceId { get; set; }

        public decimal? Monto { get; set; }

        [MaxLength(10)]
        public string Moneda { get; set; }

        [MaxLength(50)]
        public string Estado { get; set; }

        public DateTime? Fecha { get; set; }

        [ForeignKey("TenantId")]
        public Tenant Tenant { get; set; }
    }
}