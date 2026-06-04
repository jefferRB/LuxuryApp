using System.Linq.Expressions;
using System.Security.Claims;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.WhatsApp;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.DataBase
{
    [Authorize(Roles = "Administrador")]
    public class ClientesController : Controller
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;
        private const int BuscarNombreMaxResults = 50;
        private const int AutocompleteMinLength = 3;
        private const int AutocompleteMaxResults = 10;
        private const string TenantWhatsAppEnabledViewDataKey = "TenantWhatsAppEnabled";

        private static readonly Expression<Func<ClientesModel, ClienteSummaryViewModel>> ClienteSummaryProjection = cliente =>
            new ClienteSummaryViewModel
            {
                Id = cliente.Id,
                Nombre = cliente.Nombre,
                CorreoElectronico = cliente.CorreoElectronico,
                NumeroTelefono = cliente.NumeroTelefono,
                FechaCumpleanos = cliente.FechaCumpleaños,
                FrecuenciaVisita = cliente.FrecuenciaVisita,
                FechaUltimaVisita = cliente.FechaUltimaVisita
            };

        private static readonly Expression<Func<ClienteVisitas, ClienteVisitaItemViewModel>> ClienteVisitaProjection = visita =>
            new ClienteVisitaItemViewModel
            {
                Id = visita.Id,
                FechaVisita = visita.FechaVisita
            };

        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantWhatsAppFeatureService _tenantWhatsAppFeatureService;
        private readonly ILogger<ClientesController> _logger;

        public ClientesController(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantWhatsAppFeatureService tenantWhatsAppFeatureService,
            ILogger<ClientesController> logger)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
            _tenantWhatsAppFeatureService = tenantWhatsAppFeatureService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = DefaultPageSize)
        {
            var normalizedPageSize = NormalizePageSize(pageSize);
            var clientesQuery = _context.Clientes.AsNoTracking();
            var totalCount = await clientesQuery.CountAsync();
            var totalPages = CalculateTotalPages(totalCount, normalizedPageSize);
            var normalizedPageNumber = NormalizePageNumber(pageNumber, totalPages);

            IReadOnlyList<ClienteSummaryViewModel> clientes = totalCount == 0
                ? Array.Empty<ClienteSummaryViewModel>()
                : await clientesQuery
                    .OrderBy(c => c.Nombre)
                    .ThenBy(c => c.Id)
                    .Skip((normalizedPageNumber - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .Select(ClienteSummaryProjection)
                    .ToListAsync();

            return View(new ClientesIndexViewModel
            {
                Clientes = clientes,
                PageNumber = normalizedPageNumber,
                PageSize = normalizedPageSize,
                TotalCount = totalCount
            });
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var cliente = new ClientesModel
            {
                FechaUltimaVisita = _businessDateTimeProvider.Today(),
                FrecuenciaVisita = 30
            };

            await SetTenantWhatsAppEnabledViewDataAsync(cancellationToken);
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
                nameof(ClientesModel.FechaCumpleaños) + "," +
                nameof(ClientesModel.AceptaMensajesWhatsApp))]
            ClientesModel cliente)
        {
            NormalizeCliente(cliente);
            var tenantWhatsAppEnabled = await SetTenantWhatsAppEnabledViewDataAsync();

            if (tenantWhatsAppEnabled)
            {
                ApplyConsentAudit(cliente);
            }
            else
            {
                cliente.AceptaMensajesWhatsApp = false;
            }

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
                ModelState.AddModelError(string.Empty, "No fue posible guardar el cliente. Revisa los datos e intentalo de nuevo.");
                return View(cliente);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo la creacion del cliente {NumeroTelefono}.", cliente.NumeroTelefono);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el cliente por una validacion de seguridad o consistencia.");
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
            var esBusquedaTelefonica = LooksLikePhoneForSearch(criterio);
            var clientesQuery = _context.Clientes.AsNoTracking();

            List<ClienteSummaryViewModel> clientes;
            string? mensaje = null;
            var resultadosLimitados = false;

            if (esBusquedaTelefonica)
            {
                clientes = await ApplyExactPhoneSearch(clientesQuery, criterio)
                    .OrderBy(c => c.Nombre)
                    .ThenBy(c => c.Id)
                    .Select(ClienteSummaryProjection)
                    .ToListAsync();
            }
            else
            {
                clientes = await clientesQuery
                    .Where(c => EF.Functions.Like(c.Nombre, $"%{criterio}%"))
                    .OrderBy(c => c.Nombre)
                    .ThenBy(c => c.Id)
                    .Take(BuscarNombreMaxResults + 1)
                    .Select(ClienteSummaryProjection)
                    .ToListAsync();

                if (clientes.Count > BuscarNombreMaxResults)
                {
                    clientes = clientes.Take(BuscarNombreMaxResults).ToList();
                    resultadosLimitados = true;
                    mensaje = $"Se encontraron muchos clientes. Mostrando los primeros {BuscarNombreMaxResults}; refina la búsqueda.";
                }
            }

            if (clientes.Count == 0)
            {
                return View(new BuscarClienteViewModel
                {
                    Criterio = criterio,
                    EsBusquedaTelefonica = esBusquedaTelefonica,
                    Mensaje = $"No se encontraron clientes con el criterio: {criterio}."
                });
            }

            var model = new BuscarClienteViewModel
            {
                Criterio = criterio,
                EsBusquedaTelefonica = esBusquedaTelefonica,
                ResultadosLimitados = resultadosLimitados,
                Mensaje = mensaje,
                ClientesEncontrados = clientes
            };

            if (clientes.Count == 1)
            {
                var cliente = clientes[0];

                var historial = await _context.ClienteVisitas
                    .AsNoTracking()
                    .Where(v => v.ClienteId == cliente.Id)
                    .OrderByDescending(v => v.FechaVisita)
                    .Select(ClienteVisitaProjection)
                    .ToListAsync();

                model.ClienteSeleccionado = cliente;
                model.TotalVisitas = historial.Count;
                model.HistorialVisitas = historial;
            }

            return View(model);
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
                .Where(c => c.Id == id)
                .Select(c => new ClientesModel
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    NumeroTelefono = c.NumeroTelefono,
                    CorreoElectronico = c.CorreoElectronico,
                    AceptaMensajesWhatsApp = c.AceptaMensajesWhatsApp,
                    WhatsAppConsentUpdatedAtUtc = c.WhatsAppConsentUpdatedAtUtc,
                    WhatsAppConsentSource = c.WhatsAppConsentSource,
                    WhatsAppConsentCapturedByUserId = c.WhatsAppConsentCapturedByUserId,
                    WhatsAppConsentTextVersion = c.WhatsAppConsentTextVersion,
                    FechaCumpleaños = c.FechaCumpleaños,
                    FrecuenciaVisita = c.FrecuenciaVisita,
                    FechaUltimaVisita = c.FechaUltimaVisita
                })
                .FirstOrDefaultAsync();

            if (cliente == null)
            {
                return NotFound();
            }

            await SetTenantWhatsAppEnabledViewDataAsync();
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
                nameof(ClientesModel.FechaCumpleaños) + "," +
                nameof(ClientesModel.AceptaMensajesWhatsApp))]
            ClientesModel cliente)
        {
            NormalizeCliente(cliente);
            var tenantWhatsAppEnabled = await SetTenantWhatsAppEnabledViewDataAsync();

            await ValidateTelefonoDisponibleAsync(cliente.NumeroTelefono, cliente.Id);

            if (!ModelState.IsValid)
            {
                return View(cliente);
            }

            ClientesModel? clienteExistente = null;

            try
            {
                var executionStrategy = _context.Database.CreateExecutionStrategy();
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    clienteExistente = await _context.Clientes
                        .FirstOrDefaultAsync(c => c.Id == cliente.Id);

                    if (clienteExistente == null)
                    {
                        return;
                    }

                    var telefonoAnterior = clienteExistente.NumeroTelefono;
                    var cambioTelefono = !string.Equals(
                        telefonoAnterior,
                        cliente.NumeroTelefono,
                        StringComparison.Ordinal);
                    var cambioFechaVisita = clienteExistente.FechaUltimaVisita != cliente.FechaUltimaVisita;
                    var cambioConsentimiento = tenantWhatsAppEnabled &&
                        clienteExistente.AceptaMensajesWhatsApp != cliente.AceptaMensajesWhatsApp;

                    clienteExistente.Nombre = cliente.Nombre;
                    clienteExistente.NumeroTelefono = cliente.NumeroTelefono;
                    clienteExistente.CorreoElectronico = cliente.CorreoElectronico;
                    if (tenantWhatsAppEnabled)
                    {
                        clienteExistente.AceptaMensajesWhatsApp = cliente.AceptaMensajesWhatsApp;
                    }
                    clienteExistente.FechaCumpleaños = cliente.FechaCumpleaños;
                    clienteExistente.FrecuenciaVisita = cliente.FrecuenciaVisita;
                    clienteExistente.FechaUltimaVisita = cliente.FechaUltimaVisita;

                    if (cambioConsentimiento)
                    {
                        ApplyConsentAudit(clienteExistente);
                    }

                    if (cambioTelefono)
                    {
                        await _context.ClienteVisitas
                            .Where(v => v.ClienteId == clienteExistente.Id)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(v => v.NumeroTelefono, cliente.NumeroTelefono));
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

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                if (clienteExistente == null)
                {
                    return NotFound();
                }

                TempData["Mensaje"] = "Cliente editado con exito.";
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
                _logger.LogError(ex, "Guard bloqueo la edicion del cliente {ClienteId}.", cliente.Id);
                ModelState.AddModelError(string.Empty, "No fue posible guardar los cambios por una validacion de seguridad o consistencia.");
                return View(cliente);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int id)
        {
            if (id <= 0)
            {
                return BadRequest("Cliente no valido.");
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

        [HttpGet]
        public async Task<IActionResult> RegistrarServicios(int id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var model = await BuildRegistrarServiciosViewModelAsync(id);
            if (model == null)
            {
                return NotFound();
            }

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

            ClientesModel? cliente = null;

            try
            {
                var executionStrategy = _context.Database.CreateExecutionStrategy();
                await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();

                    cliente = await _context.Clientes
                        .FirstOrDefaultAsync(c => c.Id == id);

                    if (cliente == null)
                    {
                        return;
                    }

                    var hoy = _businessDateTimeProvider.Today();
                    cliente.FechaUltimaVisita = hoy;

                    _context.ClienteVisitas.Add(new ClienteVisitas
                    {
                        ClienteId = cliente.Id,
                        NumeroTelefono = cliente.NumeroTelefono,
                        FechaVisita = hoy
                    });

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                });

                if (cliente == null)
                {
                    return NotFound();
                }

                TempData["Mensaje"] = "Visita registrada correctamente.";
                return RedirectToAction(nameof(Buscar), new { criterio = cliente.NumeroTelefono });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al agregar visita rapida para cliente {ClienteId}.", id);
                TempData["Error"] = "No fue posible registrar la visita.";
                return RedirectToAction(nameof(Buscar), new { criterio = cliente?.NumeroTelefono });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo la visita rapida del cliente {ClienteId}.", id);
                TempData["Error"] = "No fue posible registrar la visita por una validacion de seguridad o consistencia.";
                return RedirectToAction(nameof(Buscar), new { criterio = cliente?.NumeroTelefono });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> RegistrarServicios(
            [Bind(
                nameof(ServicioRealizadoViewModel.ClienteId) + "," +
                nameof(ServicioRealizadoViewModel.DescripcionServicios))]
            ServicioRealizadoViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildRegistrarServiciosViewModelAsync(model.ClienteId);
                if (invalidModel == null)
                {
                    return NotFound();
                }

                invalidModel.DescripcionServicios = model.DescripcionServicios;
                return View(invalidModel);
            }

            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == model.ClienteId);

            if (cliente == null)
            {
                return NotFound();
            }

            try
            {
                cliente.DescripcionServiciosRealizados = NormalizeOptionalString(model.DescripcionServicios);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Registro de servicios actualizado correctamente.";
                return RedirectToAction(nameof(Buscar), new { criterio = cliente.NumeroTelefono });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al actualizar servicios del cliente {ClienteId}.", model.ClienteId);
                ModelState.AddModelError(string.Empty, "No fue posible actualizar el registro de servicios.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo el registro de servicios del cliente {ClienteId}.", model.ClienteId);
                ModelState.AddModelError(string.Empty, "No fue posible actualizar el registro por una validacion de seguridad o consistencia.");
            }

            var reloadModel = await BuildRegistrarServiciosViewModelAsync(model.ClienteId);
            if (reloadModel == null)
            {
                return NotFound();
            }

            reloadModel.DescripcionServicios = model.DescripcionServicios;
            return View(reloadModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarVisita(int id)
        {
            if (id <= 0)
            {
                return BadRequest();
            }

            var visita = await _context.ClienteVisitas
                .FirstOrDefaultAsync(v => v.Id == id);

            if (visita == null)
            {
                return NotFound();
            }

            try
            {
                _context.ClienteVisitas.Remove(visita);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Visita eliminada correctamente.";
                return RedirectToAction(nameof(Buscar), new { criterio = visita.NumeroTelefono });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar visita {VisitaId}.", id);
                TempData["Error"] = "No fue posible eliminar la visita.";
                return RedirectToAction(nameof(Buscar), new { criterio = visita.NumeroTelefono });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo la eliminacion de la visita {VisitaId}.", id);
                TempData["Error"] = "No fue posible eliminar la visita por una validacion de seguridad o consistencia.";
                return RedirectToAction(nameof(Buscar), new { criterio = visita.NumeroTelefono });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Autocompletado(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Ok(Array.Empty<object>());
            }

            term = term.Trim();

            if (term.Length < AutocompleteMinLength)
            {
                return Ok(Array.Empty<object>());
            }

            var esBusquedaTelefonica = LooksLikePhoneFragment(term);
            var tenantWhatsAppEnabled = await _tenantWhatsAppFeatureService.IsWhatsAppEnabledForCurrentTenantAsync();
            var clientesQuery = _context.Clientes.AsNoTracking();
            var normalizedPhoneTerm = NormalizePhoneForComparison(term);

            clientesQuery = esBusquedaTelefonica
                ? ApplyPhoneAutocompleteSearch(clientesQuery, term)
                    .OrderByDescending(c => c.NumeroTelefono
                        .Replace(" ", string.Empty)
                        .Replace("-", string.Empty)
                        .Replace("(", string.Empty)
                        .Replace(")", string.Empty)
                        .Replace("+", string.Empty)
                        .Replace(".", string.Empty)
                        .Replace("/", string.Empty) == normalizedPhoneTerm)
                    .ThenBy(c => c.Nombre)
                    .ThenBy(c => c.Id)
                : clientesQuery
                    .Where(c => EF.Functions.Like(c.Nombre, $"%{term}%"))
                    .OrderBy(c => c.Nombre)
                    .ThenBy(c => c.Id);

            var clientes = await clientesQuery
                .Take(AutocompleteMaxResults)
                .Select(c => new
                {
                    id = c.Id,
                    nombre = c.Nombre,
                    telefono = c.NumeroTelefono,
                    aceptaMensajesWhatsApp = tenantWhatsAppEnabled && c.AceptaMensajesWhatsApp
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
                    "Este numero de telefono ya se encuentra registrado.");
            }
        }

        private async Task<ServicioRealizadoViewModel?> BuildRegistrarServiciosViewModelAsync(int clienteId)
        {
            return await _context.Clientes
                .AsNoTracking()
                .Where(c => c.Id == clienteId)
                .Select(c => new ServicioRealizadoViewModel
                {
                    ClienteId = c.Id,
                    NumeroTelefono = c.NumeroTelefono,
                    NombreCliente = c.Nombre,
                    DescripcionServicios = c.DescripcionServiciosRealizados
                })
                .FirstOrDefaultAsync();
        }

        private static IQueryable<ClientesModel> ApplyExactPhoneSearch(
            IQueryable<ClientesModel> query,
            string criterio)
        {
            var normalizedPhone = NormalizePhoneForComparison(criterio);

            return query.Where(c =>
                c.NumeroTelefono == criterio ||
                c.NumeroTelefono
                    .Replace(" ", string.Empty)
                    .Replace("-", string.Empty)
                    .Replace("(", string.Empty)
                    .Replace(")", string.Empty)
                    .Replace("+", string.Empty)
                    .Replace(".", string.Empty)
                    .Replace("/", string.Empty) == normalizedPhone);
        }

        private static IQueryable<ClientesModel> ApplyPhoneAutocompleteSearch(
            IQueryable<ClientesModel> query,
            string term)
        {
            var normalizedPhone = NormalizePhoneForComparison(term);

            return query.Where(c =>
                EF.Functions.Like(c.NumeroTelefono, $"{term}%") ||
                EF.Functions.Like(
                    c.NumeroTelefono
                        .Replace(" ", string.Empty)
                        .Replace("-", string.Empty)
                        .Replace("(", string.Empty)
                        .Replace(")", string.Empty)
                        .Replace("+", string.Empty)
                        .Replace(".", string.Empty)
                        .Replace("/", string.Empty),
                    $"{normalizedPhone}%"));
        }

        private static void NormalizeCliente(ClientesModel cliente)
        {
            cliente.Nombre = NormalizeRequiredString(cliente.Nombre);
            cliente.NumeroTelefono = NormalizeRequiredString(cliente.NumeroTelefono);
            cliente.CorreoElectronico = NormalizeOptionalString(cliente.CorreoElectronico);
        }

        private void ApplyConsentAudit(ClientesModel cliente)
        {
            cliente.WhatsAppConsentUpdatedAtUtc = DateTime.UtcNow;
            cliente.WhatsAppConsentSource = WhatsAppConsentSources.ClienteForm;
            cliente.WhatsAppConsentTextVersion = WhatsAppConsentTextVersions.WaOptInV1;
            cliente.WhatsAppConsentCapturedByUserId = ResolveCurrentUserId();
        }

        private string? ResolveCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private async Task<bool> SetTenantWhatsAppEnabledViewDataAsync(CancellationToken cancellationToken = default)
        {
            var isEnabled = await _tenantWhatsAppFeatureService.IsWhatsAppEnabledForCurrentTenantAsync(cancellationToken);
            ViewData[TenantWhatsAppEnabledViewDataKey] = isEnabled;
            return isEnabled;
        }

        private static int NormalizePageNumber(int pageNumber, int totalPages)
        {
            if (pageNumber < 1)
            {
                return 1;
            }

            return Math.Min(pageNumber, totalPages);
        }

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultPageSize;
            }

            return Math.Min(pageSize, MaxPageSize);
        }

        private static int CalculateTotalPages(int totalCount, int pageSize) =>
            totalCount == 0
                ? 1
                : (int)Math.Ceiling(totalCount / (double)pageSize);

        private static bool LooksLikePhoneForSearch(string criterio)
        {
            var digitCount = 0;

            foreach (var character in criterio)
            {
                if (char.IsDigit(character))
                {
                    digitCount++;
                    continue;
                }

                if (char.IsWhiteSpace(character) ||
                    character is '+' or '-' or '(' or ')' or '.' or '/')
                {
                    continue;
                }

                return false;
            }

            return digitCount >= 4;
        }

        private static bool LooksLikePhoneFragment(string criterio)
        {
            var digitCount = 0;

            foreach (var character in criterio)
            {
                if (char.IsDigit(character))
                {
                    digitCount++;
                    continue;
                }

                if (char.IsWhiteSpace(character) ||
                    character is '+' or '-' or '(' or ')' or '.' or '/')
                {
                    continue;
                }

                return false;
            }

            return digitCount >= AutocompleteMinLength;
        }

        private static string NormalizePhoneForComparison(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace("(", string.Empty, StringComparison.Ordinal)
                    .Replace(")", string.Empty, StringComparison.Ordinal)
                    .Replace("+", string.Empty, StringComparison.Ordinal)
                    .Replace(".", string.Empty, StringComparison.Ordinal)
                    .Replace("/", string.Empty, StringComparison.Ordinal);

        private static string NormalizeRequiredString(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static string? NormalizeOptionalString(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
