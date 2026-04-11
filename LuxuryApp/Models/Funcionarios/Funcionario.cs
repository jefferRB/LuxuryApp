using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Funcionarios
{
    public class Funcionario : ITenantEntity
    {
    
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }
        [Key]
        public int IdFuncionario { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Telefono { get; set; }

        [Required]
        public int IdPuesto { get; set; }

        public Puesto? Puesto { get; set; }

        [Required]
        public string ColorCalendario { get; set; } = string.Empty;

        [Range(0, 100)]
        public decimal PorcentajeGanancia { get; set; }
        public decimal PorcentajeProducto { get; set; }

        public DateTime FechaIngreso { get; set; }

        public bool Activo { get; set; }
    }

}
