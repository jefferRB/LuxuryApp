using LuxuryApp.Models.Funcionarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Funcionarios
{
    [Authorize(Roles = "Administrador")]
    public class PuestosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PuestosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ========================================
        // LISTAR
        // ========================================
        public async Task<IActionResult> Index()
        {
            var puestos = await _context.Puestos
                .OrderBy(p => p.NombrePuesto)
                .ToListAsync();

            return View(puestos);
        }

        // ========================================
        // CREAR (GET)
        // ========================================
        public IActionResult Create()
        {
            return View();
        }

        // ========================================
        // CREAR (POST)
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Puesto puesto)
        {
            bool existe = await _context.Puestos
                .AnyAsync(p => p.NombrePuesto == puesto.NombrePuesto);

            if (existe)
                ModelState.AddModelError("NombrePuesto", "Ya existe un puesto con ese nombre.");

            if (ModelState.IsValid)
            {
                puesto.Activo = true;

                _context.Add(puesto);
                await _context.SaveChangesAsync();

                // 👉 Soporte AJAX
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Ok();

                return RedirectToAction(nameof(Index));
            }

            // 👉 Si es AJAX devolver partial
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView("_FormPuesto", puesto);

            return View(puesto);
        }

        // ========================================
        // EDITAR (GET)
        // ========================================
        public async Task<IActionResult> Edit(int id)
        {
            var puesto = await _context.Puestos.FindAsync(id);

            if (puesto == null)
                return NotFound();

            return View(puesto);
        }

        // ========================================
        // EDITAR (POST)
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Puesto puesto)
        {
            if (id != puesto.IdPuesto)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(puesto);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Puestos.Any(p => p.IdPuesto == puesto.IdPuesto))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(puesto);
        }

        // ========================================
        // ACTIVAR / DESACTIVAR
        // ========================================
        [HttpPost]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var puesto = await _context.Puestos.FindAsync(id);

            if (puesto == null)
                return NotFound();

            // Validar si está en uso antes de desactivar
            if (puesto.Activo)
            {
                bool estaEnUso = await _context.Funcionarios
                    .AnyAsync(f => f.IdPuesto == id);

                if (estaEnUso)
                    return BadRequest("No se puede desactivar porque está asignado a funcionarios.");
            }

            puesto.Activo = !puesto.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }

        // ========================================
        // MODAL LISTADO
        // ========================================
        public IActionResult ModalPuestos()
        {
            var puestos = _context.Puestos
                
                .OrderBy(p => p.NombrePuesto)
                .ToList();

            return PartialView("_PuestosModal", puestos);
        }

        // ========================================
        // FORMULARIO PARTIAL
        // ========================================
        public IActionResult FormPuesto()
        {
            return PartialView("~/Views/Puestos/_FormPuesto.cshtml", new Puesto());
        }
    }
}