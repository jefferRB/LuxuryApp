using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    public class ServiciosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ServiciosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // LISTAR SERVICIOS
        public async Task<IActionResult> Index()
        {
            var servicios = await _context.Servicios
                .OrderBy(s => s.Nombre)
                .ToListAsync();

            return View(servicios);
        }

        
        // CREAR SERVICIO (GET)
       
        public IActionResult Create()
        {
            return View();
        }


        // CREAR SERVICIO (POST)

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Servicio servicio)
        {
            if (ModelState.IsValid)
            {
                servicio.Activo = true;

                _context.Add(servicio);
                await _context.SaveChangesAsync();

                // 👉 Si viene del modal (AJAX)
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Ok();
                }

                return RedirectToAction(nameof(Index));
            }

            // 👉 Si AJAX devolver partial
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_FormServicio", servicio);
            }

            return View(servicio);
        }



        // EDITAR SERVICIO (GET)

        public async Task<IActionResult> Edit(int id)
        {
            var servicio = await _context.Servicios.FindAsync(id);

            if (servicio == null)
                return NotFound();

            return View(servicio);
        }

        
        // EDITAR SERVICIO (POST)
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Servicio servicio)
        {
            if (id != servicio.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(servicio);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Servicios.Any(s => s.Id == servicio.Id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(servicio);
        }

        
        // ACTIVAR / DESACTIVAR
        
        [HttpPost]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            var servicio = await _context.Servicios.FindAsync(id);

            if (servicio == null)
                return NotFound();

            servicio.Activo = !servicio.Activo;

            await _context.SaveChangesAsync();

            return Ok();
        }

        
        // OBTENER PRECIO (AJAX)
        
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


    }
}
