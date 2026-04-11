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

        public CategoriasController(ApplicationDbContext context)
        {
            _context = context;
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
            [Bind(nameof(Categoria.Nombre) + "," + nameof(Categoria.Detalle))]
            Categoria categoria)
        {
            if (ModelState.IsValid)
            {
                categoria.Activo = true;

                _context.Add(categoria);
                await _context.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Ok();

                return RedirectToAction(nameof(Index));
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView("_FormCategoria", categoria);

            return View(categoria);
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

        public IActionResult ModalCategorias()
        {
            var categorias = _context.Categorias
                .OrderBy(c => c.Nombre)
                .ToList();

            return PartialView("_CategoriasModal", categorias);
        }

        public IActionResult FormCategoria()
        {
            return PartialView("~/Views/Categorias/_FormCategoria.cshtml", new Categoria());
        }
    }
}
