using LuxuryApp.Models.Common;
using LuxuryApp.Models.SaaS;
using Microsoft.AspNetCore.Identity;

namespace LuxuryApp.Models.Identity
{
    public class AppUsuario : IdentityUser
    {
        public string? Name { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public bool State { get; set; } = true;

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}
