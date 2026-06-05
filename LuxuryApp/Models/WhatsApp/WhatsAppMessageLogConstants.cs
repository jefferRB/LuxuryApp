namespace LuxuryApp.Models.WhatsApp
{
    public static class WhatsAppMessageDirections
    {
        public const string Outbound = "Outbound";
        public const string Inbound = "Inbound";
        public const string Status = "Status";
    }

    public static class WhatsAppNotificationTypes
    {
        public const string Confirmation = "Confirmation";
        public const string Reminder3Hours = "Reminder3Hours";
        public const string Reply = "Reply";
        public const string Status = "Status";
    }

    public static class WhatsAppProviders
    {
        public const string Meta = "Meta";
    }

    public static class WhatsAppMessageStatuses
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Sent = "Sent";
        public const string Delivered = "Delivered";
        public const string Read = "Read";
        public const string Failed = "Failed";
        public const string Received = "Received";
        public const string Ignored = "Ignored";
        public const string SkippedTenantDisabled = "SkippedTenantDisabled";
        public const string SkippedDailyLimitExceeded = "SkippedDailyLimitExceeded";
        public const string SkippedInvalidPhone = "SkippedInvalidPhone";
        public const string SkippedConfiguration = "SkippedConfiguration";
        public const string SkippedNotEligible = "SkippedNotEligible";
        public const string SkippedConsentMissing = "SkippedConsentMissing";
        public const string SkippedSubscriptionRequired = "SkippedSubscriptionRequired";
        public const string SkippedMonthlyLimitExceeded = "SkippedMonthlyLimitExceeded";
    }

    public static class WhatsAppErrorCodes
    {
        public const string TenantDisabled = "TenantDisabled";
        public const string DailyLimitExceeded = "DailyLimitExceeded";
        public const string InvalidPhone = "InvalidPhone";
        public const string ConfigurationDisabled = "ConfigurationDisabled";
        public const string NotificationTypeDisabled = "NotificationTypeDisabled";
        public const string AppointmentNotEligible = "AppointmentNotEligible";
        public const string ConsentMissing = "ConsentMissing";
        public const string SubscriptionRequired = "SubscriptionRequired";
        public const string MonthlyLimitExceeded = "MonthlyLimitExceeded";
    }

    public static class WhatsAppConfirmationStates
    {
        public const string Pendiente = "Pendiente";
        public const string Confirmada = "Confirmada";
        public const string Cancelada = "Cancelada";
        public const string NoEnviada = "NoEnviada";
        public const string ErrorEnvio = "ErrorEnvio";
    }

    public static class WhatsAppConsentSources
    {
        public const string ClienteForm = "ClienteForm";
        public const string ClienteRegistrado = "ClienteRegistrado";
        public const string CitaManual = "CitaManual";
        public const string SinConsentimiento = "SinConsentimiento";
    }

    public static class WhatsAppConsentTextVersions
    {
        public const string WaOptInV1 = "wa_optin_v1";
    }
}
