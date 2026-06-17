using LuxuryApp.Models.Funcionarios;

namespace LuxuryApp.Services.Funcionarios
{
    /// <summary>
    /// Lectura de datos para el portal del funcionario. Todas las consultas filtran por
    /// TenantId (global query filter) y además por FuncionarioId en backend. Nunca se
    /// confía en un FuncionarioId proveniente del navegador.
    /// </summary>
    public interface IFuncionarioPortalQueryService
    {
        /// <summary>Resuelve y valida el funcionario del portal. Null si no existe en el tenant.</summary>
        Task<PortalFuncionario?> ResolverFuncionarioAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default);

        Task<MiPanelViewModel> ObtenerPanelAsync(
            int funcionarioId,
            bool puedeRegistrarCobros,
            CancellationToken cancellationToken = default);

        Task<MisGananciasViewModel> ObtenerGananciasAsync(
            int funcionarioId,
            CancellationToken cancellationToken = default);

        Task<MisPagosViewModel> ObtenerPagosAsync(
            int funcionarioId,
            int pagina,
            CancellationToken cancellationToken = default);

        Task<MiCalendarioViewModel> ObtenerCalendarioAsync(
            int funcionarioId,
            DateTime fecha,
            bool puedeCrearCitas,
            bool puedeRegistrarCobros,
            CancellationToken cancellationToken = default);

        Task<MisCobrosViewModel> ObtenerCobrosAsync(
            int funcionarioId,
            int pagina,
            bool puedeRegistrarCobros,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resuelve una cita concreta del funcionario para registrar su cobro.
        /// Null si la cita no existe, no es del funcionario o no es del tenant actual.
        /// </summary>
        Task<PortalCitaItem?> ObtenerCitaCobrableAsync(
            int funcionarioId,
            int citaId,
            CancellationToken cancellationToken = default);
    }
}
