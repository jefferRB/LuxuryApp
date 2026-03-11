namespace LuxuryApp.Models.Informacion
{
    public class InformacionViewModel
    {
        public string MesMasCitas { get; set; }
        public int TotalMesMasCitas { get; set; }

        public string MesMenosCitas { get; set; }
        public int TotalMesMenosCitas { get; set; }

        public string DiaMasOcupado { get; set; }
        public int TotalDiaMasOcupado { get; set; }

        public string HoraMasOcupada { get; set; }
        public int TotalHoraMasOcupada { get; set; }

        public string ServicioMasSolicitado { get; set; }
        public int TotalServicioMasSolicitado { get; set; }

        public string FuncionarioMasCitas { get; set; }
        public int TotalFuncionarioCitas { get; set; }

        public List<TopClienteVM> TopClientes { get; set; }

        public List<string> BarberosNombres { get; set; } = new();
        public List<int> BarberosCitas { get; set; } = new();

        public List<string> ServiciosNombres { get; set; } = new();
        public List<int> ServiciosCantidad { get; set; } = new();

    }

    public class TopClienteVM
    {
        public string Nombre { get; set; }
        public string Telefono { get; set; }
        public int TotalVisitas { get; set; }
    }
}