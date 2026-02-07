using LuxuryApp.Models.Calendar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Calendar
{
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CalendarController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CitaCreateVM vm)
        {

            if (vm == null)
                return BadRequest("Datos inválidos");

            // 1️⃣ Crear cita
            var cita = new Cita
            {
                NombreCliente = vm.NombreCliente,
                TelefonoCliente = vm.TelefonoCliente,
                Servicio = vm.Servicio,
                FechaHoraCita = vm.FechaHoraCita
            };

            _context.Citas.Add(cita);
            await _context.SaveChangesAsync(); // 🔥 NECESARIO para obtener cita.Id

            // 2️⃣ Relacionar barberos
            foreach (var barberoId in vm.BarberoIds)
            {
                var cb = new CitaBarbero
                {
                    CitaId = cita.Id,
                    BarberoId = barberoId
                };

                _context.CitaBarberos.Add(cb);
            }

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        public IActionResult GetCitasByDay(string date)
        {
            var fecha = DateTime.Parse(date);

            var citas = _context.Citas
                .Include(c => c.CitaBarberos)
                    .ThenInclude(cb => cb.Barbero)
                .Where(c => c.FechaHoraCita.Date == fecha.Date)
                .Select(c => new
                {
                    c.Id,
                    c.NombreCliente,
                    c.TelefonoCliente,
                    c.Servicio,
                    c.FechaHoraCita,
                    Barberos = c.CitaBarberos.Select(cb => new
                    {
                        cb.Barbero.Id,
                        cb.Barbero.Nombre
                    })
                })
                .ToList();

            return Ok(citas);
        }

        [HttpGet("Calendar/GetById/{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.CitaBarberos)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            return Ok(new
            {
                cita.Id,
                cita.NombreCliente,
                cita.TelefonoCliente,
                cita.Servicio,
                cita.FechaHoraCita,
                BarberoIds = cita.CitaBarberos.Select(cb => cb.BarberoId)
            });
        }

        [HttpPut("Calendar/Edit/{id}")]
        public async Task<IActionResult> Edit(int id, [FromBody] CitaCreateVM vm)
        {
            var cita = await _context.Citas
                .Include(c => c.CitaBarberos)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            // actualizar datos
            cita.NombreCliente = vm.NombreCliente;
            cita.TelefonoCliente = vm.TelefonoCliente;
            cita.Servicio = vm.Servicio;
            cita.FechaHoraCita = vm.FechaHoraCita;

            // actualizar barberos
            _context.CitaBarberos.RemoveRange(cita.CitaBarberos);

            foreach (var barberoId in vm.BarberoIds)
            {
                _context.CitaBarberos.Add(new CitaBarbero
                {
                    CitaId = cita.Id,
                    BarberoId = barberoId
                });
            }

            await _context.SaveChangesAsync();
            return Ok();
        }


        [HttpDelete("Calendar/Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.CitaBarberos)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            _context.CitaBarberos.RemoveRange(cita.CitaBarberos);
            _context.Citas.Remove(cita);

            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        public IActionResult GetCitasCountByMonth(int year, int month)
        {
            var data = _context.Citas
                .Where(c => c.FechaHoraCita.Year == year &&
                            c.FechaHoraCita.Month == month)
                .GroupBy(c => c.FechaHoraCita.Day)
                .Select(g => new
                {
                    Day = g.Key,
                    Count = g.Count()
                })
                .ToList();

            return Ok(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetUpcomingAppointments(int? barberoId)
        {
            var now = DateTime.Now;

            var citas = await _context.Citas
                .Where(c => c.FechaHoraCita >= now)
                 .OrderBy(c => c.FechaHoraCita)
                .Select(c => new
                {
                    c.Id,
                    c.NombreCliente,
                    c.TelefonoCliente,
                    c.Servicio,
                    c.FechaHoraCita,
                    Barberos = c.CitaBarberos.Select(cb => new {
                        cb.BarberoId,
                        cb.Barbero.Nombre
                    })
                })
                .ToListAsync();

            if (barberoId.HasValue)
            {
                citas = citas
                    .Where(c => c.Barberos.Any(b => b.BarberoId == barberoId))
                    .ToList();
            }

            return Ok(citas);
        }

        [HttpGet]
        public async Task<JsonResult> GetServicios()
        {
            var servicios = await _context.Servicios
                .Where(s => s.Activo)
                .Select(s => new
                {
                    nombre = s.Nombre
                })
                .ToListAsync();

            return Json(servicios);
        }

    }
}
