using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LuxuryApp.Models.DataBase
{
    public class ClientesModel
    {
        [Key]
        public string NumeroTelefono { get; set; }
        public string CorreoElectronico { get; set; }
        public string Nombre { get; set; }
        public int FrecuenciaVisita { get; set; }
        public DateTime FechaUltimaVisita { get; set; }

        public DateTime? FechaCumpleaños { get; set; }
        public string? DescripcionServiciosRealizados { get; set; }

        [NotMapped]
        public DateTime ProximaVisita => FechaUltimaVisita.AddDays(FrecuenciaVisita);
    }
}
