using LuxuryApp.Models.SaaS;

namespace LuxuryApp.Models.Platform
{
    public sealed class PlatformTenantRowViewModel
    {
        // ── Fase 2: salud y métricas de uso ───────────────────────────────────
        public PlatformTenantHealthViewModel Health { get; init; } = new();
        public int Citas30d { get; init; }
        public int Cobros30d { get; init; }
        public int BookingRequests30d { get; init; }
        public int BookingRequestsPending { get; init; }
        public DateTime? LastActivityUtc { get; init; }
        public Guid TenantId { get; init; }
        public string TenantName { get; init; } = string.Empty;
        public bool TenantActive { get; init; }
        public TenantCommercialAccessMode CommercialAccessMode { get; init; }
        public Guid? ForcedPlanId { get; init; }
        public string? ForcedPlanName { get; init; }
        public string? OwnerEmail { get; init; }
        public string? CommercialNotes { get; init; }
        public bool CanAccessApp { get; init; }
        public string? EffectivePlanName { get; init; }
        public string Reason { get; init; } = string.Empty;
        public bool WhatsAppEnabled { get; init; }
        public bool WhatsAppAddonActive { get; init; }
        public bool SendWhatsAppConfirmationOnCreate { get; init; }
        public bool SendWhatsAppReminderThreeHoursBefore { get; init; }
        public int WhatsAppDailyMessageLimit { get; init; }
        public int WhatsAppTodayUsage { get; init; }
        public string WhatsAppTimeZoneId { get; init; } = string.Empty;
        public string? WhatsAppNotes { get; init; }
        public string? WhatsAppLastErrorCode { get; init; }
        public string? WhatsAppLastErrorMessage { get; init; }
        public DateTime? WhatsAppLastErrorAtUtc { get; init; }
        public string? WhatsAppAddonCode { get; init; }
        public bool WhatsAppAddonIsManual { get; init; }
        public DateTime? WhatsAppAddonFechaFin { get; init; }
        public int? WhatsAppAddonMonthlyLimit { get; init; }

        /// <summary>Origen del add-on (ProviderRecurring / ManualGrant / Legacy) para el modal de plataforma.</summary>
        public LuxuryApp.Models.SaaS.WhatsAppAddonBillingSource WhatsAppAddonSource { get; init; }
        public bool WhatsAppAddonManualIndefinite { get; init; }
        public DateTime? WhatsAppAddonManualExpiresAtUtc { get; init; }
        public bool WhatsAppAddonManualExpired { get; init; }
        public bool WhatsAppAddonProviderRisk { get; init; }

        /// <summary>Existe una fila de add-on TiloPay ACTIVA (para exigir confirmación de override manual).</summary>
        public bool WhatsAppHasActiveProviderAddon { get; init; }
    }
}
