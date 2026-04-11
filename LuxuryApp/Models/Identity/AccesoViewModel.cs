using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Identity
{
    public class AccesoViewModel
    {
        [Required(ErrorMessage = "The Email is required")]
        [EmailAddress]
        //todas las validaciones con buen manejo
        public string Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "The Password is required ")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")] // Used to choose the name to display
        public string Password { get; set; } = string.Empty;
        [Display(Name = "Recordar datos?")]
        public bool RememberMe { get; set; }
    }
}
