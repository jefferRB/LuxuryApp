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

        public decimal TotalServicios { get; set; }

        public decimal TotalProductos { get; set; }

        public decimal TotalGenerado { get; set; }

        public decimal TotalSinImpuestos { get; set; }

        public decimal TotalImpuestos { get; set; }

        public decimal TotalPagadoFuncionarios { get; set; }

        public decimal TotalEgresos { get; set; }

        public decimal GananciaNegocio => TotalSinImpuestos - TotalEgresos;
        public decimal IngresosEfectivo { get; set; }
        public decimal IngresosSinpe { get; set; }
        public decimal IngresosTarjeta { get; set; }
        public List<decimal> GananciaPorMes { get; set; } = new();
    }
}
