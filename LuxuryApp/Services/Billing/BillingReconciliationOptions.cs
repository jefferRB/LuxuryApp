namespace LuxuryApp.Services.Billing
{
    /// <summary>
    /// Configuración del pase diario de reconciliación de Billing. Los defaults son seguros
    /// para producción: detección y alertas siempre, reparación automática solo para casos
    /// determinísticos (pago confirmado sin activación), nunca sobre datos ambiguos.
    /// </summary>
    public sealed class BillingReconciliationOptions
    {
        /// <summary>Apaga por completo el pase (el worker queda registrado pero inerte).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Permite la reparación automática de pagos confirmados sin activación.
        /// En false, esos casos generan alerta en vez de repararse.
        /// </summary>
        public bool AutoRepairEnabled { get; set; } = true;

        /// <summary>Frecuencia del pase. 24 = diario.</summary>
        public int IntervalHours { get; set; } = 24;

        /// <summary>Espera inicial tras el arranque de la app antes del primer pase.</summary>
        public int InitialDelayMinutes { get; set; } = 3;

        /// <summary>Ventana hacia atrás para buscar pagos confirmados sin activación.</summary>
        public int ConfirmedPaymentLookbackDays { get; set; } = 14;

        /// <summary>
        /// Tolerancia sobre FechaProximoCobroUtc antes de alertar renovación vencida
        /// (TiloPay puede cobrar con horas de diferencia; 36h evita falsos positivos).
        /// </summary>
        public int OverdueRenewalToleranceHours { get; set; } = 36;

        /// <summary>Días tras los cuales un intento Pendiente sin webhook se considera abandonado.</summary>
        public int StalePendingDays { get; set; } = 7;

        /// <summary>Horas tras las cuales un ManualReview sin resolver genera alerta (nunca se toca).</summary>
        public int StaleManualReviewHours { get; set; } = 24;

        /// <summary>Minutos tras los cuales un EventoPago Recibido/Error se considera atascado.</summary>
        public int StuckEventMinutes { get; set; } = 60;

        /// <summary>Horas de silencio antes de repetir la MISMA alerta sobre la misma entidad.</summary>
        public int AlertCooldownHours { get; set; } = 20;

        /// <summary>
        /// Habilita el worker de ALTA frecuencia que reintenta la cancelación del suscriptor viejo
        /// tras un cambio de plan. El riesgo de doble cobro no debe esperar al pase diario.
        /// </summary>
        public bool OldCancellationRetryEnabled { get; set; } = true;

        /// <summary>Intervalo (minutos) del worker de reintento de cancelación vieja. Clamp [5, 720].</summary>
        public int OldCancellationRetryMinutes { get; set; } = 20;
    }
}
