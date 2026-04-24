namespace LuxuryApp.Models.Productos
{
    public class ProductoWriteRequest
    {
        public string NombreProducto { get; set; } = string.Empty;

        public string? DetalleProducto { get; set; }

        public decimal PrecioProducto { get; set; }

        public int CantidadProducto { get; set; }

        public int StockMinimo { get; set; }
    }
}
