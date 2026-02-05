namespace LuxuryApp.Models.Calendar
{
    public class CitaBarbero
    {
        public int Id { get; set; }

        public int CitaId { get; set; }
        public Cita Cita { get; set; }

        public int BarberoId { get; set; }
        public Barbero Barbero { get; set; }

    }
}
