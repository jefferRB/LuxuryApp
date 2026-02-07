using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    public class CategoriasController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoriasController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =============================
        // LISTAR CATEGORIAS
        // =============================
        public async Task<IActionResult> Index()
        {
            var categorias = await _context.Categorias
                .OrderBy(c => c.Nombre)
                .ToListAsync();

            return View(categorias);
        }

        // =============================
        // CREAR CATEGORIA (GET)
        // =============================
        public IActionResult Create()
        {
            return View();
        }

        // =============================
        // CREAR CATEGORIA (POST)
        // =============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Categoria categoria)
        {
            if (ModelState.IsValid)
            {
                categoria.Activo = true;

                _context.Add(categoria);
                await _context.SaveChangesAsync();

                // AJAX modal
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Ok();

                return RedirectToAction(nameof(Index));
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView("_FormCategoria", categoria);

            return View(categoria);
        }

        // =============================
        // EDITAR CATEGORIA (GET)
        // =============================
        public async Task<IActionResult> Edit(int id)
        {
            var categoria = await _context.Categorias.FindAsync(id);

            if (categoria == null)
                return NotFound();

            return View(categoria);
        }

        // =============================
        // EDITAR CATEGORIA (POST)
        // =============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Categoria categoria)
        {
            if (id != categoria.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(categoria);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Categorias.Any(c => c.Id == categoria.Id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(categoria);
        }

        // =============================
        // ACTIVAR / DESACTIVAR
        // =============================
        [HttpPost]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var categoria = await _context.Categorias.FindAsync(id);

            if (categoria == null)
                return NotFound();

            categoria.Activo = !categoria.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }

        // =============================
        // MODAL LISTADO
        // =============================
        public IActionResult ModalCategorias()
        {
            var categorias = _context.Categorias
                .OrderBy(c => c.Nombre)
                .ToList();

            return PartialView("_CategoriasModal", categorias);
        }

        // =============================
        // FORMULARIO MODAL
        // =============================
        public IActionResult FormCategoria()
        {
            return PartialView("~/Views/Categorias/_FormCategoria.cshtml", new Categoria());
        }
    }
}
