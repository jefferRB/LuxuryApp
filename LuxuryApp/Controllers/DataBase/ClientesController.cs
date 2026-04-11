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

        public ClientesController(ApplicationDbContext context, RecordatorioService recordatorioService, IWebHostEnvironment webHostEnvironment, EmailService emailSender)
        {
            _context = context;
            _recordatorioService = recordatorioService;
            _webHostEnvironment = webHostEnvironment;
            _emailSender = emailSender;
        }
        public async Task<IActionResult> Index()
        {
            var clientes = await _context.Clientes
                .Select(c => new ClientesModel
                {
                    Nombre = c.Nombre,
                    CorreoElectronico = c.CorreoElectronico,
                    NumeroTelefono = c.NumeroTelefono,
                    FechaCumpleaños = c.FechaCumpleaños,
                    FrecuenciaVisita = c.FrecuenciaVisita,
                    FechaUltimaVisita = c.FechaUltimaVisita
                })
                .ToListAsync();

            var hoy = DateTime.Now.Date;
            var manana = hoy.AddDays(1);

            // FUTURAS (>= mañana)
            var futuras = clientes
                .Where(c => c.ProximaVisita.Date >= manana)
                .OrderBy(c => c.ProximaVisita)
                .ToList();

            // PASADAS (< mañana)
            var pasadas = clientes
                .Where(c => c.ProximaVisita.Date < manana)
                .OrderBy(c => c.ProximaVisita)
                .ToList();

            // Unir: primero futuras, luego pasadas
            var resultado = futuras.Concat(pasadas).ToList();

            return View(resultado);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var cliente = new ClientesModel
            {
                FechaUltimaVisita = DateTime.Today
            };

            return View(cliente);
        }

        // ✅ GUARDAR CLIENTE NUEVO
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
            bool telefonoExiste = await _context.Clientes
                .AnyAsync(c => c.NumeroTelefono == cliente.NumeroTelefono);

            if (telefonoExiste)
            {
                ModelState.AddModelError("NumeroTelefono",
                    "Este número de teléfono ya se encuentra registrado.");
            }
            if (ModelState.IsValid)
            {
                var primeraVisita = new ClienteVisitas
                {
                    NumeroTelefono = cliente.NumeroTelefono,
                    FechaVisita = cliente.FechaUltimaVisita
                };

                var executionStrategy = _context.Database.CreateExecutionStrategy();
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    _context.Clientes.Add(cliente);
                    _context.ClienteVisitas.Add(primeraVisita);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                });


                return RedirectToAction(nameof(Index));
            }

            return View(cliente);
        }
        // ✅ BUSCAR CLIENTE POR NÚMERO DE TELÉFONO (GET)
        [HttpGet, HttpPost]
        public IActionResult Buscar(string criterio)
        {
            if (string.IsNullOrWhiteSpace(criterio))
            {
                return View(new BuscarClienteViewModel());
            }

            var clientes = _context.Clientes
                .Where(c =>
                    c.NumeroTelefono == criterio ||
                    EF.Functions.Like(c.Nombre, $"%{criterio}%"))
                .ToList();

            if (!clientes.Any())
            {
                ViewBag.Mensaje = $"No se encontraron clientes con el criterio: {criterio}.";
                return View(new BuscarClienteViewModel());
            }

            var vm = new BuscarClienteViewModel
            {
                ClientesEncontrados = clientes
            };

            // 🟢 Si solo hay uno, se selecciona automáticamente
            if (clientes.Count == 1)
            {
                var cliente = clientes.First();

                var historial = _context.ClienteVisitas
                    .Where(v => v.NumeroTelefono == cliente.NumeroTelefono)
                    .OrderByDescending(v => v.FechaVisita)
                    .ToList();

                vm.ClienteSeleccionado = cliente;
                vm.TotalVisitas = historial.Count;
                ViewBag.HistorialVisitas = historial;
            }

            return View(vm);
        }
        [HttpGet]
        public IActionResult Editar(string numeroTelefono)
        {
            if (string.IsNullOrEmpty(numeroTelefono))
                return NotFound();

            var cliente = _context.Clientes.FirstOrDefault(c => c.NumeroTelefono == numeroTelefono);

            if (cliente == null)
                return NotFound();

            return View(cliente);
        }

        // ✅ GUARDAR CAMBIOS DEL CLIENTE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Editar(
            [Bind(
                nameof(ClientesModel.NumeroTelefono) + "," +
                nameof(ClientesModel.CorreoElectronico) + "," +
                nameof(ClientesModel.Nombre) + "," +
                nameof(ClientesModel.FrecuenciaVisita) + "," +
                nameof(ClientesModel.FechaUltimaVisita) + "," +
                nameof(ClientesModel.FechaCumpleaños))]
            ClientesModel cliente)
        {
            if (!ModelState.IsValid)
            {
                return View(cliente);
            }

            var clienteExistente = _context.Clientes.FirstOrDefault(c => c.NumeroTelefono == cliente.NumeroTelefono);

            if (clienteExistente == null)
            {
                return NotFound();
            }

            // 🟡 Detectar si hubo cambio en la fecha de última visita
            bool cambioFechaVisita = clienteExistente.FechaUltimaVisita != cliente.FechaUltimaVisita;

            // 🔒 No se cambia el número de teléfono
            clienteExistente.Nombre = cliente.Nombre;
            clienteExistente.CorreoElectronico = cliente.CorreoElectronico;
            clienteExistente.FechaCumpleaños = cliente.FechaCumpleaños;
            clienteExistente.FrecuenciaVisita = cliente.FrecuenciaVisita;
            clienteExistente.FechaUltimaVisita = cliente.FechaUltimaVisita;

            // 🟢 PASO 2: Registrar visita en historial
            if (cambioFechaVisita)
            {
                var nuevaVisita = new ClienteVisitas
                {
                    NumeroTelefono = cliente.NumeroTelefono,
                    FechaVisita = cliente.FechaUltimaVisita
                };

                _context.ClienteVisitas.Add(nuevaVisita);
            }

            _context.SaveChanges();

            // Mensaje de éxito
            TempData["Mensaje"] = "Cliente editado con éxito.";

            return RedirectToAction("Buscar", new { criterio = cliente.NumeroTelefono });
        }
        [HttpPost]
        public async Task<IActionResult> Eliminar(string numeroTelefono)
        {
            if (string.IsNullOrEmpty(numeroTelefono))
                return BadRequest("Número de teléfono no válido.");

            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.NumeroTelefono == numeroTelefono);

            if (cliente == null)
                return NotFound();

            _context.Clientes.Remove(cliente);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Cliente eliminado correctamente.";
            return RedirectToAction("Buscar");
        }

        [HttpPost]
        
        public async Task<IActionResult> EnviarMensaje(string numeroTelefono)
        {
            try
            {
                //recordatorios
                var usuarios = await _recordatorioService.ObtenerUsuariosProximos();
                var ruta = Path.Combine(_webHostEnvironment.WebRootPath, "Plantillas", "MensajeRecordatorio.html");
                var contenidoHtml = System.IO.File.ReadAllText(ruta);
                await _emailSender.SendBulkEmailsAsync(usuarios, "Recordatorio", contenidoHtml);

                //cumpleaños

                var cumpleaneros = await _recordatorioService.ObtenerCumpleañerosHoy();
                var rutaCumple = Path.Combine(_webHostEnvironment.WebRootPath, "Plantillas", "MensajeCumpleaños.html");
                var contenidoHtmlCumple = System.IO.File.ReadAllText(rutaCumple);
                await _emailSender.SendBulkEmailsAsync(cumpleaneros, "Feliz cumpleaños", contenidoHtmlCumple);

                return Json(new { success = true, message = "Mensajes enviados correctamente." });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return Json(new { success = false });

            }
        }
        //registro de imagenes y descripcion de servicios realizados
        [HttpGet]
        public IActionResult RegistrarServicios(string numeroTelefono)
        {
            if (string.IsNullOrEmpty(numeroTelefono))
                return NotFound();

            var cliente = _context.Clientes.FirstOrDefault(c => c.NumeroTelefono == numeroTelefono);
            var imagenes = _context.ClienteImagenes
                .Where(i => i.NumeroTelefono == numeroTelefono)
                .ToList();

            var model = new ServicioRealizadoViewModel
            {
                NumeroTelefono = numeroTelefono,
                ImagenesGuardadas = imagenes,
                DescripcionServicios = cliente?.DescripcionServiciosRealizados // ← AQUI SE CARGA
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult AgregarVisitaRapida(string numeroTelefono)
        {
            if (string.IsNullOrEmpty(numeroTelefono))
                return BadRequest();

            var cliente = _context.Clientes
                .FirstOrDefault(c => c.NumeroTelefono == numeroTelefono);

            if (cliente == null)
                return NotFound();

            var hoy = DateTime.Today;

            // 🟢 Actualizar última visita
            cliente.FechaUltimaVisita = hoy;

            // 🟢 Registrar visita en historial
            var visita = new ClienteVisitas
            {
                NumeroTelefono = numeroTelefono,
                FechaVisita = hoy
            };

            _context.ClienteVisitas.Add(visita);

            _context.SaveChanges();

            TempData["Mensaje"] = "Visita registrada correctamente.";

            // 🔁 Volver al cliente
            return RedirectToAction("Buscar", new { criterio = numeroTelefono });
        }


        [HttpPost]
        public async Task<IActionResult> RegistrarServicios(ServicioRealizadoViewModel model)
        {
            var cliente = _context.Clientes.FirstOrDefault(c => c.NumeroTelefono == model.NumeroTelefono);
            if (cliente == null)
            {
                return NotFound();
            }

            // Actualizar descripción general del cliente
            cliente.DescripcionServiciosRealizados = model.DescripcionServicios;

            // Guardar imágenes si existen
            if (model.Imagenes != null && model.Imagenes.Count > 0)
            {
                foreach (var img in model.Imagenes)
                {
                    using var ms = new MemoryStream();
                    await img.CopyToAsync(ms);

                    var registro = new ClienteImagenesModel
                    {
                        NumeroTelefono = model.NumeroTelefono,
                        Imagen = ms.ToArray(),
                        Fecha = DateTime.Now
                    };

                    _context.ClienteImagenes.Add(registro);
                }
            }

            await _context.SaveChangesAsync();


            return RedirectToAction("Buscar", new { criterio = model.NumeroTelefono });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarVisita(string numeroTelefono, DateTime fechaVisita)
        {
            var visita = _context.ClienteVisitas.FirstOrDefault(v =>
                v.NumeroTelefono == numeroTelefono &&
                v.FechaVisita.Date == fechaVisita.Date);

            if (visita == null)
                return NotFound();

            _context.ClienteVisitas.Remove(visita);
            _context.SaveChanges();

            TempData["Mensaje"] = "Visita eliminada correctamente.";

            return RedirectToAction("Buscar", new { criterio = numeroTelefono });
        }

        [HttpGet]
        public async Task<IActionResult> Autocompletado(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Ok(new List<object>());

            var clientes = await _context.Clientes
                .Where(c => EF.Functions.Like(c.Nombre, $"%{term}%"))
                .OrderBy(c => c.Nombre)
                .Take(10)
                .Select(c => new
                {
                    nombre = c.Nombre,
                    telefono = c.NumeroTelefono
                })
                .ToListAsync();

            return Ok(clientes);
        }

    }
}
