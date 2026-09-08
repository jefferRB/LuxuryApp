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
        public decimal TotalPagadoFuncionariosAnalitico { get; set; }

        public decimal TotalEgresos { get; set; }
        public decimal TotalEgresosAnaliticos { get; set; }

        public decimal GananciaNegocio => TotalSinImpuestos - TotalEgresos;
        public decimal ResultadoAnalitico => TotalSinImpuestos - TotalEgresosAnaliticos;
        public decimal IngresosEfectivo { get; set; }
        public decimal IngresosSinpe { get; set; }
        public decimal IngresosTarjeta { get; set; }
        public List<decimal> GananciaPorMes { get; set; } = new();
        public List<decimal> ResultadoAnaliticoPorMes { get; set; } = new();

        /// <summary>
        /// Desglose del mes tal como lo devolvió el motor único de ganancia. Es la fuente de
        /// <see cref="TotalSinImpuestos"/>, <see cref="TotalImpuestos"/>,
        /// <see cref="TotalEgresosAnaliticos"/> y <see cref="ResultadoAnalitico"/>: si alguien
        /// necesita explicar de dónde salió un número del Dashboard, sale de acá.
        /// </summary>
        public Services.Finanzas.PeriodProfitBreakdown? Desglose { get; set; }

        /// <summary>
        /// Participación de los asociados sobre la ganancia del mes.
        ///
        /// <para>
        /// Es <c>null</c> cuando no hay ninguna participación vigente en el periodo. En ese caso
        /// el bloque no existe: no viaja al HTML ni al ViewModel, no se esconde con CSS. Lo
        /// construye <c>IAssociateProfitAllocationService</c> a partir del MISMO motor de ganancia
        /// que usan los estados de cuenta.
        /// </para>
        /// </summary>
        public Asociados.AssociateAllocationKpiViewModel? ParticipacionAsociados { get; set; }
    }
}
