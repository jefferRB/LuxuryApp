namespace LuxuryApp.Models.SaaS
{
    public class ResultadoCheckoutViewModel
    {
        public string? Referencia { get; set; }
        public string? CodigoProveedor { get; set; }
        public string? DescripcionProveedor { get; set; }
        public string? NombrePlan { get; set; }
        public EstadoPagoProveedor? EstadoPago { get; set; }
        public bool ConfirmadoPorWebhook => EstadoPago == EstadoPagoProveedor.Confirmado;
        public string MensajePrincipal { get; set; } = string.Empty;
    }
}
