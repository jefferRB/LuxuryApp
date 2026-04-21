namespace LuxuryApp.Models.Funcionarios
{
    public static class LiquidacionSemanalDefaults
    {
        public const string CategoriaPagoFuncionarios = "Pago Funcionarios";
        public const string EstadoPagada = "PAGADA";

        public static readonly IReadOnlyCollection<string> MetodosPagoPermitidos =
            new[] { "EFECTIVO", "SINPE", "TARJETA" };
    }
}
