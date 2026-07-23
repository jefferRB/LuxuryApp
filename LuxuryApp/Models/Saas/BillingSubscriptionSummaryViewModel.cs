namespace LuxuryApp.Models.SaaS
{
    public sealed class BillingSubscriptionSummaryViewModel
    {
        public string? PlanName { get; init; }
        public string? PlanCode { get; init; }
        public EstadoSuscripcion? Status { get; init; }
        public string StatusLabel { get; init; } = "Sin suscripcion";
        public string StatusTone { get; init; } = "secondary";
        public bool CanAccessApp { get; init; }
        public bool IsInGracePeriod { get; init; }

        // ── Ciclo de vida: cancelación de renovación (cancel-at-period-end) ──
        /// <summary>La renovación ya fue cancelada: el acceso sigue hasta la fecha efectiva, sin nuevos cobros.</summary>
        public bool CancelAtPeriodEnd { get; init; }

        /// <summary>Suscripción recurrente de TiloPay con id_suscriptor: condición para poder cancelar la renovación en línea.</summary>
        public bool IsRecurringTilopay { get; init; }

        /// <summary>Snapshot crudo del status del proveedor (último sincronizado), para diagnóstico.</summary>
        public string? ProviderStatusRaw { get; init; }

        /// <summary>
        /// La renovación está PAUSADA en el proveedor (status 3 / "Pause By Commerce"). El acceso
        /// puede seguir vigente hasta la fecha efectiva, pero no habrá nuevos cobros mientras dure la
        /// pausa. La pausa es una acción de soporte/plataforma: el cliente no la revierte en línea.
        /// Se calcula en el servicio con ProviderSubscriberStatusRules (fuente única).
        /// </summary>
        public bool IsRenewalPaused { get; init; }

        /// <summary>
        /// True si el cliente puede cancelar la renovación ahora: recurrente TiloPay, con acceso
        /// vigente (Activa/Morosa), sin una cancelación ya pedida y sin una pausa activa (una
        /// suscripción pausada no se cancela en línea: mezcla estados; se reactiva desde soporte).
        /// </summary>
        public bool CanRequestCancellation =>
            IsRecurringTilopay &&
            !CancelAtPeriodEnd &&
            !IsRenewalPaused &&
            (Status == EstadoSuscripcion.Activa || Status == EstadoSuscripcion.Morosa);

        /// <summary>
        /// Caso B: la renovación está cancelada pero el período AÚN está vigente (estado efectivo
        /// Activa/Morosa). Solo entonces el cliente puede reactivar la renovación del mismo suscriptor.
        /// Si ya venció, el estado efectivo es Suspendida y esto es false (Caso C: suscribirse de nuevo).
        /// </summary>
        public bool CanReactivateRenewal =>
            IsRecurringTilopay &&
            CancelAtPeriodEnd &&
            (Status == EstadoSuscripcion.Activa || Status == EstadoSuscripcion.Morosa);

        // ── Recuperación de pago (Fase 3): pago fallido → gracia → gracia vencida → suspensión ──
        // Se deriva de Suscripcion.PaymentRecoveryStatus, que ya mantiene el backend
        // (RegisterFailedPayment / ResolveOnSuccess / RunGraceExpirationPass). La UI NO se mezcla con
        // pausa/cancelación: si hay pausa o renovación cancelada vigente, esos estados mandan.

        /// <summary>Estado de recuperación tal cual lo guardó el backend: "GraceActive"/"GraceExpired"/"Suspended"/null.</summary>
        public string? PaymentRecoveryStatus { get; init; }

        /// <summary>
        /// La ventana de gracia (FechaFinGraciaUtc) ya venció por reloj, aunque el worker todavía no
        /// haya marcado GraceExpired. Fail-safe de la UI: no mostrar "en gracia" con la fecha vencida.
        /// Lo calcula el servicio contra la hora actual.
        /// </summary>
        public bool PaymentGraceWindowEnded { get; init; }

        private bool RecoveryTakesPrecedence => !IsRenewalPaused && !CancelAtPeriodEnd;
        private bool IsGraceActiveStatus => string.Equals(PaymentRecoveryStatus, "GraceActive", StringComparison.OrdinalIgnoreCase);
        private bool IsGraceExpiredStatus => string.Equals(PaymentRecoveryStatus, "GraceExpired", StringComparison.OrdinalIgnoreCase);

        /// <summary>Pago fallido con gracia todavía vigente: acceso mantenido, aún sin cortar.</summary>
        public bool PaymentInGrace =>
            RecoveryTakesPrecedence && IsGraceActiveStatus && !PaymentGraceWindowEnded;

        /// <summary>
        /// Gracia vencida SIN suspensión (acceso conservado, aviso fuerte). Incluye el fail-safe:
        /// si el backend aún dice "GraceActive" pero la fecha ya venció, la UI trata como vencida.
        /// </summary>
        public bool PaymentGraceExpired =>
            RecoveryTakesPrecedence &&
            (IsGraceExpiredStatus || (IsGraceActiveStatus && PaymentGraceWindowEnded));

        /// <summary>Cuenta suspendida por impago (AutoSuspendAfterGrace=true y el worker suspendió).</summary>
        public bool PaymentSuspended =>
            string.Equals(PaymentRecoveryStatus, "Suspended", StringComparison.OrdinalIgnoreCase);

        /// <summary>Hay algún estado de recuperación con banner accionable (no mezcla con pausa/cancelación).</summary>
        public bool HasPaymentRecoveryBanner => PaymentInGrace || PaymentGraceExpired || PaymentSuspended;

        /// <summary>
        /// Etiqueta de estado a mostrar cuando manda la recuperación de pago (reemplaza el genérico
        /// "En gracia" de Morosa). Null cuando no hay estado de recuperación (se usa StatusLabel).
        /// GraceExpired NUNCA dice "En gracia": la gracia ya venció.
        /// </summary>
        public string? PaymentStateBadgeLabel =>
            PaymentInGrace ? "En período de gracia"
            : PaymentGraceExpired ? "Pago pendiente"
            : PaymentSuspended ? "Suspendida por pago pendiente"
            : null;

        /// <summary>
        /// Se puede ofrecer "Actualizar método de pago": suscripción recurrente TiloPay con acceso
        /// (Activa/Morosa) o suspendida por impago (para reactivar), sin renovación cancelada ni pausa.
        /// El servicio valida además dominio/estado y, si no aplica, responde con un mensaje seguro.
        /// </summary>
        public bool CanUpdatePaymentMethod =>
            IsRecurringTilopay &&
            !CancelAtPeriodEnd &&
            !IsRenewalPaused &&
            (Status == EstadoSuscripcion.Activa ||
             Status == EstadoSuscripcion.Morosa ||
             Status == EstadoSuscripcion.Suspendida);

        // Fechas de CÁLCULO (UTC, efectivas = max(local, proveedor)): úsalas para lógica, no para mostrar.
        public DateTime? CurrentPeriodEndUtc { get; init; }
        public DateTime? NextBillingDateUtc { get; init; }
        public DateTime? GracePeriodEndsUtc { get; init; }

        // Fechas de DISPLAY (dd/MM/yyyy, fecha de calendario en Costa Rica). La UI muestra ESTAS:
        // cuando la fecha efectiva viene del proveedor, reflejan su expire exacto (p.ej. 15/09/2026),
        // no el UTC crudo de fin de día (16/09/2026).
        public string? CurrentPeriodEndDisplay { get; init; }
        public string? NextBillingDateDisplay { get; init; }
        public string? GracePeriodEndsDisplay { get; init; }
        public int? MaxFuncionarios { get; init; }
        public int ActiveFuncionarios { get; init; }
        public string? WhatsAppAddonName { get; init; }
        public string? WhatsAppAddonCode { get; init; }
        public EstadoSuscripcion? WhatsAppAddonStatus { get; init; }
        public string? WhatsAppAddonStatusLabel { get; init; }
        public int? WhatsAppMonthlyLimit { get; init; }
        public int WhatsAppMessagesUsed { get; init; }
        public int? WhatsAppMessagesRemaining { get; init; }
        public int WhatsAppTodayUsage { get; init; }
        public int? WhatsAppDailyLimit { get; init; }
        public bool WhatsAppAutomationEnabled { get; init; }
        public bool SendAppointmentConfirmations { get; init; }
        public bool SendAppointmentReminders { get; init; }

        // Programación configurable (Fase 1).
        public int ConfirmationHoursBefore { get; init; } = 24;
        public bool SendConfirmationImmediatelyIfInsideWindow { get; init; } = true;
        public int ReminderHoursBefore { get; init; } = 3;
        public bool SendReminderImmediatelyIfInsideWindow { get; init; } = true;

        // Lote diario a hora fija (Fase 2).
        public bool ConfirmationIsBatch { get; init; }
        public TimeOnly? ConfirmationBatchTime { get; init; }
        public string ConfirmationBatchTarget { get; init; } = "TomorrowAllDay";
        public TimeOnly? ConfirmationMorningStart { get; init; }
        public TimeOnly? ConfirmationMorningEnd { get; init; }
        public bool ReminderIsBatch { get; init; }
        public TimeOnly? ReminderBatchTime { get; init; }
        public string ReminderBatchTarget { get; init; } = "SameDayRemaining";

        // Horas de silencio (Fase 3).
        public bool QuietHoursEnabled { get; init; }
        public TimeOnly? QuietHoursStart { get; init; }
        public TimeOnly? QuietHoursEnd { get; init; }

        public DateTime? WhatsAppNextBillingDateUtc { get; init; }

        /// <summary>Próximo cobro del ADD-ON (ciclo INDEPENDIENTE del plan base), fecha de calendario Tica.</summary>
        public string? WhatsAppNextBillingDateDisplay { get; init; }

        /// <summary>El add-on es recurrente (tiene suscriptor TiloPay): condición para cancelar/cambiar en línea.</summary>
        public bool WhatsAppAddonIsRecurring { get; init; }

        /// <summary>La renovación del add-on ya fue cancelada: sigue activo hasta la fecha efectiva, sin nuevos cobros.</summary>
        public bool WhatsAppAddonCancelAtPeriodEnd { get; init; }

        /// <summary>Fecha hasta la que el add-on sigue vigente cuando su renovación fue cancelada (display Tico).</summary>
        public string? WhatsAppAddonEndsDisplay { get; init; }

        public bool HasWhatsAppAddon => !string.IsNullOrWhiteSpace(WhatsAppAddonCode);

        /// <summary>
        /// El cliente puede cancelar la renovación del add-on ahora: add-on recurrente activo/moroso,
        /// sin una cancelación ya pedida. Independiente del ciclo del plan base.
        /// </summary>
        public bool CanCancelWhatsAppAddon =>
            HasWhatsAppAddon &&
            WhatsAppAddonIsRecurring &&
            !WhatsAppAddonCancelAtPeriodEnd &&
            (WhatsAppAddonStatus == EstadoSuscripcion.Activa ||
             WhatsAppAddonStatus == EstadoSuscripcion.Morosa ||
             WhatsAppAddonStatus == EstadoSuscripcion.Trial);
    }
}
