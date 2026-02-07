using LuxuryApp.Models.Productos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Productos
{
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
        public async Task<IActionResult> Create(ProductoViewModel vm)
        {
            if (ModelState.IsValid)
            {
                _context.Productos.Add(vm.Producto);
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
            var producto = await _context.Productos.FindAsync(id);

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
        public async Task<IActionResult> Edit(int id, ProductoViewModel vm)
        {
            if (id != vm.Producto.IdProducto)
                return NotFound();

            if (ModelState.IsValid)
            {
                _context.Update(vm.Producto);
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
            var producto = await _context.Productos.FindAsync(id);

            if (producto == null)
                return NotFound();

            producto.Activo = !producto.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
