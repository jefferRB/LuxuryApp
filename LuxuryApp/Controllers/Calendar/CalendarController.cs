using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Finanzas;
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

        
        // CREAR CITA
        
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CitaCreateVM vm)
        {
            if (vm == null)
                return BadRequest("Datos inválidos");

            var inicio = vm.FechaHoraCita;

            int duracion = 30;
            Servicio? servicio = null;

            // 🔥 SI ES DESCANSO
            if (vm.Tipo == "DESCANSO")
            {
                duracion = vm.DuracionMinutos ?? 30;
            }
            else
            {
                // 🔥 SI ES CITA NORMAL
                servicio = await _context.Servicios
                    .FirstOrDefaultAsync(s => s.Id == vm.ServicioId && s.Activo);

                if (servicio == null)
                    return BadRequest("Servicio inválido");

                duracion = servicio.DuracionMinutos ?? 30;
            }

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

                int cDuracion =
                    c.Tipo == "DESCANSO"
                    ? c.DuracionMinutos ?? 30
                    : c.Servicio?.DuracionMinutos ?? 30;

                var cFin = cInicio.AddMinutes(cDuracion);

                if (inicio < cFin && fin > cInicio)
                    return BadRequest("Ya existe una cita o descanso en ese horario");
            }

            // 4️⃣ Crear cita
            var cita = new Cita
            {
                NombreCliente = vm.Tipo == "DESCANSO" ? "DESCANSO" : vm.NombreCliente,
                TelefonoCliente = vm.TelefonoCliente,

                ServicioId = vm.Tipo == "DESCANSO" ? null : servicio!.Id,

                FechaHoraCita = inicio,
                FuncionarioId = funcionarioData.IdFuncionario,

                Tipo = vm.Tipo,

                DuracionMinutos = vm.Tipo == "DESCANSO"
        ? vm.DuracionMinutos
        : null,

                ConfirmacionEnviada = false,
                Recordatorio24hEnviado = false,
                Recordatorio3hEnviado = false
            };
            _context.Citas.Add(cita);
            await _context.SaveChangesAsync();

            // DUPLICAR CITAS
            if (vm.Duplicar && vm.FechasDuplicadas != null)
            {
                foreach (var fecha in vm.FechasDuplicadas)
                {
                    var nuevaFecha = DateTime.Parse(fecha)
                    .AddHours(inicio.Hour)
                    .AddMinutes(inicio.Minute);

                    var nuevaCita = new Cita
                    {
                        NombreCliente = vm.NombreCliente,
                        TelefonoCliente = vm.TelefonoCliente,
                        ServicioId = servicio.Id,
                        FuncionarioId = funcionarioData.IdFuncionario,
                        FechaHoraCita = nuevaFecha
                    };

                    _context.Citas.Add(nuevaCita);
                }

                await _context.SaveChangesAsync();
            }

            // 5️⃣ WhatsApp

            if (cita.Tipo == "CITA")
            {
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
                    c.Tipo,

                    c.NombreCliente,
                    c.TelefonoCliente,
                    c.FechaHoraCita,

                    ServicioNombre = c.Servicio != null
                        ? c.Servicio.Nombre
                        : null,

                    DuracionMinutos = c.Tipo == "DESCANSO"
                        ? c.DuracionMinutos
                        : c.Servicio.DuracionMinutos,

                    FuncionarioId = c.FuncionarioId,
                    FuncionarioNombre = c.Funcionario.Nombre,
                    ColorCalendario = c.Funcionario.ColorCalendario
                })
                .ToList();

            return Ok(citas);
        }



        [HttpGet("Calendar/GetById/{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var cita = await _context.Citas
                .Include(c => c.Servicio)     
                .Include(c => c.Funcionario)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            return Ok(new
            {
                id = cita.Id,
                nombreCliente = cita.NombreCliente,
                telefonoCliente = cita.TelefonoCliente,
                servicioId = cita.ServicioId,          
                servicioNombre = cita.Servicio.Nombre, 
                fechaHoraCita = cita.FechaHoraCita,
                funcionarioId = cita.FuncionarioId
            });
        }

       
        [HttpPut("Calendar/Edit/{id}")]
        public async Task<IActionResult> Edit(int id, [FromBody] CitaCreateVM vm)
        {
            var cita = await _context.Citas
                .Include(c => c.Servicio)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cita == null)
                return NotFound();

            var servicio = await _context.Servicios
                .FirstOrDefaultAsync(s => s.Id == vm.ServicioId && s.Activo);

            if (servicio == null)
                return BadRequest("Servicio inválido");

            var duracion = servicio.DuracionMinutos ?? 30;

            var inicio = vm.FechaHoraCita;
            var fin = inicio.AddMinutes(duracion);

            // Validar choques (excepto esta misma cita)
            var citasExistentes = await _context.Citas
                .Include(c => c.Servicio)
                .Where(c => c.FuncionarioId == vm.FuncionarioId && c.Id != id)
                .ToListAsync();

            foreach (var c in citasExistentes)
            {
                var cInicio = c.FechaHoraCita;
                var cFin = cInicio.AddMinutes(c.Servicio?.DuracionMinutos ?? 30);

                if (inicio < cFin && fin > cInicio)
                    return BadRequest("Ya existe una cita en ese horario");
            }

            // Actualizar
            cita.NombreCliente = vm.NombreCliente;
            cita.TelefonoCliente = vm.TelefonoCliente;
            cita.ServicioId = servicio.Id;
            cita.FechaHoraCita = inicio;
            cita.FuncionarioId = vm.FuncionarioId;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = cita.Id,
                nombreCliente = cita.NombreCliente,
                telefonoCliente = cita.TelefonoCliente,
                fechaHoraCita = cita.FechaHoraCita,
                duracionMinutos = duracion,
                funcionarioId = cita.FuncionarioId,
                servicioNombre = servicio.Nombre
            });
        }

        
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

       
        [HttpGet]
        public IActionResult GetCitasCountByMonth(int year, int month)
        {
            var data = _context.Citas
                  .Where(c => c.Tipo == "CITA" &&
                c.FechaHoraCita.Year == year &&
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
        public async Task<IActionResult> GetUpcomingAppointments(int? funcionarioId)
        {
            var now = DateTime.Now;

            var query = _context.Citas
             .Include(c => c.Servicio)
             .Include(c => c.Funcionario)
             .Where(c => c.Tipo == "CITA" && c.FechaHoraCita >= now);

            if (funcionarioId.HasValue && funcionarioId.Value > 0)
            {
                query = query.Where(c => c.FuncionarioId == funcionarioId.Value);
            }

            var citas = await query
                .OrderBy(c => c.FechaHoraCita)
                .Select(c => new
                {
                    id = c.Id,
                    nombreCliente = c.NombreCliente,
                    telefonoCliente = c.TelefonoCliente,
                    fechaHoraCita = c.FechaHoraCita,
                    servicioNombre = c.Servicio.Nombre,        
                    funcionarioNombre = c.Funcionario.Nombre  
                })
                .ToListAsync();

            return Ok(citas);
        }

        
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

        [HttpPost("Calendar/ProcesarVisitas")]
        public async Task<IActionResult> ProcesarVisitas()
        {
            var servicio = HttpContext.RequestServices
                .GetRequiredService<VisitasAutomaticasService>();

            await servicio.ProcesarCitasFinalizadas();

            return Ok(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetFechasOcupadas(int funcionarioId)
        {
            var citas = await _context.Citas
            .Include(c => c.Servicio)
            .Where(c => c.FuncionarioId == funcionarioId)
            .Select(c => new
            {
                fecha = c.FechaHoraCita.ToString("yyyy-MM-dd"),
                hora = c.FechaHoraCita.ToString("HH:mm"),
                duracion = c.Tipo == "DESCANSO"
                    ? c.DuracionMinutos ?? 30
                    : c.Servicio.DuracionMinutos ?? 30,
                funcionarioId = c.FuncionarioId
            })
            .ToListAsync();

            return Ok(citas);
        }

    }
}