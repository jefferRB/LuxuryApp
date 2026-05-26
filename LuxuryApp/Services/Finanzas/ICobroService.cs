using LuxuryApp.Models.Finanzas;

namespace LuxuryApp.Services.Finanzas
{
    public interface ICobroService
    {
        Task RegistrarAsync(CobroCreateRequest request, CancellationToken cancellationToken = default);
        Task<bool> ActualizarAsync(CobroUpdateRequest request, CancellationToken cancellationToken = default);
        Task<bool> EliminarAsync(int idCobro, CancellationToken cancellationToken = default);
    }
}
