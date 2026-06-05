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
        public DateTime? CurrentPeriodEndUtc { get; init; }
        public DateTime? NextBillingDateUtc { get; init; }
        public DateTime? GracePeriodEndsUtc { get; init; }
        public int? MaxFuncionarios { get; init; }
        public int ActiveFuncionarios { get; init; }
        public string? WhatsAppAddonName { get; init; }
        public string? WhatsAppAddonCode { get; init; }
        public EstadoSuscripcion? WhatsAppAddonStatus { get; init; }
        public string? WhatsAppAddonStatusLabel { get; init; }
        public int? WhatsAppMonthlyLimit { get; init; }
        public int WhatsAppMessagesUsed { get; init; }
        public int? WhatsAppMessagesRemaining { get; init; }
        public bool HasWhatsAppAddon => !string.IsNullOrWhiteSpace(WhatsAppAddonName);
    }
}
