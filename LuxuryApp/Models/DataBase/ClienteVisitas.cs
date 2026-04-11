using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.DataBase
{
    public class ClienteVisitas : ITenantEntity
    {
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }
        public int Id { get; set; }

        public string NumeroTelefono { get; set; } = string.Empty;

        public DateTime FechaVisita { get; set; }
    }
}
