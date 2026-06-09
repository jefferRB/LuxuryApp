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
    }
}
