using LuxuryApp.Models.Finanzas;

namespace LuxuryApp.Services.Finanzas
{
    public interface ICobroQueryService
    {
        Task<CobroIndexViewModel> BuildIndexViewModelAsync(
            CobroFiltroViewModel filtros,
            bool includeFilterOptions = true,
            CancellationToken cancellationToken = default);

        Task<CobroViewModel> BuildCreateViewModelAsync(
            Cobro? cobro = null,
            CancellationToken cancellationToken = default);

        Task<decimal?> ObtenerPrecioServicioAsync(int id, CancellationToken cancellationToken = default);
        Task<decimal?> ObtenerPrecioProductoAsync(int id, CancellationToken cancellationToken = default);
    }
}
