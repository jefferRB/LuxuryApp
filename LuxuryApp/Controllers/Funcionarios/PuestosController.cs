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
        private readonly ILogger<PuestosController> _logger;

        public PuestosController(ApplicationDbContext context, ILogger<PuestosController> logger)
        {
            _context = context;
            _logger = logger;
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
        public async Task<IActionResult> Create(
            [Bind(nameof(Puesto.IdPuesto) + "," + nameof(Puesto.NombrePuesto) + "," + nameof(Puesto.Detalle))]
            Puesto puesto)
        {
            return await Save(puesto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            [Bind(nameof(Puesto.IdPuesto) + "," + nameof(Puesto.NombrePuesto) + "," + nameof(Puesto.Detalle))]
            Puesto puesto)
        {
            NormalizePuesto(puesto);

            bool existe = await _context.Puestos
                .AnyAsync(p => p.NombrePuesto == puesto.NombrePuesto && p.IdPuesto != puesto.IdPuesto);

            if (existe)
            {
                ModelState.AddModelError(nameof(Puesto.NombrePuesto), "Ya existe un puesto con ese nombre.");
            }

            if (!ModelState.IsValid)
            {
                return PartialView("_FormPuesto", puesto);
            }

            try
            {
                if (puesto.IdPuesto == 0)
                {
                    puesto.Activo = true;
                    _context.Puestos.Add(puesto);
                }
                else
                {
                    var puestoDb = await _context.Puestos
                        .FirstOrDefaultAsync(p => p.IdPuesto == puesto.IdPuesto);

                    if (puestoDb == null)
                    {
                        return NotFound();
                    }

                    puestoDb.NombrePuesto = puesto.NombrePuesto;
                    puestoDb.Detalle = puesto.Detalle;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al guardar puesto {PuestoId}.", puesto.IdPuesto);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el puesto.");
                return PartialView("_FormPuesto", puesto);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueó la operación sobre puesto {PuestoId}.", puesto.IdPuesto);
                return BadRequest("No fue posible guardar el puesto por una validación de seguridad o consistencia.");
            }
        }

        // ========================================
        // EDITAR (GET)
        // ========================================
        public async Task<IActionResult> Edit(int id)
        {
            var puesto = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == id);

            if (puesto == null)
                return NotFound();

            return View(puesto);
        }

        // ========================================
        // EDITAR (POST)
        // ========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind(nameof(Puesto.IdPuesto) + "," + nameof(Puesto.NombrePuesto) + "," + nameof(Puesto.Detalle))]
            Puesto puesto)
        {
            if (id != puesto.IdPuesto)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var puestoDb = await _context.Puestos
                        .FirstOrDefaultAsync(p => p.IdPuesto == id);

                    if (puestoDb == null)
                        return NotFound();

                    puestoDb.NombrePuesto = puesto.NombrePuesto;
                    puestoDb.Detalle = puesto.Detalle;

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var puesto = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == id);

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var puesto = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == id);

            if (puesto == null)
            {
                return NotFound();
            }

            bool estaEnUso = await _context.Funcionarios
                .AnyAsync(f => f.IdPuesto == id);

            if (estaEnUso)
            {
                return BadRequest("No se puede eliminar el puesto porque tiene funcionarios asociados.");
            }

            try
            {
                _context.Puestos.Remove(puesto);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar puesto {PuestoId}.", id);
                return BadRequest("No fue posible eliminar el puesto porque tiene relaciones activas.");
            }
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
        public async Task<IActionResult> FormPuesto(int? id)
        {
            if (!id.HasValue || id.Value <= 0)
            {
                return PartialView("~/Views/Puestos/_FormPuesto.cshtml", new Puesto());
            }

            var puesto = await _context.Puestos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdPuesto == id.Value);

            if (puesto == null)
            {
                return NotFound();
            }

            return PartialView("~/Views/Puestos/_FormPuesto.cshtml", puesto);
        }

        private static void NormalizePuesto(Puesto puesto)
        {
            puesto.NombrePuesto = string.IsNullOrWhiteSpace(puesto.NombrePuesto)
                ? string.Empty
                : puesto.NombrePuesto.Trim();

            puesto.Detalle = string.IsNullOrWhiteSpace(puesto.Detalle)
                ? null
                : puesto.Detalle.Trim();
        }
    }
}
