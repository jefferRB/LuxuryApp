using LuxuryApp.Models.Calendar;
using LuxuryApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Calendar
{
    [Authorize(Roles = "Administrador")]
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

        // ============================
        // CREAR CITA
        // ============================
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CitaCreateVM vm)
        {
            if (vm == null)
                return BadRequest("Datos inválidos");

            // 1️⃣ Servicio
            var servicio = await _context.Servicios
                .FirstOrDefaultAsync(s => s.Id == vm.ServicioId && s.Activo);

            if (servicio == null)
                return BadRequest("Servicio inválido");

            var duracion = servicio.DuracionMinutos ?? 30;

            var inicio = vm.FechaHoraCita;
            var fin = inicio.AddMinutes(duracion);

            // 2️⃣ Traer funcionario UNA sola vez
            var funcionarioData = await _context.Funcionarios
                .Where(f => f.IdFuncionario == vm.FuncionarioId && f.Activo)
                .Select(f => new
                {
                    f.IdFuncionario,
                    f.Nombre,
                    f.ColorCalendario
                })
                .FirstOrDefaultAsync();

            if (funcionarioData == null)
                return BadRequest("Funcionario inválido");

            // 3️⃣ Validar choque optimizado
            var citasExistentes = await _context.Citas
                .Include(c => c.Servicio)
                .Where(c => c.FuncionarioId == vm.FuncionarioId)
                .ToListAsync();

            foreach (var c in citasExistentes)
            {
                var cInicio = c.FechaHoraCita;
                var cFin = cInicio.AddMinutes(c.Servicio?.DuracionMinutos ?? 30);

                if (inicio < cFin && fin > cInicio)
                    return BadRequest("Ya existe una cita en ese horario");
            }

            // 4️⃣ Crear cita
            var cita = new Cita
            {
                NombreCliente = vm.NombreCliente,
                TelefonoCliente = vm.TelefonoCliente,
                ServicioId = servicio.Id,
                FechaHoraCita = inicio,
                FuncionarioId = funcionarioData.IdFuncionario,
                ConfirmacionEnviada = false,
                Recordatorio24hEnviado = false,
                Recordatorio3hEnviado = false
            };

            _context.Citas.Add(cita);
            await _context.SaveChangesAsync();

            // 5️⃣ WhatsApp
            try
            {
                var whatsapp = HttpContext.RequestServices
                    .GetRequiredService<WhatsAppService>();

                var config = HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>();

                var template = config["TwilioTemplates:Confirmacion"];

                await whatsapp.SendTemplateAsync(
                    cita.TelefonoCliente!,
                    template!,
                    new Dictionary<string, object>
                    {
                { "1", cita.NombreCliente },
                { "2", cita.FechaHoraCita.ToString("dd/MM/yyyy") },
                { "3", cita.FechaHoraCita.ToString("hh:mm tt") },
                { "4", servicio.Nombre },
                { "5", funcionarioData.Nombre }
                    });

                cita.ConfirmacionEnviada = true;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error enviando confirmación: " + ex.Message);
            }

            // 6️⃣ Respuesta limpia para el calendario
            var citaCreada = new
            {
                id = cita.Id,
                nombreCliente = cita.NombreCliente,
                telefonoCliente = cita.TelefonoCliente,
                fechaHoraCita = cita.FechaHoraCita,
                duracionMinutos = duracion,
                funcionarioId = funcionarioData.IdFuncionario,
                servicioNombre = servicio.Nombre,
                colorCalendario = funcionarioData.ColorCalendario
            };

            return Ok(citaCreada);
        }

        // ============================
        // CITAS POR DÍA
        // ============================
        [HttpGet]
        public IActionResult GetCitasByDay(string date)
        {
            var fecha = DateTime.Parse(date);

            var citas = _context.Citas
                .Include(c => c.Funcionario)
                .Include(c => c.Servicio) 
                .Where(c => c.FechaHoraCita.Date == fecha.Date)
                .Select(c => new
                {
                    c.Id,
                    c.NombreCliente,
                    c.TelefonoCliente,
                    c.FechaHoraCita,

                    ServicioNombre = c.Servicio.Nombre,
                    DuracionMinutos = c.Servicio.DuracionMinutos,

                    FuncionarioId = c.FuncionarioId,
                    FuncionarioNombre = c.Funcionario.Nombre,
                    ColorCalendario = c.Funcionario.ColorCalendario
                })
                .ToList();

            return Ok(citas);
        }

        // ============================
        // OBTENER POR ID
        // ============================
        [HttpGet("Calendar/GetById/{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.Funcionario)
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
                cita.FuncionarioId
            });
        }

        // ============================
        // EDITAR
        // ============================
        [HttpPut("Calendar/Edit/{id}")]
        public async Task<IActionResult> Edit(int id, [FromBody] CitaCreateVM vm)
        {
            var cita = await _context.Citas.FindAsync(id);

            if (cita == null)
                return NotFound();

            cita.NombreCliente = vm.NombreCliente;
            cita.TelefonoCliente = vm.TelefonoCliente;
            cita.ServicioId = vm.ServicioId;
            cita.FechaHoraCita = vm.FechaHoraCita;
            cita.FuncionarioId = vm.FuncionarioId; // 🔥

            await _context.SaveChangesAsync();

            return Ok();
        }

        // ============================
        // ELIMINAR
        // ============================
        [HttpDelete("Calendar/Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var cita = await _context.Citas.FindAsync(id);

            if (cita == null)
                return NotFound();

            _context.Citas.Remove(cita);
            await _context.SaveChangesAsync();

            return Ok();
        }

        // ============================
        // CONTADOR POR MES
        // ============================
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

        // ============================
        // PRÓXIMAS CITAS
        // ============================
        [HttpGet]
        public async Task<IActionResult> GetUpcomingAppointments(int? funcionarioId)
        {
            var now = DateTime.Now;

            var citas = await _context.Citas
                .Include(c => c.Funcionario)
                .Where(c => c.FechaHoraCita >= now)
                .OrderBy(c => c.FechaHoraCita)
                .ToListAsync();

            if (funcionarioId.HasValue)
            {
                citas = citas
                    .Where(c => c.FuncionarioId == funcionarioId.Value)
                    .ToList();
            }

            return Ok(citas.Select(c => new
            {
                c.Id,
                c.NombreCliente,
                c.TelefonoCliente,
                c.Servicio,
                c.FechaHoraCita,
                Funcionario = new
                {
                    c.FuncionarioId,
                    c.Funcionario.Nombre
                }
            }));
        }

        // ============================
        // SERVICIOS ACTIVOS
        // ============================
        [HttpGet]
        public async Task<IActionResult> GetServiciosActivos()
        {
            var servicios = await _context.Servicios
                .Where(s => s.Activo)
                .Select(s => new {
                    s.Id,
                    s.Nombre,
                    s.DuracionMinutos
                })
                .ToListAsync();

            return Ok(servicios);
        }
    }
}