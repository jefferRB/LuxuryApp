using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using LuxuryApp.Models.Productos;

namespace LuxuryApp.Models.Finanzas
{
    public class DetalleCobroProducto
    {
        [Key]
        
        public int IdDetalle { get; set; }

        public int CobroId { get; set; }

        public int ProductoId { get; set; }

        public int Cantidad { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal PrecioUnitario { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Subtotal { get; set; }

        // NAV
        public Cobro Cobro { get; set; }

        public Producto Producto { get; set; }
    }
}
