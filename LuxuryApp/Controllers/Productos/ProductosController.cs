using LuxuryApp.Models.Productos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Productos
{
    [Authorize(Roles = "Administrador")]

    public class ProductosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProductosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =========================
        // INDEX
        // =========================
        public async Task<IActionResult> Index()
        {
            var productos = await _context.Productos
                .OrderBy(p => p.NombreProducto)
                .ToListAsync();

            var vm = new ProductoIndexViewModel
            {
                Productos = productos,
                TotalProductos = productos.Count,
                ProductosBajoStock = productos.Count(p => p.CantidadProducto <= p.StockMinimo),
                ValorInventario = productos.Sum(p => p.CantidadProducto * p.PrecioProducto)
            };

            return View(vm);
        }

        // =========================
        // CREATE GET
        // =========================
        public IActionResult Create()
        {
            return View(new ProductoViewModel
            {
                Producto = new Producto()
            });
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
            Producto producto)
        {
            var vm = new ProductoViewModel
            {
                Producto = producto
            };

            if (ModelState.IsValid)
            {
                producto.Activo = true;
                _context.Productos.Add(producto);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View(vm);
        }

        // =========================
        // EDIT GET
        // =========================
        public async Task<IActionResult> Edit(int id)
        {
            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.IdProducto == id);

            if (producto == null)
                return NotFound();

            return View(new ProductoViewModel
            {
                Producto = producto
            });
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
            Producto producto)
        {
            var vm = new ProductoViewModel
            {
                Producto = producto
            };

            if (id != producto.IdProducto)
                return NotFound();

            if (ModelState.IsValid)
            {
                var productoDb = await _context.Productos
                    .FirstOrDefaultAsync(p => p.IdProducto == id);

                if (productoDb == null)
                    return NotFound();

                productoDb.NombreProducto = producto.NombreProducto;
                productoDb.DetalleProducto = producto.DetalleProducto;
                productoDb.PrecioProducto = producto.PrecioProducto;
                productoDb.CantidadProducto = producto.CantidadProducto;
                productoDb.StockMinimo = producto.StockMinimo;

                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View(vm);
        }

        // =========================
        // ACTIVAR / DESACTIVAR
        // =========================
        [HttpPost]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var producto = await _context.Productos
                .FirstOrDefaultAsync(p => p.IdProducto == id);

            if (producto == null)
                return NotFound();

            producto.Activo = !producto.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
