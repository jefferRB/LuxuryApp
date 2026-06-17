namespace LuxuryApp.Models.Funcionarios
{
    /// <summary>Funcionario validado para el portal (resuelto desde el claim).</summary>
    public sealed class PortalFuncionario
    {
        public int IdFuncionario { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public bool Activo { get; init; }
        public string ColorCalendario { get; init; } = string.Empty;
    }

    /// <summary>Una cita del funcionario para el portal (solo lectura).</summary>
    public sealed class PortalCitaItem
    {
        public int Id { get; init; }
        public DateTime FechaHora { get; init; }
        public string Cliente { get; init; } = string.Empty;
        public string Servicio { get; init; } = string.Empty;
        public string Tipo { get; init; } = "CITA";
        public string? Telefono { get; init; }
        public int? DuracionMinutos { get; init; }

        public int? ServicioId { get; init; }
        public decimal? PrecioServicio { get; init; }
        public bool YaCobrada { get; init; }

        public string Hora => FechaHora.ToString("hh:mm tt");

        /// <summary>True si esta cita puede generar un cobro (servicio de catálogo y aún sin cobro).</summary>
        public bool EsCobrable =>
            string.Equals(Tipo, "CITA", System.StringComparison.OrdinalIgnoreCase) &&
            ServicioId.HasValue &&
            !YaCobrada;
    }

    /// <summary>Resumen de producción y comisión estimada para un periodo.</summary>
    public sealed class PortalResumenProduccion
    {
        public decimal ProduccionServicios { get; init; }
        public decimal ComisionServicios { get; init; }
        public decimal ProduccionProductos { get; init; }
        public decimal ComisionProductos { get; init; }

        /// <summary>Comisión total estimada (servicios + productos).</summary>
        public decimal TotalEstimado { get; init; }

        public decimal ProduccionTotal => ProduccionServicios + ProduccionProductos;
        public bool TieneProductos => ProduccionProductos > 0 || ComisionProductos > 0;
    }

    public sealed class MiPanelViewModel
    {
        public string Nombre { get; init; } = string.Empty;
        public DateTime Hoy { get; init; }

        public IReadOnlyList<PortalCitaItem> CitasHoy { get; init; } = Array.Empty<PortalCitaItem>();
        public IReadOnlyList<PortalCitaItem> ProximasCitas { get; init; } = Array.Empty<PortalCitaItem>();

        public PortalResumenProduccion ResumenHoy { get; init; } = new();

        // Semana
        public decimal ProduccionSemana { get; init; }
        public decimal ComisionSemana { get; init; }
        public decimal PagadoSemana { get; init; }
        public decimal PendienteSemana { get; init; }
        public DateTime InicioSemana { get; init; }
        public DateTime FinSemana { get; init; }

        // Permisos relevantes para la vista
        public bool PuedeRegistrarCobros { get; init; }
    }

    public sealed class MisGananciasViewModel
    {
        public string Nombre { get; init; } = string.Empty;

        public PortalResumenProduccion Hoy { get; init; } = new();
        public PortalResumenProduccion Semana { get; init; } = new();
        public PortalResumenProduccion Mes { get; init; } = new();

        public DateTime InicioSemana { get; init; }
        public DateTime FinSemana { get; init; }
        public DateTime InicioMes { get; init; }

        public decimal PagadoSemana { get; init; }
        public decimal PendienteSemana { get; init; }

        /// <summary>Producción diaria de la semana (para mini desglose).</summary>
        public IReadOnlyList<DetalleDiaVM> DetalleDiasSemana { get; init; } = Array.Empty<DetalleDiaVM>();
    }

    public sealed class PortalPagoItem
    {
        public DateTime FechaPago { get; init; }
        public decimal Monto { get; init; }
        public string? MetodoPago { get; init; }
        public DateTime InicioSemana { get; init; }
        public DateTime FinSemana { get; init; }
        public string? Observacion { get; init; }
    }

    public sealed class MisPagosViewModel
    {
        public string Nombre { get; init; } = string.Empty;
        public IReadOnlyList<PortalPagoItem> Pagos { get; init; } = Array.Empty<PortalPagoItem>();
        public decimal TotalPagadoHistorico { get; init; }

        public int Pagina { get; init; }
        public int TotalPaginas { get; init; }
        public bool HayMas => Pagina < TotalPaginas;
    }

    public sealed class MiCalendarioViewModel
    {
        public string Nombre { get; init; } = string.Empty;
        public string ColorCalendario { get; init; } = string.Empty;
        public DateTime Fecha { get; init; }
        public DateTime FechaAnterior => Fecha.AddDays(-1);
        public DateTime FechaSiguiente => Fecha.AddDays(1);
        public bool EsHoy { get; init; }

        public IReadOnlyList<PortalCitaItem> Citas { get; init; } = Array.Empty<PortalCitaItem>();

        // Permisos y datos para acciones
        public bool PuedeCrearCitas { get; init; }
        public bool PuedeRegistrarCobros { get; init; }
        public IReadOnlyList<LuxuryApp.Models.Calendar.CalendarServiceOptionResponse> Servicios { get; init; }
            = Array.Empty<LuxuryApp.Models.Calendar.CalendarServiceOptionResponse>();
    }

    public sealed class PortalCobroItem
    {
        public int IdCobro { get; init; }
        public DateTime FechaCobro { get; init; }
        public string Cliente { get; init; } = string.Empty;
        public string Servicio { get; init; } = string.Empty;
        public decimal Monto { get; init; }
        public string MetodoPago { get; init; } = string.Empty;
        public bool DesdeCita { get; init; }
    }

    public sealed class MisCobrosViewModel
    {
        public string Nombre { get; init; } = string.Empty;
        public bool PuedeRegistrarCobros { get; init; }

        public decimal TotalHoy { get; init; }
        public decimal TotalSemana { get; init; }

        public IReadOnlyList<PortalCobroItem> Cobros { get; init; } = Array.Empty<PortalCobroItem>();

        public int Pagina { get; init; }
        public int TotalPaginas { get; init; }
        public bool HayMas => Pagina < TotalPaginas;
    }
}
