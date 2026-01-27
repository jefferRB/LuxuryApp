namespace LuxuryApp.Models.DataBase
{
    public class ClienteVisitas
    {
        public int Id { get; set; }

        public string NumeroTelefono { get; set; } = string.Empty;

        public DateTime FechaVisita { get; set; }
    }
}
