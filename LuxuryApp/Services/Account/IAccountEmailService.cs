namespace LuxuryApp.Services.Account
{
    public interface IAccountEmailService
    {
        Task SendPasswordResetEmailAsync(
            string toEmail,
            string displayName,
            string resetLink,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Invitación al portal de funcionarios: el funcionario define su propia
        /// contraseña a través del enlace (token de restablecimiento).
        /// </summary>
        Task SendFuncionarioInvitationEmailAsync(
            string toEmail,
            string displayName,
            string setPasswordLink,
            string businessName,
            CancellationToken cancellationToken = default);
    }
}
