namespace LuxuryApp.Services.Billing
{
    /// <summary>Resultado de un pase de reconciliación. Se persiste serializado en PlatformAuditLog.</summary>
    public sealed record BillingReconciliationReport
    {
        public DateTime StartedUtc { get; init; }
        public DateTime FinishedUtc { get; set; }

        /// <summary>Pagos confirmados sin activación reparados automáticamente.</summary>
        public int OrphanPaymentsRepaired { get; set; }

        /// <summary>Pagos confirmados sin activación que NO era seguro reparar (alerta).</summary>
        public int OrphanPaymentsAlerted { get; set; }

        /// <summary>Suscripciones base activas con próximo cobro vencido (alerta, nunca se tocan).</summary>
        public int OverdueRenewalsAlerted { get; set; }

        /// <summary>Add-ons WhatsApp activos con próximo cobro vencido (alerta).</summary>
        public int OverdueAddonsAlerted { get; set; }

        /// <summary>Intentos Pendiente abandonados marcados Expirado (limpieza segura).</summary>
        public int StalePendingsExpired { get; set; }

        /// <summary>Intentos en ManualReview viejos sin resolver (alerta, nunca se tocan).</summary>
        public int StaleManualReviewsAlerted { get; set; }

        /// <summary>Eventos de pago atascados en Recibido/Error (alerta).</summary>
        public int StuckEventsAlerted { get; set; }

        /// <summary>Alertas omitidas por cooldown (ya alertadas recientemente).</summary>
        public int AlertsSuppressedByCooldown { get; set; }

        public double DurationMs => (FinishedUtc - StartedUtc).TotalMilliseconds;

        public bool HasFindings =>
            OrphanPaymentsRepaired > 0 ||
            OrphanPaymentsAlerted > 0 ||
            OverdueRenewalsAlerted > 0 ||
            OverdueAddonsAlerted > 0 ||
            StalePendingsExpired > 0 ||
            StaleManualReviewsAlerted > 0 ||
            StuckEventsAlerted > 0;
    }
}
