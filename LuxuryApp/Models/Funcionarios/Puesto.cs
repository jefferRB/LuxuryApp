using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Funcionarios
{
    public class Puesto
    {
        [Key]
        public int IdPuesto { get; set; }

        [Required]
        [MaxLength(100)]
        public string NombrePuesto { get; set; }

        [MaxLength(250)]
        public string? Detalle { get; set; }

        public bool Activo { get; set; } = true;

        // Relación
        public ICollection<Funcionario> Funcionarios { get; set; } = new List<Funcionario>();
    }
}
