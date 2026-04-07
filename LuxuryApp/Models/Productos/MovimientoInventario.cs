using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Productos
{
    public class MovimientoInventario : ITenantEntity
    {
        public Guid TenantId { get; set; }
        [Key]
        public int IdMovimiento { get; set; }

        public int ProductoId { get; set; }

        public DateTime FechaMovimiento { get; set; }

        public string TipoMovimiento { get; set; } // VENTA, AJUSTE, COMPRA

        public int Cantidad { get; set; }

        public int StockAnterior { get; set; }

        public int StockNuevo { get; set; }

        public string Observacion { get; set; }

        public Producto Producto { get; set; }
    }
}
