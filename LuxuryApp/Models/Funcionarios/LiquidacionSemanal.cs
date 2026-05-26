using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.Finanzas;

namespace LuxuryApp.Models.Funcionarios
{
    public class LiquidacionSemanal : ITenantEntity
    {
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }

        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime SemanaInicio { get; set; }

        [Required]
        public DateTime SemanaFin { get; set; }

        [Required]
        public DateTime FechaPago { get; set; }

        [Required]
        public decimal MontoTotal { get; set; }

        [Required]
        [MaxLength(30)]
        public string Estado { get; set; } = LiquidacionSemanalDefaults.EstadoPagada;

        [MaxLength(500)]
        public string? Observacion { get; set; }

        [MaxLength(450)]
        public string? CreadoPor { get; set; }

        [Required]
        public DateTime FechaCreacion { get; set; }

        public int? EgresoId { get; set; }
        public Egreso? Egreso { get; set; }

        public ICollection<LiquidacionSemanalDetalle> Detalles { get; set; } = new List<LiquidacionSemanalDetalle>();
        public ICollection<LiquidacionSemanalDistribucionMensual> DistribucionesMensuales { get; set; } = new List<LiquidacionSemanalDistribucionMensual>();
    }
}
