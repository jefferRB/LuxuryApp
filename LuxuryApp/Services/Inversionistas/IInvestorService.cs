using LuxuryApp.Models.Inversionistas;

namespace LuxuryApp.Services.Inversionistas
{
    /// <summary>
    /// Gestión de inversionistas, acuerdos de participación y política de cálculo del tenant.
    /// Todas las validaciones de negocio (100 %, solapes, cambios a mitad de periodo) viven acá.
    /// </summary>
    public interface IInvestorService
    {
        Task<InvestorsIndexViewModel> BuildIndexAsync(CancellationToken cancellationToken = default);

        Task<InvestorFormViewModel> BuildCreateFormAsync(CancellationToken cancellationToken = default);

        Task<InvestorFormViewModel?> BuildEditFormAsync(int investorId, CancellationToken cancellationToken = default);

        /// <summary>Crea el inversionista y su primer acuerdo. Devuelve el Id creado.</summary>
        Task<int> CreateAsync(
            InvestorFormViewModel form,
            string? userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Actualiza los datos del inversionista. Si el porcentaje, la frecuencia o el tratamiento
        /// de pérdidas cambian, cierra el acuerdo vigente y crea una nueva versión desde la fecha
        /// efectiva indicada (que debe ser el inicio de un periodo).
        /// </summary>
        Task UpdateAsync(
            int investorId,
            InvestorFormViewModel form,
            string? userId,
            CancellationToken cancellationToken = default);

        Task SetActivoAsync(
            int investorId,
            bool activo,
            string? userId,
            CancellationToken cancellationToken = default);

        Task<InvestorProfitPolicy> GetPolicyAsync(CancellationToken cancellationToken = default);

        Task<InvestorPolicyViewModel> BuildPolicyFormAsync(CancellationToken cancellationToken = default);

        Task SavePolicyAsync(
            InvestorPolicyViewModel form,
            string? userId,
            CancellationToken cancellationToken = default);

        /// <summary>Acuerdo vigente del inversionista en la fecha indicada, o null.</summary>
        Task<InvestorAgreement?> GetAgreementForDateAsync(
            int investorId,
            DateOnly fecha,
            CancellationToken cancellationToken = default);
    }
}
