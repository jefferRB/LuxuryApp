using Microsoft.AspNetCore.Identity;

namespace LuxuryApp.Models.Identity
{
    public class AppUsuario : IdentityUser
    {
        public string? Name { get; set; }
        public string? PhoneNumber { get; set; }
        public bool State { get; set; }
    }
}
