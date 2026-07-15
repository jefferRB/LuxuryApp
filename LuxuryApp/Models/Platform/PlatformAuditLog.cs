namespace LuxuryApp.Models.Platform
{
    /// <summary>
    /// Registro append-only de acciones internas del SuperAdmin sobre la plataforma.
    /// No implementa <c>ITenantEntity</c> a propósito: es una bitácora cross-tenant que
    /// queda fuera del Row-Level Security. Nunca debe exponerse un endpoint de borrado.
    /// </summary>
    public class PlatformAuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>UserId del SuperAdmin que ejecutó la acción.</summary>
        public string ActorUserId { get; set; } = string.Empty;

        /// <summary>Snapshot del correo/usuario del actor al momento de la acción.</summary>
        public string ActorEmail { get; set; } = string.Empty;

        /// <summary>Acción ejecutada. Ver <see cref="PlatformAuditActions"/>.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Tipo de entidad afectada. Ver <see cref="PlatformAuditEntityTypes"/>.</summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>Id de la entidad afectada (string para soportar Guid o Id de Identity).</summary>
        public string? EntityId { get; set; }

        public Guid? TenantId { get; set; }

        /// <summary>Snapshot del nombre del tenant al momento de la acción.</summary>
        public string? TenantName { get; set; }

        public string? TargetUserId { get; set; }

        /// <summary>Snapshot del correo del usuario objetivo.</summary>
        public string? TargetUserEmail { get; set; }

        public string? BeforeJson { get; set; }

        public string? AfterJson { get; set; }

        public string? Reason { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Acciones auditables de la consola de plataforma.</summary>
    public static class PlatformAuditActions
    {
        public const string UserDeactivated = "UserDeactivated";
        public const string UserReactivated = "UserReactivated";
        public const string DangerousActionPasswordFailed = "DangerousActionPasswordFailed";
        public const string DangerousActionBlocked = "DangerousActionBlocked";
        public const string TenantCommercialAccessUpdated = "TenantCommercialAccessUpdated";
        public const string WhatsAppSettingsUpdated = "WhatsAppSettingsUpdated";
        public const string MetaDiagnosticExecuted = "MetaDiagnosticExecuted";

        /// <summary>MFA TOTP habilitado/deshabilitado/recuperado para una cuenta de plataforma.</summary>
        public const string MfaEnabled = "MfaEnabled";
        public const string MfaDisabled = "MfaDisabled";
        public const string MfaRecoveryCodeUsed = "MfaRecoveryCodeUsed";

        /// <summary>Activación/desactivación de un código promocional (S11).</summary>
        public const string PromotionalCodeToggled = "PromotionalCodeToggled";

        /// <summary>Captura manual del snapshot comercial mensual (AD-4).</summary>
        public const string CommercialSnapshotCaptured = "CommercialSnapshotCaptured";

        /// <summary>
        /// Alerta automática: un upgrade de plan se aplicó y quedó una suscripción recurrente
        /// anterior viva en el proveedor que debe cancelarse manualmente (TiloPay no tiene API).
        /// </summary>
        public const string PlanUpgradeRequiresProviderCancellation = "PlanUpgradeRequiresProviderCancellation";

        /// <summary>
        /// Alerta automática: un webhook de pago quedó en revisión manual o sin correlación.
        /// Puede haber dinero cobrado en el proveedor sin activar; revisar en
        /// Platform/RecurringCheckouts (conciliación interna).
        /// </summary>
        public const string PaymentWebhookRequiresManualReview = "PaymentWebhookRequiresManualReview";

        /// <summary>Cierre de un pase de reconciliación de Billing (resumen en AfterJson).</summary>
        public const string BillingReconciliationCompleted = "BillingReconciliationCompleted";

        /// <summary>Reparación automática segura aplicada por la reconciliación (ej. pago confirmado sin activación).</summary>
        public const string BillingAutoRepairApplied = "BillingAutoRepairApplied";

        /// <summary>Limpieza segura aplicada por la reconciliación (ej. Pendiente abandonado expirado).</summary>
        public const string BillingReconciliationCleanup = "BillingReconciliationCleanup";

        /// <summary>Hallazgo que requiere decisión humana; la reconciliación nunca toca datos ambiguos.</summary>
        public const string BillingReconciliationAlert = "BillingReconciliationAlert";

        // ── Resolución y gestión del suscriptor recurrente de TiloPay ──
        public const string ProviderSubscriberResolved = "ProviderSubscriberResolved";
        public const string ProviderSubscriberResolutionPending = "ProviderSubscriberResolutionPending";
        public const string ProviderSubscriberResolutionAmbiguous = "ProviderSubscriberResolutionAmbiguous";
        public const string ProviderSubscriberResolutionFailed = "ProviderSubscriberResolutionFailed";
        public const string RecurrentUrlGenerated = "RecurrentUrlGenerated";
        public const string CheckoutBlockedExistingProviderSubscriber = "CheckoutBlockedExistingProviderSubscriber";

        /// <summary>
        /// Checkout bloqueado porque no se pudo verificar el suscriptor en TiloPay (API caído/erróneo)
        /// Y existe señal local de suscripción previa: bloquear evita crear un suscriptor duplicado.
        /// </summary>
        public const string CheckoutBlockedProviderVerificationUnavailable = "CheckoutBlockedProviderVerificationUnavailable";
        public const string ProviderSubscriptionPaused = "ProviderSubscriptionPaused";
        public const string ProviderSubscriptionReactivated = "ProviderSubscriptionReactivated";
        public const string ProviderSubscriptionDeleted = "ProviderSubscriptionDeleted";
        public const string ProviderSubscriptionDeleteFailed = "ProviderSubscriptionDeleteFailed";
        public const string UpgradeOldProviderSubscriptionCancellationRequired = "UpgradeOldProviderSubscriptionCancellationRequired";
        public const string UpgradeOldProviderSubscriptionCancellationCompleted = "UpgradeOldProviderSubscriptionCancellationCompleted";
        public const string UpgradeOldProviderSubscriptionCancellationFailed = "UpgradeOldProviderSubscriptionCancellationFailed";

        // ── Cambio de plan base (estrategia B) ──
        /// <summary>Cambio de plan bloqueado: la suscripción activa no tiene id_suscriptor del proveedor, imposible cancelar el viejo con seguridad.</summary>
        public const string PlanChangeBlockedMissingCurrentProviderSubscription = "PlanChangeBlockedMissingCurrentProviderSubscription";

        /// <summary>Reintento del reconciliador para cancelar el suscriptor viejo de un cambio aplicado que quedó pendiente.</summary>
        public const string PlanChangeOldSubscriberCancellationRetried = "PlanChangeOldSubscriberCancellationRetried";

        /// <summary>Cambio de plan bloqueado: hay un cambio previo aplicado cuyo suscriptor viejo aún no se canceló (riesgo de múltiples rebajos).</summary>
        public const string PlanChangeBlockedPendingOldCancellation = "PlanChangeBlockedPendingOldCancellation";

        /// <summary>Cambio de plan bloqueado: el destino tiene menos cupo que los funcionarios activos.</summary>
        public const string PlanChangeBlockedDowngradeFuncionarioLimit = "PlanChangeBlockedDowngradeFuncionarioLimit";

        /// <summary>
        /// CRÍTICO: TiloPay respondió éxito a la baja del suscriptor viejo, pero la verificación
        /// posterior (getSuscriptorRepeat) lo muestra todavía Activo o no pudo confirmarse.
        /// El viejo podría seguir rebajando; queda pendiente y se reintenta con backoff.
        /// </summary>
        public const string PlanChangeOldSubscriberCancellationVerificationFailed = "PlanChangeOldSubscriberCancellationVerificationFailed";
    }

    public static class PlatformAuditEntityTypes
    {
        public const string User = "User";
        public const string Tenant = "Tenant";
        public const string Subscription = "Subscription";
        public const string Billing = "Billing";
        public const string PromotionalCode = "PromotionalCode";
        public const string CommercialSnapshot = "CommercialSnapshot";
    }
}
