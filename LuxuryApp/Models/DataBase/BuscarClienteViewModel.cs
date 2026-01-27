namespace LuxuryApp.Models.DataBase
{
    public class BuscarClienteViewModel
    {
        public ClientesModel? ClienteSeleccionado { get; set; }
        public List<ClientesModel> ClientesEncontrados { get; set; } = new();
        public int TotalVisitas { get; set; }
    }
}
