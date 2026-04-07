using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.SaaS
{
    public class StripeEvento
    {
        public Guid Id { get; set; }

        [MaxLength(100)]
        public string StripeEventId { get; set; }

        [MaxLength(100)]
        public string Tipo { get; set; }

        public bool Procesado { get; set; } = false;

        public string Payload { get; set; }

        public DateTime Fecha { get; set; } = DateTime.Now;
    }
}