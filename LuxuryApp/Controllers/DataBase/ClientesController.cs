using LuxuryApp.Models.DataBase;
using LuxuryApp.Services.DataBase;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.DataBase
{
    [Authorize(Roles = "Administrador")]
    public class ClientesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly RecordatorioService _recordatorioService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly EmailService _emailSender;
        private readonly ILogger<ClientesController> _logger;

        public ClientesController(
            ApplicationDbContext context,
            RecordatorioService recordatorioService,
            IWebHostEnvironment webHostEnvironment,
            EmailService emailSender,
            ILogger<ClientesController> logger)
        {
            _context = context;
            _recordatorioService = recordatorioService;
            _webHostEnvironment = webHostEnvironment;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var clientes = await _context.Clientes
                .AsNoTracking()
                .Select(c => new ClientesModel
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    CorreoElectronico = c.CorreoElectronico,
                    NumeroTelefono = c.NumeroTelefono,
                    FechaCumpleaños = c.FechaCumpleaños,
                    FrecuenciaVisita = c.FrecuenciaVisita,
                    FechaUltimaVisita = c.FechaUltimaVisita
                })
                .ToListAsync();

            return View(clientes
    .OrderBy(c => c.Nombre)
    .ToList());
        }

        [HttpGet]
        public IActionResult Create()
        {
            var cliente = new ClientesModel
            {
                FechaUltimaVisita = DateTime.Today,
                FrecuenciaVisita = 30
            };

            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind(
                nameof(ClientesModel.NumeroTelefono) + "," +
                nameof(ClientesModel.CorreoElectronico) + "," +
                nameof(ClientesModel.Nombre) + "," +
                nameof(ClientesModel.FrecuenciaVisita) + "," +
                nameof(ClientesModel.FechaUltimaVisita) + "," +
                nameof(ClientesModel.FechaCumpleaños))]
            ClientesModel cliente)
        {
            NormalizeCliente(cliente);

            await ValidateTelefonoDisponibleAsync(cliente.NumeroTelefono);

            if (!ModelState.IsValid)
            {
                return View(cliente);
            }

            try
            {
                var executionStrategy = _context.Database.CreateExecutionStrategy();
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    _context.Clientes.Add(cliente);
                    await _context.SaveChangesAsync();

                    _context.ClienteVisitas.Add(new ClienteVisitas
                    {
                        ClienteId = cliente.Id,
                        NumeroTelefono = cliente.NumeroTelefono,
                        FechaVisita = cliente.FechaUltimaVisita
                    });

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                TempData["Mensaje"] = "Cliente creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al crear cliente para telefono {NumeroTelefono}.", cliente.NumeroTelefono);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el cliente. Revisa los datos e inténtalo de nuevo.");
                return View(cliente);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueó la creación del cliente {NumeroTelefono}.", cliente.NumeroTelefono);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el cliente por una validación de seguridad o consistencia.");
                return View(cliente);
            }
        }

        [HttpGet, HttpPost]
        public async Task<IActionResult> Buscar(string criterio)
        {
            if (string.IsNullOrWhiteSpace(criterio))
            {
                return View(new BuscarClienteViewModel());
            }

            criterio = criterio.Trim();

            var clientes = await _context.Clientes
                .AsNoTracking()
                .Where(c =>
                    c.NumeroTelefono == criterio ||
                    EF.Functions.Like(c.Nombre, $"%{criterio}%"))
                .OrderBy(c => c.Nombre)
                .ToListAsync();

            if (!clientes.Any())
            {
                ViewBag.Mensaje = $"No se encontraron clientes con el criterio: {criterio}.";
                return View(new BuscarClienteViewModel());
            }

            var vm = new BuscarClienteViewModel
            {
                ClientesEncontrados = clientes
            };

            if (clientes.Count == 1)
            {
                var cliente = clientes[0];

                var historial = await _context.ClienteVisitas
                    .AsNoTracking()
                    .Where(v => v.ClienteId == cliente.Id)
                    .OrderByDescending(v => v.FechaVisita)
                    .ToListAsync();

                vm.ClienteSeleccionado = cliente;
                vm.TotalVisitas = historial.Count;
                ViewBag.HistorialVisitas = historial;
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cliente == null)
            {
                return NotFound();
            }

            return View(cliente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(
            [Bind(
                nameof(ClientesModel.Id) + "," +
                nameof(ClientesModel.NumeroTelefono) + "," +
                nameof(ClientesModel.CorreoElectronico) + "," +
                nameof(ClientesModel.Nombre) + "," +
                nameof(ClientesModel.FrecuenciaVisita) + "," +
                nameof(ClientesModel.FechaUltimaVisita) + "," +
                nameof(ClientesModel.FechaCumpleaños))]
            ClientesModel cliente)
        {
            NormalizeCliente(cliente);

            await ValidateTelefonoDisponibleAsync(cliente.NumeroTelefono, cliente.Id);

            if (!ModelState.IsValid)
            {
                return View(cliente);
            }

            var clienteExistente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == cliente.Id);

            if (clienteExistente == null)
            {
                return NotFound();
            }

            var telefonoAnterior = clienteExistente.NumeroTelefono;
            var cambioTelefono = !string.Equals(
                telefonoAnterior,
                cliente.NumeroTelefono,
                StringComparison.Ordinal);
            var cambioFechaVisita = clienteExistente.FechaUltimaVisita != cliente.FechaUltimaVisita;

            clienteExistente.Nombre = cliente.Nombre;
            clienteExistente.NumeroTelefono = cliente.NumeroTelefono;
            clienteExistente.CorreoElectronico = cliente.CorreoElectronico;
            clienteExistente.FechaCumpleaños = cliente.FechaCumpleaños;
            clienteExistente.FrecuenciaVisita = cliente.FrecuenciaVisita;
            clienteExistente.FechaUltimaVisita = cliente.FechaUltimaVisita;

            if (cambioTelefono)
            {
                var visitas = await _context.ClienteVisitas
                    .Where(v => v.ClienteId == clienteExistente.Id)
                    .ToListAsync();

                foreach (var visita in visitas)
                {
                    visita.NumeroTelefono = cliente.NumeroTelefono;
                }

                var imagenes = await _context.ClienteImagenes
                    .Where(i => i.ClienteId == clienteExistente.Id)
                    .ToListAsync();

                foreach (var imagen in imagenes)
                {
                    imagen.NumeroTelefono = cliente.NumeroTelefono;
                }
            }

            if (cambioFechaVisita)
            {
                _context.ClienteVisitas.Add(new ClienteVisitas
                {
                    ClienteId = clienteExistente.Id,
                    NumeroTelefono = cliente.NumeroTelefono,
                    FechaVisita = cliente.FechaUltimaVisita
                });
            }

            try
            {
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = "Cliente editado con éxito.";
                return RedirectToAction(nameof(Buscar), new { criterio = clienteExistente.NumeroTelefono });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al editar cliente {ClienteId}.", cliente.Id);
                ModelState.AddModelError(string.Empty, "No fue posible guardar los cambios del cliente.");
                return View(cliente);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueó la edición del cliente {ClienteId}.", cliente.Id);
                ModelState.AddModelError(string.Empty, "No fue posible guardar los cambios por una validación de seguridad o consistencia.");
                return View(cliente);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            if (id <= 0)
            {
                return BadRequest("Cliente no válido.");
            }

            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cliente == null)
            {
                return NotFound();
            }

            try
            {
                _context.Clientes.Remove(cliente);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Cliente eliminado correctamente.";
                return RedirectToAction(nameof(Buscar));
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar cliente {ClienteId}.", id);
                TempData["Error"] = "No fue posible eliminar el cliente porque tiene datos relacionados.";
                return RedirectToAction(nameof(Buscar), new { criterio = cliente.NumeroTelefono });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EnviarMensaje(string numeroTelefono)
        {
            try
            {
                var usuarios = await _recordatorioService.ObtenerUsuariosProximos();
                var ruta = Path.Combine(_webHostEnvironment.WebRootPath, "Plantillas", "MensajeRecordatorio.html");
                var contenidoHtml = System.IO.File.ReadAllText(ruta);
                await _emailSender.SendBulkEmailsAsync(usuarios, "Recordatorio", contenidoHtml);

                var cumpleaneros = await _recordatorioService.ObtenerCumpleañerosHoy();
                var rutaCumple = Path.Combine(_webHostEnvironment.WebRootPath, "Plantillas", "MensajeCumpleaños.html");
                var contenidoHtmlCumple = System.IO.File.ReadAllText(rutaCumple);
                await _emailSender.SendBulkEmailsAsync(cumpleaneros, "Feliz cumpleaños", contenidoHtmlCumple);

                return Json(new { success = true, message = "Mensajes enviados correctamente." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar mensajes masivos para clientes.");
                return Json(new { success = false });
            }
        }

        [HttpGet]
        public async Task<IActionResult> RegistrarServicios(int id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cliente == null)
            {
                return NotFound();
            }

            var imagenes = await _context.ClienteImagenes
                .AsNoTracking()
                .Where(i => i.ClienteId == id)
                .OrderByDescending(i => i.Fecha)
                .ToListAsync();

            var model = new ServicioRealizadoViewModel
            {
                ClienteId = cliente.Id,
                NumeroTelefono = cliente.NumeroTelefono,
                ImagenesGuardadas = imagenes,
                DescripcionServicios = cliente.DescripcionServiciosRealizados
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarVisitaRapida(int id)
        {
            if (id <= 0)
            {
                return BadRequest();
            }

            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cliente == null)
            {
                return NotFound();
            }

            var hoy = DateTime.Today;

            cliente.FechaUltimaVisita = hoy;

            _context.ClienteVisitas.Add(new ClienteVisitas
            {
                ClienteId = cliente.Id,
                NumeroTelefono = cliente.NumeroTelefono,
                FechaVisita = hoy
            });

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Visita registrada correctamente.";
            return RedirectToAction(nameof(Buscar), new { criterio = cliente.NumeroTelefono });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarServicios(ServicioRealizadoViewModel model)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == model.ClienteId);

            if (cliente == null)
            {
                return NotFound();
            }

            cliente.DescripcionServiciosRealizados = NormalizeOptionalString(model.DescripcionServicios);

            if (model.Imagenes != null && model.Imagenes.Count > 0)
            {
                foreach (var img in model.Imagenes)
                {
                    using var ms = new MemoryStream();
                    await img.CopyToAsync(ms);

                    _context.ClienteImagenes.Add(new ClienteImagenesModel
                    {
                        ClienteId = cliente.Id,
                        NumeroTelefono = cliente.NumeroTelefono,
                        Imagen = ms.ToArray(),
                        Fecha = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Registro de servicios actualizado correctamente.";
            return RedirectToAction(nameof(Buscar), new { criterio = cliente.NumeroTelefono });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarVisita(int id)
        {
            var visita = await _context.ClienteVisitas
                .Include(v => v.Cliente)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (visita == null)
            {
                return NotFound();
            }

            _context.ClienteVisitas.Remove(visita);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Visita eliminada correctamente.";
            var criterio = visita.Cliente?.NumeroTelefono ?? visita.NumeroTelefono;
            return RedirectToAction(nameof(Buscar), new { criterio });
        }

        [HttpGet]
        public async Task<IActionResult> Autocompletado(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Ok(new List<object>());
            }

            var clientes = await _context.Clientes
                .AsNoTracking()
                .Where(c => EF.Functions.Like(c.Nombre, $"%{term}%"))
                .OrderBy(c => c.Nombre)
                .Take(10)
                .Select(c => new
                {
                    id = c.Id,
                    nombre = c.Nombre,
                    telefono = c.NumeroTelefono
                })
                .ToListAsync();

            return Ok(clientes);
        }

        private async Task ValidateTelefonoDisponibleAsync(string numeroTelefono, int? clienteIdActual = null)
        {
            var telefono = NormalizeRequiredString(numeroTelefono);
            if (string.IsNullOrWhiteSpace(telefono))
            {
                return;
            }

            var query = _context.Clientes
                .AsNoTracking()
                .Where(c => c.NumeroTelefono == telefono);

            if (clienteIdActual.HasValue)
            {
                query = query.Where(c => c.Id != clienteIdActual.Value);
            }

            if (await query.AnyAsync())
            {
                ModelState.AddModelError(
                    nameof(ClientesModel.NumeroTelefono),
                    "Este número de teléfono ya se encuentra registrado.");
            }
        }

        private static void NormalizeCliente(ClientesModel cliente)
        {
            cliente.Nombre = NormalizeRequiredString(cliente.Nombre);
            cliente.NumeroTelefono = NormalizeRequiredString(cliente.NumeroTelefono);
            cliente.CorreoElectronico = NormalizeOptionalString(cliente.CorreoElectronico);
        }

        private static string NormalizeRequiredString(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static string? NormalizeOptionalString(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
