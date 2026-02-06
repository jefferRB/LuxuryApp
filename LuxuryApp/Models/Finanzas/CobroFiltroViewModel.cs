namespace LuxuryApp.Models.Finanzas
{
    public class CobroFiltroViewModel
    {
        public string VistaTiempo { get; set; } = "dia";
        // dia / semana / mes / año

        public int? BarberoId { get; set; }

        public string MetodoPago { get; set; }

        public DateTime? FechaInicio { get; set; }

        public DateTime? FechaFin { get; set; }
    }
}
