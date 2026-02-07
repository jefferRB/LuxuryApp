using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Finanzas
{
    public class Categoria
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Nombre categoria")]
        public string? Nombre { get; set; }

        [Required]
        [Display(Name = "Detalle")]
        
        public string? Detalle { get; set; }

        public bool Activo { get; set; } = true;
    }
}
