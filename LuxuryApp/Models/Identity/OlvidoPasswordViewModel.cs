using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.Identity
{
    public class OlvidoPasswordViewModel
    {
        [Required(ErrorMessage = "The Email is required")]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
