using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.DataBase
{
    public class ClientesModel : ITenantEntity
    {
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }
        [Key]
        public string NumeroTelefono { get; set; } = string.Empty;
        public string CorreoElectronico { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int FrecuenciaVisita { get; set; }
        public DateTime FechaUltimaVisita { get; set; }

        public DateTime? FechaCumpleaños { get; set; }
        public string? DescripcionServiciosRealizados { get; set; }

        [NotMapped]
        public DateTime ProximaVisita => FechaUltimaVisita.AddDays(FrecuenciaVisita);
    }
}
