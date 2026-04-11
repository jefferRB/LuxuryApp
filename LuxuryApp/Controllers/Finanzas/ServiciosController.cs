using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]
    public class ServiciosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ServiciosController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var servicios = await _context.Servicios
                .OrderBy(s => s.Nombre)
                .ToListAsync();

            return View(servicios);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind(nameof(Servicio.Nombre) + "," + nameof(Servicio.Precio) + "," + nameof(Servicio.DuracionMinutos))]
            Servicio servicio)
        {
            if (ModelState.IsValid)
            {
                servicio.Activo = true;

                _context.Add(servicio);
                await _context.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Ok();
                }

                return RedirectToAction(nameof(Index));
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_FormServicio", servicio);
            }

            return View(servicio);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var servicio = await _context.Servicios
                .FirstOrDefaultAsync(s => s.Id == id);

            if (servicio == null)
                return NotFound();

            return View(servicio);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind(nameof(Servicio.Id) + "," + nameof(Servicio.Nombre) + "," + nameof(Servicio.Precio) + "," + nameof(Servicio.DuracionMinutos))]
            Servicio servicio)
        {
            if (id != servicio.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var servicioDb = await _context.Servicios
                        .FirstOrDefaultAsync(s => s.Id == id);

                    if (servicioDb == null)
                        return NotFound();

                    servicioDb.Nombre = servicio.Nombre;
                    servicioDb.Precio = servicio.Precio;
                    servicioDb.DuracionMinutos = servicio.DuracionMinutos;

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Servicios.Any(s => s.Id == servicio.Id))
                        return NotFound();

                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(servicio);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var servicio = await _context.Servicios
                .FirstOrDefaultAsync(s => s.Id == id);

            if (servicio == null)
                return NotFound();

            servicio.Activo = !servicio.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerPrecio(int id)
        {
            var servicio = await _context.Servicios
                .Where(s => s.Id == id)
                .Select(s => new { s.Precio })
                .FirstOrDefaultAsync();

            return Json(servicio);
        }

        public IActionResult ModalServicios()
        {
            var servicios = _context.Servicios
                .OrderBy(s => s.Nombre)
                .ToList();

            return PartialView("_ServiciosModal", servicios);
        }

        public IActionResult FormServicio()
        {
            return PartialView("~/Views/Servicios/_FormServicio.cshtml", new Servicio());
        }

        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            var servicio = await _context.Servicios
                .FirstOrDefaultAsync(s => s.Id == id);

            if (servicio == null)
                return NotFound();

            var tieneCobros = await _context.Cobros
                .AnyAsync(c => c.ServicioId == id);

            if (tieneCobros)
            {
                return BadRequest("No se puede eliminar este servicio porque tiene cobros asociados.");
            }

            var tieneCitas = await _context.Citas
                .AnyAsync(c => c.ServicioId == id);

            if (tieneCitas)
            {
                return BadRequest("No se puede eliminar este servicio porque tiene citas asociadas.");
            }

            _context.Servicios.Remove(servicio);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
