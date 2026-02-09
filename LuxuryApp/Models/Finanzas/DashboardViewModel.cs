namespace LuxuryApp.Models.Finanzas
{
    public class DashboardViewModel
    {
        public decimal TotalIngresosMes { get; set; }

        public decimal TotalEgresosMes { get; set; }

        public decimal GananciaNeta => TotalIngresosMes - TotalEgresosMes;

        public int CantidadClientes { get; set; }

        public int CantidadCitasMes { get; set; }
        public decimal ValorInventarioProductos { get; set; }
        public int TotalProductosInventario { get; set; }

        public int MesSeleccionado { get; set; }
        public int AnioSeleccionado { get; set; }

    }
}
