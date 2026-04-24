using LuxuryApp.Models.Productos;
using LuxuryApp.Services.Productos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Productos
{
    [Authorize(Roles = "Administrador")]
    public class ProductosController : Controller
    {
        private readonly IProductoService _productoService;
        private readonly IProductoQueryService _productoQueryService;
        private readonly ILogger<ProductosController> _logger;

        public ProductosController(
            IProductoService productoService,
            IProductoQueryService productoQueryService,
            ILogger<ProductosController> logger)
        {
            _productoService = productoService;
            _productoQueryService = productoQueryService;
            _logger = logger;
        }

        // =========================
        // INDEX
        // =========================
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var vm = await _productoQueryService.BuildIndexViewModelAsync(cancellationToken);
            return View(vm);
        }

        // =========================
        // CREATE GET
        // =========================
        public IActionResult Create()
        {
            return View(_productoQueryService.BuildFormViewModel());
        }

        // =========================
        // CREATE POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind(
                nameof(Producto.NombreProducto),
                nameof(Producto.DetalleProducto),
                nameof(Producto.PrecioProducto),
                nameof(Producto.CantidadProducto),
                nameof(Producto.StockMinimo),
                Prefix = "Producto")]
            Producto producto,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return View(_productoQueryService.BuildFormViewModel(producto));
            }

            try
            {
                await _productoService.RegistrarAsync(MapRequest(producto), cancellationToken);
                return RedirectToAction(nameof(Index));
            }
            catch (ProductoValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            return View(_productoQueryService.BuildFormViewModel(producto));
        }

        // =========================
        // EDIT GET
        // =========================
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var vm = await _productoQueryService.BuildEditViewModelAsync(id, cancellationToken);

            if (vm is null)
            {
                return NotFound();
            }

            return View(vm);
        }

        // =========================
        // EDIT POST
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind(
                nameof(Producto.IdProducto),
                nameof(Producto.NombreProducto),
                nameof(Producto.DetalleProducto),
                nameof(Producto.PrecioProducto),
                nameof(Producto.CantidadProducto),
                nameof(Producto.StockMinimo),
                Prefix = "Producto")]
            Producto producto,
            CancellationToken cancellationToken)
        {
            if (id != producto.IdProducto)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(_productoQueryService.BuildFormViewModel(producto));
            }

            try
            {
                await _productoService.ActualizarAsync(id, MapRequest(producto), cancellationToken);
                return RedirectToAction(nameof(Index));
            }
            catch (ProductoValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            return View(_productoQueryService.BuildFormViewModel(producto));
        }

        // =========================
        // ACTIVAR / DESACTIVAR
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActivo(int id, CancellationToken cancellationToken)
        {
            try
            {
                var toggled = await _productoService.ToggleActivoAsync(id, cancellationToken);

                if (!toggled)
                {
                    return NotFound();
                }

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado del producto {ProductoId}.", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        private static ProductoWriteRequest MapRequest(Producto producto)
        {
            return new ProductoWriteRequest
            {
                NombreProducto = producto.NombreProducto,
                DetalleProducto = producto.DetalleProducto,
                PrecioProducto = producto.PrecioProducto,
                CantidadProducto = producto.CantidadProducto,
                StockMinimo = producto.StockMinimo
            };
        }
    }
}