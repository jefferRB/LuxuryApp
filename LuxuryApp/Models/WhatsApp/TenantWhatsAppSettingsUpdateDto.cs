using System.ComponentModel.DataAnnotations;

namespace LuxuryApp.Models.WhatsApp
{
    public sealed class TenantWhatsAppSettingsUpdateDto
    {
        public bool IsEnabled { get; set; }

        public bool SendConfirmationOnCreate { get; set; } = true;

        public bool SendReminderThreeHoursBefore { get; set; } = true;

        [Range(0, int.MaxValue, ErrorMessage = "El limite diario no puede ser negativo.")]
        public int DailyMessageLimit { get; set; } = TenantWhatsAppSettings.DefaultDailyMessageLimit;

        [Required]
        [MaxLength(100)]
        public string TimeZoneId { get; set; } = TenantWhatsAppSettings.DefaultTimeZoneId;

        [MaxLength(2000)]
        public string? Notes { get; set; }

        /// Código de paquete a asignar manual. null/"" = sin cambio; "NONE" = revocar; "WA400"/"WA800"/"WA1200" = asignar.
        [MaxLength(10)]
        public string? AddonCode { get; set; }

        [MaxLength(2000)]
        public string? ManualAssignmentObservation { get; set; }

        // ── Programación de confirmaciones ──
        [MaxLength(40)]
        public string ConfirmationScheduleMode { get; set; } = WhatsAppConfirmationScheduleModes.RelativeBeforeAppointment;

        [Range(1, 168, ErrorMessage = "Las horas de anticipación deben estar entre 1 y 168.")]
        public int ConfirmationHoursBefore { get; set; } = TenantWhatsAppSettings.DefaultConfirmationHoursBefore;

        public TimeOnly? ConfirmationBatchTime { get; set; }

        [MaxLength(30)]
        public string ConfirmationBatchTarget { get; set; } = WhatsAppConfirmationBatchTargets.TomorrowAllDay;

        public TimeOnly? ConfirmationMorningStart { get; set; }

        public TimeOnly? ConfirmationMorningEnd { get; set; }

        public bool SendConfirmationImmediatelyIfInsideWindow { get; set; } = true;

        // ── Programación de recordatorios ──
        [MaxLength(40)]
        public string ReminderScheduleMode { get; set; } = WhatsAppReminderScheduleModes.RelativeBeforeAppointment;

        [Range(1, 168, ErrorMessage = "Las horas de anticipación deben estar entre 1 y 168.")]
        public int ReminderHoursBefore { get; set; } = TenantWhatsAppSettings.DefaultReminderHoursBefore;

        public TimeOnly? ReminderBatchTime { get; set; }

        [MaxLength(30)]
        public string ReminderBatchTarget { get; set; } = WhatsAppReminderBatchTargets.SameDayRemaining;

        [Range(1, 168, ErrorMessage = "Las horas de anticipación deben estar entre 1 y 168.")]
        public int ReminderLookAheadHours { get; set; } = TenantWhatsAppSettings.DefaultReminderHoursBefore;

        public bool SendReminderImmediatelyIfInsideWindow { get; set; } = true;

        // ── Horas de silencio ──
        public bool QuietHoursEnabled { get; set; }

        public TimeOnly? QuietHoursStart { get; set; }

        public TimeOnly? QuietHoursEnd { get; set; }
    }
}
