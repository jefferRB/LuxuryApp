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
        // LISTADO
        // ========================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var puestos = await _context.Puestos
                .OrderBy(p => p.NombrePuesto)
                .ToListAsync();

            return View(puestos);
        }

        // ========================================
        // CREAR
        // ========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Puesto puesto)
        {
            bool existe = await _context.Puestos
                .AnyAsync(p => p.NombrePuesto == puesto.NombrePuesto);

            if (existe)
            {
                ModelState.AddModelError("NombrePuesto", "Ya existe un puesto con ese nombre.");
            }

            if (!ModelState.IsValid)
            {
                var puestos = await _context.Puestos
                    .OrderBy(p => p.NombrePuesto)
                    .ToListAsync();

                return View("Index", puestos);
            }

            puesto.Activo = true;

            _context.Puestos.Add(puesto);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Puesto creado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ========================================
        // EDITAR
        // ========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Puesto puesto)
        {
            if (!ModelState.IsValid)
            {
                var puestos = await _context.Puestos
                    .OrderBy(p => p.NombrePuesto)
                    .ToListAsync();

                return View("Index", puestos);
            }

            var puestoDB = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == puesto.IdPuesto);

            if (puestoDB == null)
                return NotFound();

            puestoDB.NombrePuesto = puesto.NombrePuesto;
            puestoDB.Detalle = puesto.Detalle;
            puestoDB.Activo = puesto.Activo;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Puesto actualizado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ========================================
        // ELIMINAR (Soft Delete)
        // ========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            var puesto = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == id);

            if (puesto == null)
                return NotFound();

            // Validación importante:
            bool estaEnUso = await _context.Funcionarios
                .AnyAsync(f => f.IdPuesto == id);

            if (estaEnUso)
            {
                TempData["Error"] = "No se puede eliminar el puesto porque está asignado a uno o más funcionarios.";
                return RedirectToAction(nameof(Index));
            }

            puesto.Activo = false;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Puesto desactivado correctamente.";

            return RedirectToAction(nameof(Index));
        }
    }
}
