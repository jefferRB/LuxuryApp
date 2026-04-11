using System;
using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Calendar
{
    public class CitaCreateVM
    {
        public string? NombreCliente { get; set; }
        public string? TelefonoCliente { get; set; }
        public int? ServicioId { get; set; }

        [Required]
        public DateTime FechaHoraCita { get; set; }
        public int FuncionarioId { get; set; }
        public string Tipo { get; set; } = "CITA";
        public int? DuracionMinutos { get; set; }

        public bool Duplicar { get; set; }

        public List<string> FechasDuplicadas { get; set; } = new();

    }
}
