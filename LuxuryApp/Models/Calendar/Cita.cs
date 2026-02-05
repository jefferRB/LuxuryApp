using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Calendar
{
    public class Cita
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string? NombreCliente { get; set; }

        [MaxLength(20)]
        public string? TelefonoCliente { get; set; }

        [MaxLength(100)]
        public string? Servicio { get; set; }

        [Required]
        public DateTime FechaHoraCita { get; set; }

        public ICollection<CitaBarbero> CitaBarberos { get; set; }
    }
}
