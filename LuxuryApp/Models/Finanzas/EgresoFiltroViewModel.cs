namespace LuxuryApp.Models.Finanzas
{
    public class EgresoFiltroViewModel
    {
        public string VistaTiempo { get; set; } = "dia";
        // dia / semana / mes / año

        public string MetodoPago { get; set; }

        public string Categoria { get; set; }

        public DateTime? FechaInicio { get; set; }

        public DateTime? FechaFin { get; set; }
        public int? CategoriaId { get; set; }
    }
}
