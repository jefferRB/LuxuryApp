namespace LuxuryApp.Models.SaaS
{
    public class ResultadoCheckoutViewModel
    {
        public string? Referencia { get; set; }
        public string? CodigoProveedor { get; set; }
        public string? DescripcionProveedor { get; set; }
        public string? NombrePlan { get; set; }
        public EstadoPagoProveedor? EstadoPago { get; set; }
        public EstadoSuscripcion? EstadoSuscripcion { get; set; }
        public bool AccesoRestringido { get; set; }
        public bool PagoAprobadoPorProveedor { get; set; }
        public bool ConfirmadoPorWebhook { get; set; }
        public bool SuscripcionActiva { get; set; }
        public bool DebeAutoActualizar { get; set; }
        public int SegundosAutoActualizacion { get; set; }
        public string? UrlActualizacion { get; set; }
        public string MensajePrincipal { get; set; } = string.Empty;
        public string? MensajeSecundario { get; set; }
    }
}
