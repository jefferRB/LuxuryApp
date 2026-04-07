using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LuxuryApp.Models.SaaS
{
    public class HistorialSuscripcion
    {
        public Guid Id { get; set; }

        public Guid SuscripcionId { get; set; }

        public Guid? PlanIdAnterior { get; set; }

        public Guid? PlanIdNuevo { get; set; }

        public DateTime FechaCambio { get; set; } = DateTime.Now;

        // Relación
        [ForeignKey("SuscripcionId")]
        public Suscripcion Suscripcion { get; set; }
    }
}