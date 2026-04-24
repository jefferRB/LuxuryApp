using LuxuryApp.Models.Productos;

namespace LuxuryApp.Services.Productos
{
    public interface IProductoQueryService
    {
        Task<ProductoIndexViewModel> BuildIndexViewModelAsync(CancellationToken cancellationToken = default);

        ProductoViewModel BuildFormViewModel(Producto? producto = null);

        Task<ProductoViewModel?> BuildEditViewModelAsync(int idProducto, CancellationToken cancellationToken = default);
    }
}
