using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Productos;

namespace LuxuryApp.Models.Finanzas
{
    public class Cobro : ITenantEntity
    {
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }
        [Key]
        public int IdCobro { get; set; }

        [Required]
        [Display(Name = "Fecha")]
        public DateTime FechaCobro { get; set; }

        [Required]
        [Display(Name = "Nombre Cliente")]
        public string NombreCliente { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Funcionario")]
        public int FuncionarioId { get; set; }

        [Display(Name = "Servicio")]
        public int? ServicioId { get; set; }

        [Required]
        [Display(Name = "Monto")]
        [DecimalRange(0.01, 999999, ErrorMessage = "Debe indicar un monto mayor a cero y dentro del rango permitido.")]
        public decimal Monto { get; set; }

        [Required]
        [Display(Name = "Método de Pago")]
        public string MetodoPago { get; set; } = string.Empty;

        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }

        // 🔗 Navegación EF
        public Funcionario? Funcionario { get; set; }
        public Servicio? Servicio { get; set; }
        public int? ProductoId { get; set; }
        public Producto? Producto { get; set; }
        
        public ICollection<DetalleCobroProducto> ProductosVendidos { get; set; } = new List<DetalleCobroProducto>();

    }
}
