using System;
using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Calendar
{
    public class CitaCreateVM
    {
        [StringLength(100)]
        public string? NombreCliente { get; set; }

        [StringLength(20)]
        public string? TelefonoCliente { get; set; }

        public int? ServicioId { get; set; }

        [Required]
        public DateTime FechaHoraCita { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Debe seleccionar un funcionario válido.")]
        public int FuncionarioId { get; set; }

        [Required]
        [StringLength(20)]
        public string Tipo { get; set; } = "CITA";

        [Range(5, 180, ErrorMessage = "La duración del descanso debe estar entre 5 y 180 minutos.")]
        public int? DuracionMinutos { get; set; }

        public bool Duplicar { get; set; }

        public List<string> FechasDuplicadas { get; set; } = new();

    }
}
