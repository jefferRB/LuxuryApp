using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Calendar;

namespace LuxuryApp.Models.Finanzas
{
    public class Cobro
    {
        [Key]
        public int IdCobro { get; set; }

        [Required]
        [Display(Name = "Fecha")]
        public DateTime FechaCobro { get; set; } = DateTime.Now;

        [Required]
        [Display(Name = "Nombre Cliente")]
        public string NombreCliente { get; set; }

        [Required]
        [Display(Name = "Barbero")]
        public int BarberoId { get; set; }

        [Required]
        [Display(Name = "Servicio")]
        public int ServicioId { get; set; }

        [Required]
        [Display(Name = "Monto")]
        [Range(0, 999999)]
        public decimal Monto { get; set; }

        [Required]
        [Display(Name = "Método de Pago")]
        public string MetodoPago { get; set; }

        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }

        // 🔗 Navegación EF
        public Barbero? Barbero { get; set; }
        public Servicio? Servicio { get; set; }
    }
}
