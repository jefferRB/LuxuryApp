using LuxuryApp.Models.Finanzas;

namespace LuxuryApp.Services.Finanzas
{
    public interface ICobroService
    {
        Task RegistrarAsync(CobroCreateRequest request, CancellationToken cancellationToken = default);
    }
}
