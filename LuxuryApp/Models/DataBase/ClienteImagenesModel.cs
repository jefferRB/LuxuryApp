namespace LuxuryApp.Models.DataBase
{
    public class ClienteImagenesModel
    {
        public int Id { get; set; }

        public string NumeroTelefono { get; set; } = null!;

        public byte[] Imagen { get; set; } = null!;

        public string? Descripcion { get; set; }

        public DateTime Fecha { get; set; }
    }
}
