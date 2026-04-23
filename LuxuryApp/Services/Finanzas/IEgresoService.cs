using LuxuryApp.Models.Finanzas;

namespace LuxuryApp.Services.Finanzas
{
    public interface IEgresoService
    {
        Task RegistrarAsync(EgresoCreateRequest request, CancellationToken cancellationToken = default);
    }
}
