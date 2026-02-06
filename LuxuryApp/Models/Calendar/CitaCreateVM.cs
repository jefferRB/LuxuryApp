using System;
using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Calendar
{
    public class CitaCreateVM
    {
        public string? NombreCliente { get; set; }
        public string? TelefonoCliente { get; set; }
        public string? Servicio { get; set; }

        [Required]
        public DateTime FechaHoraCita { get; set; }
        public List<int> BarberoIds { get; set; } = new();
        public List<string> Servicios { get; set; }
    }
}
