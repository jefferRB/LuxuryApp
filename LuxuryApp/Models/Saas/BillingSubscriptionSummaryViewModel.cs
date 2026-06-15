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
        public bool HasWhatsAppAddon => !string.IsNullOrWhiteSpace(WhatsAppAddonCode);
    }
}
