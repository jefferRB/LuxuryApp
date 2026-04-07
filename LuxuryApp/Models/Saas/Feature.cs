namespace LuxuryApp.Models.Saas
{
    public class Feature
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } // "REPORTES_AVANZADOS"
        public string Nombre { get; set; }

        public ICollection<PlanFeature> PlanFeatures { get; set; }
    }
}
