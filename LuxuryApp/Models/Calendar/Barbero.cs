namespace LuxuryApp.Models.Calendar
{
    public class Barbero
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public bool Activo { get; set; } = true;

        public ICollection<CitaBarbero> CitaBarberos { get; set; }
    }
}
