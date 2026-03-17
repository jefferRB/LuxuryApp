namespace LuxuryApp.Models.Informacion
{
    public class InformacionViewModel
    {
        public int MesSeleccionado { get; set; }
        public int AnioSeleccionado { get; set; }

        // KPIs

        public string MesMasCitas { get; set; }
        public int TotalMesMasCitas { get; set; }

        public string MesMenosCitas { get; set; }
        public int TotalMesMenosCitas { get; set; }

        public string DiaMasOcupado { get; set; }
        public int TotalDiaMasOcupado { get; set; }

        public string DiaMasLibre { get; set; }
        public int TotalDiaMasLibre { get; set; }

        public string HoraMasOcupada { get; set; }
        public double PromedioHoraMasOcupada { get; set; }

        public string HoraMasLibre { get; set; }
        public double PromedioHoraMasLibre { get; set; }

        public string ServicioMasSolicitado { get; set; }
        public int TotalServicioMasSolicitado { get; set; }

        public List<string> ServiciosNombres { get; set; } = new();
        public List<int> ServiciosCantidad { get; set; } = new();

        public string ProductoMasVendido { get; set; }
        public int TotalProductoMasVendido { get; set; }
        public string ProductoMenosVendido { get; set; }
        public int TotalProductoMenosVendido { get; set; }

        public string FuncionarioMasCitas { get; set; }
        public int TotalFuncionarioCitas { get; set; }

        

        // GRAFICOS

        public List<int> CitasPorMes { get; set; } = new();

        public List<string> SemanaDias { get; set; } = new();
        public List<int> SemanaCitas { get; set; } = new();

        public List<string> FuncionariosNombres { get; set; } = new();
        public List<int> FuncionariosCitas { get; set; } = new();

        // TOP CLIENTES

        public int TopCantidad { get; set; } = 10;
        public List<TopClienteVM> TopClientes { get; set; } = new();
    }

    public class TopClienteVM
    {
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public int TotalVisitas { get; set; }
    }
}