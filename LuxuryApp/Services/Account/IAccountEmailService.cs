namespace LuxuryApp.Services.Account
{
    public interface IAccountEmailService
    {
        Task SendPasswordResetEmailAsync(
            string toEmail,
            string displayName,
            string resetLink,
            CancellationToken cancellationToken = default);
    }
}
