namespace LuxuryApp.Models.DataBase
{
    public class ServicioRealizadoViewModel
    {
        public string NumeroTelefono { get; set; } = null!;

        public List<IFormFile> Imagenes { get; set; } = new List<IFormFile>();
        public List<ClienteImagenesModel> ImagenesGuardadas { get; set; } = new();

        public string? DescripcionServicios { get; set; } // NUEVO
        public DateTime Fecha { get; set; } = DateTime.Now;
    }
}
