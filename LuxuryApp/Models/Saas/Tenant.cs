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

        // Navegación
        public ICollection<Suscripcion> Suscripciones { get; set; } = new List<Suscripcion>();
    }
}
