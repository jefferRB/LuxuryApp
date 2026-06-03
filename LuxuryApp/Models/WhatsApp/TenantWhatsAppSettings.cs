using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;
using LuxuryApp.Models.SaaS;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxuryApp.Models.WhatsApp
{
    public sealed class TenantWhatsAppSettings : ITenantEntity
    {
        public const int DefaultDailyMessageLimit = 30;
        public const string DefaultTimeZoneId = "America/Costa_Rica";

        public Guid Id { get; set; } = Guid.NewGuid();

        [BindNever]
        public Guid TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public bool IsEnabled { get; set; }

        public bool SendConfirmationOnCreate { get; set; } = true;

        public bool SendReminderThreeHoursBefore { get; set; } = true;

        public int DailyMessageLimit { get; set; } = DefaultDailyMessageLimit;

        [MaxLength(100)]
        public string TimeZoneId { get; set; } = DefaultTimeZoneId;

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? UpdatedByUserId { get; set; }
    }
}
