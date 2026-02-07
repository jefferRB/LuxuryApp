namespace LuxuryApp.Models.Productos
{
    public class ProductoIndexViewModel
    {
        public List<Producto> Productos { get; set; }

        public int TotalProductos { get; set; }

        public int ProductosBajoStock { get; set; }

        public decimal ValorInventario { get; set; }
    }
}
