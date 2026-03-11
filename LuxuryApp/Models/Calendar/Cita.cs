using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;

namespace LuxuryApp.Models.Calendar
{
    public class Cita
    {
        public int Id { get; set; }

        [MaxLength(100)]
        public string? NombreCliente { get; set; }

        [MaxLength(20)]
        public string? TelefonoCliente { get; set; }

        public int? ServicioId { get; set; }
        public Servicio? Servicio { get; set; }

        [Required]
        public DateTime FechaHoraCita { get; set; }


        public bool ConfirmacionEnviada { get; set; }
        public bool Recordatorio24hEnviado { get; set; }
        public bool Recordatorio3hEnviado { get; set; }
        public bool VisitaProcesada { get; set; } = false;

        // 🔥 FK
        public int FuncionarioId { get; set; }

        // 🔥 Navigation property
        public Funcionario? Funcionario { get; set; }

        
    }
}
