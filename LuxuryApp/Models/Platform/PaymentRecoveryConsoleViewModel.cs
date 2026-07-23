using LuxuryApp.Services.Billing;

namespace LuxuryApp.Models.Platform
{
    /// <summary>
    /// Consola de recuperación de pago (SuperAdmin): incidentes vivos y acciones de soporte. La
    /// <see cref="GeneratedUpdateUrl"/> se muestra SOLO en la respuesta que la generó (nunca se
    /// persiste ni se guarda en TempData/cookie) para no filtrar la recurrentUrl.
    /// </summary>
    public sealed class PaymentRecoveryConsoleViewModel
    {
        public IReadOnlyList<PaymentRecoveryConsoleItem> Incidents { get; init; } =
            Array.Empty<PaymentRecoveryConsoleItem>();

        /// <summary>La integración admin de TiloPay está activa (habilita "Generar enlace").</summary>
        public bool AdminEnabled { get; init; }

        /// <summary>recurrentUrl recién generada para copiar/enviar. Transitoria: solo en esta vista.</summary>
        public string? GeneratedUpdateUrl { get; init; }

        public string? GeneratedUrlTenantName { get; init; }

        /// <summary>El enlace se generó con contrato fallback (id_plan+aliases): puede fallar en TiloPay.</summary>
        public bool GeneratedUrlUsedFallback { get; init; }
    }
}
