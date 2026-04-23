namespace LuxuryApp.Models.DataBase
{
    public sealed class ClienteSummaryViewModel
    {
        public int Id { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public string NumeroTelefono { get; init; } = string.Empty;
        public string? CorreoElectronico { get; init; }
        public int FrecuenciaVisita { get; init; }
        public DateTime FechaUltimaVisita { get; init; }
        public DateTime? FechaCumpleanos { get; init; }

        public DateTime ProximaVisita => FechaUltimaVisita.AddDays(FrecuenciaVisita);
    }
}
