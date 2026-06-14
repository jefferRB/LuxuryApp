namespace LuxuryApp.Models.DataBase
{
    public sealed class BuscarClienteViewModel
    {
        public string? Criterio { get; set; }
        public string? Mensaje { get; set; }
        public bool EsBusquedaTelefonica { get; set; }
        public bool ResultadosLimitados { get; set; }
        public ClienteSummaryViewModel? ClienteSeleccionado { get; set; }
        public IReadOnlyList<ClienteSummaryViewModel> ClientesEncontrados { get; set; } = Array.Empty<ClienteSummaryViewModel>();
        public IReadOnlyList<CitaVisitaItemViewModel> HistorialVisitas { get; set; } = Array.Empty<CitaVisitaItemViewModel>();
        public int TotalVisitas { get; set; }
        public int TotalCitasHistorial { get; set; }
        public string? NotasServicio { get; set; }
        public IReadOnlyList<CobroClienteHistorialItemViewModel> HistorialPagos { get; set; } = Array.Empty<CobroClienteHistorialItemViewModel>();
    }
}
