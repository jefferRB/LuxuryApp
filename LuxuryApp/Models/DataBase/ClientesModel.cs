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
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string NumeroTelefono { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(256)]
        public string? CorreoElectronico { get; set; }

        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "La frecuencia de visita debe ser mayor a cero.")]
        public int FrecuenciaVisita { get; set; } 

        public DateTime FechaUltimaVisita { get; set; }

        public DateTime? FechaCumpleaños { get; set; }
        public string? DescripcionServiciosRealizados { get; set; }

        public ICollection<ClienteVisitas> Visitas { get; set; } = new List<ClienteVisitas>();

        [NotMapped]
        public DateTime ProximaVisita => FechaUltimaVisita.AddDays(FrecuenciaVisita);
    }
}
