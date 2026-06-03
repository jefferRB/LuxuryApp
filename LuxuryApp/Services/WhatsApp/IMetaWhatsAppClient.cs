namespace LuxuryApp.Services.WhatsApp
{
    public interface IMetaWhatsAppClient
    {
        string? NormalizePhoneNumber(string? phoneNumber);

        bool IsValidPhoneNumber(string? phoneNumber);

        Task<MetaWhatsAppSendResult> SendConfirmationTemplateAsync(
            string recipientPhone,
            string customerName,
            string businessName,
            string appointmentDate,
            string appointmentTime,
            string professionalName,
            CancellationToken cancellationToken = default);

        Task<MetaWhatsAppSendResult> SendReminderTemplateAsync(
            string recipientPhone,
            string customerName,
            string businessName,
            string appointmentTime,
            string professionalName,
            CancellationToken cancellationToken = default);

        Task<MetaWhatsAppSendResult> SendTextMessageAsync(
            string recipientPhone,
            string message,
            CancellationToken cancellationToken = default);

        Task<MetaWhatsAppConfigurationDiagnosticResult> TestConfigurationAsync(
            CancellationToken cancellationToken = default);
    }
}
