using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Identity
{
    public class RegistroViewModel
    {
        [Required(ErrorMessage = "The Email is required")]
        [EmailAddress]
        //todas las validaciones con buen manejo
        public string Email { get; set; }
        [Required(ErrorMessage = "The Password is required ")]
        [StringLength(50, ErrorMessage = "The {0} must be at least {2} characters long", MinimumLength = 5)]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña")] // Used to choose the name to display
        public string Password { get; set; }

        //todas las validaciones con buen manejo
        [Required(ErrorMessage = "The Password Confirmation is required ")]
        [Compare("Password", ErrorMessage = " Password and Password Confirmation are not equals")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar Contraseña")] // Used to choose the name to display
        public string ConfirmPassword { get; set; }

        [Required(ErrorMessage = "The Name is required  ")]
        [Display(Name = "Nombre")]
        public string Name { get; set; }
        [Display(Name = "Telefono")]
        public string PhoneNumber { get; set; }
        public bool State { get; set; }
    }
}
