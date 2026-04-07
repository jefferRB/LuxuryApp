using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Finanzas
{
    public class Egreso : ITenantEntity
    {
        public Guid TenantId { get; set; }
        [Key]
        public int IdEgreso { get; set; }

        [Required]
        [Display(Name = "Fecha")]
        public DateTime FechaEgreso { get; set; } = DateTime.Now;

        [Required]
        [Display(Name = "Detalle")]
        [StringLength(200)]
        public string Detalle { get; set; }

       

        [Required]
        [Display(Name = "Monto")]
        [Range(0, 999999)]
        public decimal Monto { get; set; }

        [Required]
        [Display(Name = "Método de Pago")]
        public string MetodoPago { get; set; }
        public int CategoriaId { get; set; }
        public Categoria? Categoria { get; set; }
    }
}
