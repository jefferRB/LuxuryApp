using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]
    public class CategoriasController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CategoriasController> _logger;

        public CategoriasController(ApplicationDbContext context, ILogger<CategoriasController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var categorias = await _context.Categorias
                .OrderBy(c => c.Nombre)
                .ToListAsync();

            return View(categorias);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind(nameof(Categoria.Id) + "," + nameof(Categoria.Nombre) + "," + nameof(Categoria.Detalle))]
            Categoria categoria)
        {
            return await Save(categoria);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            [Bind(nameof(Categoria.Id) + "," + nameof(Categoria.Nombre) + "," + nameof(Categoria.Detalle))]
            Categoria categoria)
        {
            NormalizeCategoria(categoria);

            bool existe = await _context.Categorias
                .AnyAsync(c => c.Nombre == categoria.Nombre && c.Id != categoria.Id);

            if (existe)
            {
                ModelState.AddModelError(nameof(Categoria.Nombre), "Ya existe una categoría con ese nombre.");
            }

            if (!ModelState.IsValid)
            {
                return PartialView("_FormCategoria", categoria);
            }

            try
            {
                if (categoria.Id == 0)
                {
                    categoria.Activo = true;
                    _context.Categorias.Add(categoria);
                }
                else
                {
                    var categoriaDb = await _context.Categorias
                        .FirstOrDefaultAsync(c => c.Id == categoria.Id);

                    if (categoriaDb == null)
                    {
                        return NotFound();
                    }

                    categoriaDb.Nombre = categoria.Nombre;
                    categoriaDb.Detalle = categoria.Detalle;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al guardar categoría {CategoriaId}.", categoria.Id);
                ModelState.AddModelError(string.Empty, "No fue posible guardar la categoría.");
                return PartialView("_FormCategoria", categoria);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueó la operación sobre categoría {CategoriaId}.", categoria.Id);
                return BadRequest("No fue posible guardar la categoría por una validación de seguridad o consistencia.");
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var categoria = await _context.Categorias
                .FirstOrDefaultAsync(c => c.Id == id);

            if (categoria == null)
                return NotFound();

            return View(categoria);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind(nameof(Categoria.Id) + "," + nameof(Categoria.Nombre) + "," + nameof(Categoria.Detalle))]
            Categoria categoria)
        {
            if (id != categoria.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var categoriaDb = await _context.Categorias
                        .FirstOrDefaultAsync(c => c.Id == id);

                    if (categoriaDb == null)
                        return NotFound();

                    categoriaDb.Nombre = categoria.Nombre;
                    categoriaDb.Detalle = categoria.Detalle;

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Categorias.Any(c => c.Id == categoria.Id))
                        return NotFound();

                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(categoria);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var categoria = await _context.Categorias
                .FirstOrDefaultAsync(c => c.Id == id);

            if (categoria == null)
                return NotFound();

            categoria.Activo = !categoria.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var categoria = await _context.Categorias
                .FirstOrDefaultAsync(c => c.Id == id);

            if (categoria == null)
            {
                return NotFound();
            }

            bool estaEnUso = await _context.Egresos
                .AnyAsync(e => e.CategoriaId == id);

            if (estaEnUso)
            {
                return BadRequest("No se puede eliminar la categoría porque ya está siendo usada en egresos.");
            }

            try
            {
                _context.Categorias.Remove(categoria);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar categoría {CategoriaId}.", id);
                return BadRequest("No fue posible eliminar la categoría porque tiene relaciones activas.");
            }
        }

        public IActionResult ModalCategorias()
        {
            var categorias = _context.Categorias
                .OrderBy(c => c.Nombre)
                .ToList();

            return PartialView("_CategoriasModal", categorias);
        }

        public async Task<IActionResult> FormCategoria(int? id)
        {
            if (!id.HasValue || id.Value <= 0)
            {
                return PartialView("~/Views/Categorias/_FormCategoria.cshtml", new Categoria());
            }

            var categoria = await _context.Categorias
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id.Value);

            if (categoria == null)
            {
                return NotFound();
            }

            return PartialView("~/Views/Categorias/_FormCategoria.cshtml", categoria);
        }

        private static void NormalizeCategoria(Categoria categoria)
        {
            categoria.Nombre = string.IsNullOrWhiteSpace(categoria.Nombre)
                ? string.Empty
                : categoria.Nombre.Trim();

            categoria.Detalle = string.IsNullOrWhiteSpace(categoria.Detalle)
                ? null
                : categoria.Detalle.Trim();
        }
    }
}
