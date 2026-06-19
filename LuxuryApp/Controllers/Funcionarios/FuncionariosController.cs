using System.Security.Claims;
using ClosedXML.Excel;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Exports;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Funcionarios
{
    [Authorize(Roles = "Administrador")]
    public class FuncionariosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILiquidacionSemanalService _liquidacionSemanalService;
        private readonly IFuncionarioPortalAccessService _portalAccessService;
        private readonly IFuncionarioPortalPermissionService _portalPermissionService;
        private readonly LuxuryApp.Services.Account.IAccountEmailService _accountEmailService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantDisplayNameService _tenantDisplayNameService;
        private readonly ILogger<FuncionariosController> _logger;

        public FuncionariosController(
            ApplicationDbContext context,
            ILiquidacionSemanalService liquidacionSemanalService,
            IFuncionarioPortalAccessService portalAccessService,
            IFuncionarioPortalPermissionService portalPermissionService,
            LuxuryApp.Services.Account.IAccountEmailService accountEmailService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantDisplayNameService tenantDisplayNameService,
            ILogger<FuncionariosController> logger)
        {
            _context = context;
            _liquidacionSemanalService = liquidacionSemanalService;
            _portalAccessService = portalAccessService;
            _portalPermissionService = portalPermissionService;
            _accountEmailService = accountEmailService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _tenantDisplayNameService = tenantDisplayNameService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var funcionarios = await _context.Funcionarios
                .AsNoTracking()
                .OrderBy(f => f.Nombre)
                .Select(f => new
                {
                    f.IdFuncionario,
                    f.Nombre,
                    f.Telefono,
                    NombrePuesto = f.Puesto != null ? f.Puesto.NombrePuesto : string.Empty,
                    f.PorcentajeGanancia,
                    f.PorcentajeProducto,
                    f.ColorCalendario,
                    f.FechaIngreso,
                    f.Activo,
                    f.AppUsuarioId
                })
                .ToListAsync();

            // Estado de acceso: una sola consulta para todas las cuentas vinculadas.
            var userIds = funcionarios
                .Where(f => !string.IsNullOrWhiteSpace(f.AppUsuarioId))
                .Select(f => f.AppUsuarioId!)
                .ToList();

            var estadosCuenta = userIds.Count == 0
                ? new Dictionary<string, bool>()
                : await _context.Users
                    .AsNoTracking()
                    .Where(u => userIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.State })
                    .ToDictionaryAsync(u => u.Id, u => u.State);

            var items = funcionarios
                .Select(f => new FuncionarioIndexItemViewModel
                {
                    IdFuncionario = f.IdFuncionario,
                    Nombre = f.Nombre,
                    Telefono = f.Telefono,
                    NombrePuesto = f.NombrePuesto,
                    PorcentajeGanancia = f.PorcentajeGanancia,
                    PorcentajeProducto = f.PorcentajeProducto,
                    ColorCalendario = f.ColorCalendario,
                    FechaIngreso = f.FechaIngreso,
                    Activo = f.Activo,
                    AccesoEstado = ResolverAccesoEstado(f.AppUsuarioId, estadosCuenta)
                })
                .ToList();

            return View(new FuncionariosIndexViewModel
            {
                Funcionarios = items
            });
        }

        private static FuncionarioAccesoEstado ResolverAccesoEstado(
            string? appUsuarioId,
            IReadOnlyDictionary<string, bool> estadosCuenta)
        {
            if (string.IsNullOrWhiteSpace(appUsuarioId) ||
                !estadosCuenta.TryGetValue(appUsuarioId, out var activo))
            {
                return FuncionarioAccesoEstado.SinAcceso;
            }

            return activo
                ? FuncionarioAccesoEstado.AccesoActivo
                : FuncionarioAccesoEstado.AccesoBloqueado;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await CargarPuestosAsync();

            var funcionario = new Funcionario
            {
                FechaIngreso = _businessDateTimeProvider.Today(),
                Activo = true,
                ColorCalendario = "#000000"
            };

            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind(
                nameof(Funcionario.Nombre) + "," +
                nameof(Funcionario.Telefono) + "," +
                nameof(Funcionario.IdPuesto) + "," +
                nameof(Funcionario.ColorCalendario) + "," +
                nameof(Funcionario.PorcentajeGanancia) + "," +
                nameof(Funcionario.PorcentajeProducto) + "," +
                nameof(Funcionario.RebajarImpuestosAntesDeComision) + "," +
                nameof(Funcionario.FechaIngreso))]
            Funcionario funcionario)
        {
            NormalizeFuncionario(funcionario);

            if (!ModelState.IsValid)
            {
                await CargarPuestosAsync(funcionario.IdPuesto);
                return View(funcionario);
            }

            var capacidad = await ValidateActiveFuncionarioCapacityAsync();
            if (!capacidad.IsAllowed)
            {
                TempData["Error"] = capacidad.Message;
                return RedirectToAction(nameof(Index));
            }

            if (await _context.Funcionarios
                    .AsNoTracking()
                    .AnyAsync(f => f.Nombre == funcionario.Nombre))
            {
                ModelState.AddModelError(nameof(Funcionario.Nombre), "Ya existe un funcionario con ese nombre.");
            }

            await ValidatePuestoAsync(funcionario.IdPuesto);

            if (!ModelState.IsValid)
            {
                await CargarPuestosAsync(funcionario.IdPuesto);
                return View(funcionario);
            }

            funcionario.Activo = true;

            try
            {
                _context.Funcionarios.Add(funcionario);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Funcionario creado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al crear funcionario {Nombre}.", funcionario.Nombre);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el funcionario. Verifica los datos e intentalo de nuevo.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo la creacion del funcionario {Nombre}.", funcionario.Nombre);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el funcionario por una validacion de seguridad o consistencia.");
            }

            await CargarPuestosAsync(funcionario.IdPuesto);
            return View(funcionario);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var funcionario = await _context.Funcionarios
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
            {
                return NotFound();
            }

            await CargarPuestosAsync(funcionario.IdPuesto);
            ViewData["Acceso"] = await _portalAccessService.ObtenerEstadoAsync(id, HttpContext.RequestAborted);
            ViewData["Permisos"] = await _portalPermissionService.ObtenerAsync(id, HttpContext.RequestAborted);
            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPermisos(int id, List<string>? permisos)
        {
            var seleccionados = (permisos ?? new List<string>())
                .Where(FuncionarioPortalPermissions.EsPermisoValido)
                .ToHashSet(StringComparer.Ordinal);

            var valores = FuncionarioPortalPermissions.Todos
                .ToDictionary(p => p, p => seleccionados.Contains(p), StringComparer.Ordinal);

            var ok = await _portalPermissionService.GuardarAsync(id, valores, HttpContext.RequestAborted);
            TempData[ok ? "Mensaje" : "Error"] = ok
                ? "Permisos del portal actualizados."
                : "No fue posible actualizar los permisos. Verifica que el funcionario exista.";

            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            [Bind(
                nameof(Funcionario.IdFuncionario) + "," +
                nameof(Funcionario.Nombre) + "," +
                nameof(Funcionario.Telefono) + "," +
                nameof(Funcionario.IdPuesto) + "," +
                nameof(Funcionario.ColorCalendario) + "," +
                nameof(Funcionario.PorcentajeGanancia) + "," +
                nameof(Funcionario.PorcentajeProducto) + "," +
                nameof(Funcionario.RebajarImpuestosAntesDeComision) + "," +
                nameof(Funcionario.FechaIngreso))]
            Funcionario funcionario)
        {
            NormalizeFuncionario(funcionario);

            if (!ModelState.IsValid)
            {
                await CargarPuestosAsync(funcionario.IdPuesto);
                ViewData["Acceso"] = await _portalAccessService.ObtenerEstadoAsync(funcionario.IdFuncionario, HttpContext.RequestAborted);
                return View(funcionario);
            }

            var funcionarioDb = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == funcionario.IdFuncionario);

            if (funcionarioDb == null)
            {
                return NotFound();
            }

            if (await _context.Funcionarios
                    .AsNoTracking()
                    .AnyAsync(f => f.Nombre == funcionario.Nombre && f.IdFuncionario != funcionario.IdFuncionario))
            {
                ModelState.AddModelError(nameof(Funcionario.Nombre), "Ya existe un funcionario con ese nombre.");
            }

            await ValidatePuestoAsync(funcionario.IdPuesto, funcionarioDb.IdPuesto);

            if (!ModelState.IsValid)
            {
                await CargarPuestosAsync(funcionario.IdPuesto);
                ViewData["Acceso"] = await _portalAccessService.ObtenerEstadoAsync(funcionario.IdFuncionario, HttpContext.RequestAborted);
                return View(funcionario);
            }

            funcionarioDb.Nombre = funcionario.Nombre;
            funcionarioDb.Telefono = funcionario.Telefono;
            funcionarioDb.IdPuesto = funcionario.IdPuesto;
            funcionarioDb.ColorCalendario = funcionario.ColorCalendario;
            funcionarioDb.PorcentajeGanancia = funcionario.PorcentajeGanancia;
            funcionarioDb.PorcentajeProducto = funcionario.PorcentajeProducto;
            funcionarioDb.RebajarImpuestosAntesDeComision = funcionario.RebajarImpuestosAntesDeComision;
            funcionarioDb.FechaIngreso = funcionario.FechaIngreso;

            try
            {
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Funcionario actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al actualizar funcionario {FuncionarioId}.", funcionario.IdFuncionario);
                ModelState.AddModelError(string.Empty, "No fue posible actualizar el funcionario.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo la edicion del funcionario {FuncionarioId}.", funcionario.IdFuncionario);
                ModelState.AddModelError(string.Empty, "No fue posible actualizar el funcionario por una validacion de seguridad o consistencia.");
            }

            await CargarPuestosAsync(funcionario.IdPuesto);
            ViewData["Acceso"] = await _portalAccessService.ObtenerEstadoAsync(funcionario.IdFuncionario, HttpContext.RequestAborted);
            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int idFuncionario)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == idFuncionario);

            if (funcionario == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(funcionario.AppUsuarioId))
            {
                TempData["Error"] = "No se puede eliminar el funcionario porque tiene una cuenta de acceso al portal. Bloquea su acceso desde 'Acceso al sistema' antes de eliminarlo.";
                return RedirectToAction(nameof(Index));
            }

            var tieneCitas = await _context.Citas.AnyAsync(c => c.FuncionarioId == idFuncionario);
            var tieneCobros = await _context.Cobros.AnyAsync(c => c.FuncionarioId == idFuncionario);
            var tienePagos = await _context.PagosFuncionarios.AnyAsync(p => p.FuncionarioId == idFuncionario);
            var tieneLiquidaciones = await _context.LiquidacionesSemanalesDetalle.AnyAsync(d => d.FuncionarioId == idFuncionario);

            if (tieneCitas || tieneCobros || tienePagos || tieneLiquidaciones)
            {
                TempData["Error"] = "No se puede eliminar el funcionario porque tiene citas, cobros, pagos o liquidaciones asociadas. Puedes dejarlo inactivo si ya no trabaja en el negocio.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Funcionarios.Remove(funcionario);
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = "Funcionario eliminado correctamente.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al eliminar funcionario {FuncionarioId}.", idFuncionario);
                TempData["Error"] = "No fue posible eliminar el funcionario porque tiene relaciones activas.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Activar(int id) => SetActivoAsync(id, true);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Desactivar(int id) => SetActivoAsync(id, false);

        // ─────────────────────────── ACCESO AL PORTAL ───────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivarAcceso(
            int id,
            string? email,
            string? modoCredencial,
            string? contrasenaTemporal)
        {
            var modo = string.Equals(modoCredencial, "temporal", StringComparison.OrdinalIgnoreCase)
                ? FuncionarioAccesoCredencialModo.ContrasenaTemporal
                : FuncionarioAccesoCredencialModo.Invitacion;

            var resultado = await _portalAccessService.ActivarAccesoAsync(
                id,
                email ?? string.Empty,
                modo,
                contrasenaTemporal,
                HttpContext.RequestAborted);

            if (!resultado.Exitoso)
            {
                TempData["Error"] = string.Join(" ", resultado.Errores);
                return RedirectToAction(nameof(Edit), new { id });
            }

            await EnviarInvitacionSiCorrespondeAsync(resultado);

            TempData["Mensaje"] = modo == FuncionarioAccesoCredencialModo.Invitacion
                ? "Acceso habilitado. Se envió una invitación al correo del funcionario para definir su contraseña."
                : "Acceso habilitado con contraseña temporal. Compártela de forma segura con el funcionario.";

            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarAcceso(int id)
        {
            var resultado = await _portalAccessService.DesactivarAccesoAsync(id, HttpContext.RequestAborted);
            SetAccesoTempData(resultado, "Acceso del funcionario bloqueado. Ya no podrá iniciar sesión.");
            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReactivarAcceso(int id)
        {
            var resultado = await _portalAccessService.ReactivarAccesoAsync(id, HttpContext.RequestAborted);
            SetAccesoTempData(resultado, "Acceso del funcionario reactivado.");
            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReenviarInvitacionAcceso(int id)
        {
            var resultado = await _portalAccessService.GenerarEnlaceInvitacionAsync(id, HttpContext.RequestAborted);
            if (!resultado.Exitoso)
            {
                TempData["Error"] = string.Join(" ", resultado.Errores);
                return RedirectToAction(nameof(Edit), new { id });
            }

            await EnviarInvitacionSiCorrespondeAsync(resultado);
            TempData["Mensaje"] = "Se reenvió la invitación al correo del funcionario.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarCorreoAcceso(int id, string? email)
        {
            var resultado = await _portalAccessService.CambiarCorreoAsync(id, email ?? string.Empty, HttpContext.RequestAborted);
            SetAccesoTempData(resultado, "Correo de acceso actualizado.");
            return RedirectToAction(nameof(Edit), new { id });
        }

        private void SetAccesoTempData(FuncionarioAccesoResultado resultado, string mensajeExito)
        {
            if (resultado.Exitoso)
            {
                TempData["Mensaje"] = mensajeExito;
            }
            else
            {
                TempData["Error"] = string.Join(" ", resultado.Errores);
            }
        }

        private async Task EnviarInvitacionSiCorrespondeAsync(FuncionarioAccesoResultado resultado)
        {
            if (!resultado.RequiereCorreoInvitacion ||
                string.IsNullOrWhiteSpace(resultado.UserId) ||
                string.IsNullOrWhiteSpace(resultado.EnlaceTokenCodificado) ||
                string.IsNullOrWhiteSpace(resultado.Email))
            {
                return;
            }

            var enlace = Url.Action(
                "ResetPassword",
                "Accounts",
                new { userId = resultado.UserId, token = resultado.EnlaceTokenCodificado },
                Request.Scheme)!;

            try
            {
                var nombreNegocio = await _tenantDisplayNameService.GetCurrentTenantDisplayNameAsync(HttpContext.RequestAborted);
                await _accountEmailService.SendFuncionarioInvitationEmailAsync(
                    resultado.Email,
                    resultado.NombreParaCorreo ?? "Funcionario",
                    enlace,
                    nombreNegocio,
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo enviar la invitación de acceso al funcionario. UserId {UserId}.", resultado.UserId);
                TempData["Error"] = "El acceso se configuró, pero no se pudo enviar el correo de invitación. Usa 'Reenviar invitación' más tarde.";
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetActivos()
        {
            var funcionarios = await _context.Funcionarios
                .AsNoTracking()
                .Where(f => f.Activo)
                .OrderBy(f => f.Nombre)
                .Select(f => new
                {
                    id = f.IdFuncionario,
                    nombre = f.Nombre,
                    colorCalendario = f.ColorCalendario
                })
                .ToListAsync();

            return Json(funcionarios);
        }

        public async Task<IActionResult> PagosSemana(DateTime? fecha)
        {
            var fechaReferencia = fecha?.Date ?? _businessDateTimeProvider.Today();
            var fechaPagoSugerida = _businessDateTimeProvider.Now();

            var resumen = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(
                fechaReferencia,
                HttpContext.RequestAborted);

            return View(new PagosSemanaPageViewModel
            {
                InicioSemana = resumen.InicioSemana,
                FinSemana = resumen.FinSemana,
                FechaPagoSugerida = fechaPagoSugerida,
                MetodosPago = ObtenerMetodosPago(),
                Funcionarios = resumen.Funcionarios,
                TotalGeneradoServicios = resumen.TotalGeneradoServicios,
                TotalGeneradoProductos = resumen.TotalGeneradoProductos,
                TotalGeneradoGeneral = resumen.TotalGeneradoGeneral,
                TotalImpuestosGeneral = resumen.TotalImpuestosGeneral,
                TotalSinImpuestosGeneral = resumen.TotalSinImpuestosGeneral,
                TotalPagadoGeneral = resumen.TotalPagadoGeneral,
                TotalPendienteGeneral = resumen.TotalPendienteGeneral,
                GananciaNegocio = resumen.GananciaNegocio
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPago(
            int funcionarioId,
            decimal monto,
            DateTime inicioSemana,
            DateTime finSemana,
            string? observacion,
            string? metodoPago,
            DateTime? fechaPago)
        {
            if (monto <= 0)
            {
                TempData["Error"] = "El monto a pagar debe ser mayor que cero.";
                return RedirectToAction(nameof(PagosSemana), new { fecha = inicioSemana.ToString("yyyy-MM-dd") });
            }

            var resumenValidacion = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(
                inicioSemana,
                finSemana,
                HttpContext.RequestAborted);

            var funcionarioResumen = resumenValidacion.Funcionarios
                .FirstOrDefault(f => f.FuncionarioId == funcionarioId);

            var montoPendiente = funcionarioResumen?.MontoPendiente ?? 0m;

            if (monto > montoPendiente)
            {
                TempData["Error"] = montoPendiente <= 0
                    ? "Este funcionario no tiene monto pendiente en la semana seleccionada."
                    : "El monto a pagar no puede ser mayor al pendiente del funcionario.";
                return RedirectToAction(nameof(PagosSemana), new { fecha = inicioSemana.ToString("yyyy-MM-dd") });
            }

            try
            {
                await _liquidacionSemanalService.RegistrarPagoAsync(
                    new RegistrarLiquidacionSemanalCommand
                    {
                        SemanaInicio = inicioSemana,
                        SemanaFin = finSemana,
                        FechaPago = fechaPago,
                        MetodoPago = metodoPago ?? string.Empty,
                        Observacion = observacion,
                        CreadoPor = User.FindFirstValue(CustomClaimTypes.UserId) ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
                        Detalles =
                        {
                            new RegistrarLiquidacionSemanalDetalleCommand
                            {
                                FuncionarioId = funcionarioId,
                                MontoPagado = monto
                            }
                        }
                    },
                    HttpContext.RequestAborted);

                TempData["Mensaje"] = "Pago registrado correctamente.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Validacion de negocio al registrar pago semanal para funcionario {FuncionarioId}.", funcionarioId);
                TempData["Error"] = ex.Message;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al registrar pago semanal para funcionario {FuncionarioId}.", funcionarioId);
                TempData["Error"] = "No fue posible registrar el pago semanal por un error de persistencia.";
            }

            return RedirectToAction(nameof(PagosSemana), new { fecha = inicioSemana });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PagarTodaLaSemana(
            DateTime inicioSemana,
            DateTime finSemana,
            string? metodoPago,
            DateTime? fechaPago,
            string? observacion)
        {
            var resumen = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(
                inicioSemana,
                finSemana,
                HttpContext.RequestAborted);

            var detalles = resumen.Funcionarios
                .Where(f => f.MontoPendiente > 0)
                .Select(f => new RegistrarLiquidacionSemanalDetalleCommand
                {
                    FuncionarioId = f.FuncionarioId,
                    MontoPagado = f.MontoPendiente
                })
                .ToList();

            if (detalles.Count == 0)
            {
                TempData["Mensaje"] = "La semana seleccionada no tiene pendientes por liquidar.";
                return RedirectToAction(nameof(PagosSemana), new { fecha = inicioSemana });
            }

            try
            {
                await _liquidacionSemanalService.RegistrarPagoAsync(
                    new RegistrarLiquidacionSemanalCommand
                    {
                        SemanaInicio = inicioSemana,
                        SemanaFin = finSemana,
                        FechaPago = fechaPago,
                        MetodoPago = metodoPago ?? string.Empty,
                        Observacion = string.IsNullOrWhiteSpace(observacion)
                            ? "Pago semanal automatico"
                            : observacion,
                        CreadoPor = User.FindFirstValue(CustomClaimTypes.UserId) ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
                        Detalles = detalles
                    },
                    HttpContext.RequestAborted);

                TempData["Mensaje"] = "Todos los pagos pendientes de la semana fueron liquidados.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Validacion de negocio al liquidar la semana {InicioSemana:yyyy-MM-dd} - {FinSemana:yyyy-MM-dd}.", inicioSemana, finSemana);
                TempData["Error"] = ex.Message;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al liquidar pagos semanales para el rango {InicioSemana:yyyy-MM-dd} - {FinSemana:yyyy-MM-dd}.", inicioSemana, finSemana);
                TempData["Error"] = "No fue posible registrar la liquidacion semanal.";
            }

            return RedirectToAction(nameof(PagosSemana), new { fecha = inicioSemana });
        }

        public async Task<IActionResult> ExportarPagosExcel(DateTime inicioSemana, DateTime finSemana)
        {
            var resumen = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(
                inicioSemana,
                finSemana,
                HttpContext.RequestAborted);

            var generatedAt = _businessDateTimeProvider.Now();
            const string excelCurrencyFormat = "CRC #,##0.00";

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Pagos Funcionarios");

            var colorNegro = XLColor.FromHtml("#1C1C1C");
            var colorDorado = XLColor.FromHtml("#C6A55C");
            var colorGris = XLColor.FromHtml("#F5F5F5");

            var nombreNegocio = await _tenantDisplayNameService.GetCurrentTenantDisplayNameAsync(HttpContext.RequestAborted);

            ws.Range("A1:I1").Merge();
            ws.Cell("A1").Value = nombreNegocio;
            ws.Cell("A1").Style.Font.FontSize = 20;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontColor = colorDorado;
            ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range("A2:I2").Merge();
            ws.Cell("A2").Value = "Reporte de Pagos a Funcionarios";
            ws.Cell("A2").Style.Font.FontSize = 14;
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range("A3:I3").Merge();
            ws.Cell("A3").Value = $"Semana: {inicioSemana:dd/MM/yyyy} - {finSemana:dd/MM/yyyy}";
            ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            const int headerRow = 5;

            ws.Cell(headerRow, 1).Value = "Funcionario";
            ws.Cell(headerRow, 2).Value = "Total Generado";
            ws.Cell(headerRow, 3).Value = "Impuestos";
            ws.Cell(headerRow, 4).Value = "Total Neto";
            ws.Cell(headerRow, 5).Value = "% Servicio";
            ws.Cell(headerRow, 6).Value = "% Producto";
            ws.Cell(headerRow, 7).Value = "Monto Generado";
            ws.Cell(headerRow, 8).Value = "Monto Pagado";
            ws.Cell(headerRow, 9).Value = "Pendiente";

            var header = ws.Range(headerRow, 1, headerRow, 9);
            header.Style.Fill.BackgroundColor = colorNegro;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var fila = headerRow + 1;
            foreach (var funcionario in resumen.Funcionarios)
            {
                ws.Cell(fila, 1).Value = funcionario.Nombre;
                ws.Cell(fila, 2).Value = funcionario.TotalGenerado;
                ws.Cell(fila, 3).Value = funcionario.Impuestos;
                ws.Cell(fila, 4).Value = funcionario.TotalNeto;
                ws.Cell(fila, 5).Value = funcionario.Porcentaje;
                ws.Cell(fila, 6).Value = funcionario.PorcentajeProducto;
                ws.Cell(fila, 7).Value = funcionario.PagoFinal;
                ws.Cell(fila, 8).Value = funcionario.MontoPagado;
                ws.Cell(fila, 9).Value = funcionario.MontoPendiente;

                ws.Range(fila, 2, fila, 4).Style.NumberFormat.Format = "₡ #,##0.00";
                ws.Range(fila, 7, fila, 9).Style.NumberFormat.Format = "₡ #,##0.00";

                ws.Range(fila, 2, fila, 4).Style.NumberFormat.Format = excelCurrencyFormat;
                ws.Range(fila, 7, fila, 9).Style.NumberFormat.Format = excelCurrencyFormat;
                fila++;
            }

            if (fila > headerRow + 1)
            {
                var dataRange = ws.Range(headerRow + 1, 1, fila - 1, 9);
                dataRange.AddConditionalFormat()
                    .WhenIsTrue("MOD(ROW(),2)=0")
                    .Fill.SetBackgroundColor(colorGris);
            }

            ws.Cell(fila, 7).Value = "TOTAL PAGADO";
            ws.Cell(fila, 7).Style.Font.Bold = true;

            ws.Cell(fila, 8).FormulaA1 = $"SUM(H{headerRow + 1}:H{fila - 1})";
            ws.Cell(fila, 8).Style.NumberFormat.Format = "₡ #,##0.00";
            ws.Cell(fila, 8).Style.NumberFormat.Format = excelCurrencyFormat;
            ws.Cell(fila, 8).Style.Font.Bold = true;
            ws.Cell(fila, 8).Style.Font.FontColor = colorDorado;

            fila += 3;

            ws.Cell(fila, 1).Value = "Resumen del Salon";
            ws.Cell(fila, 1).Style.Font.Bold = true;

            fila++;
            ws.Cell(fila, 1).Value = "Total generado servicios";
            ws.Cell(fila, 2).Value = resumen.TotalGeneradoServicios;

            fila++;
            ws.Cell(fila, 1).Value = "Total generado productos";
            ws.Cell(fila, 2).Value = resumen.TotalGeneradoProductos;

            fila++;
            ws.Cell(fila, 1).Value = "Total generado general";
            ws.Cell(fila, 2).Value = resumen.TotalGeneradoGeneral;

            fila++;
            ws.Cell(fila, 1).Value = "Total impuestos";
            ws.Cell(fila, 2).Value = resumen.TotalImpuestosGeneral;

            fila++;
            ws.Cell(fila, 1).Value = "Total sin impuestos";
            ws.Cell(fila, 2).Value = resumen.TotalSinImpuestosGeneral;

            fila++;
            ws.Cell(fila, 1).Value = "Total pagado funcionarios";
            ws.Cell(fila, 2).Value = resumen.TotalPagadoGeneral;

            fila++;
            ws.Cell(fila, 1).Value = "Total pendiente funcionarios";
            ws.Cell(fila, 2).Value = resumen.TotalPendienteGeneral;

            fila++;
            ws.Cell(fila, 1).Value = "Ganancia del negocio";
            ws.Cell(fila, 2).Value = resumen.GananciaNegocio;

            ws.Range(fila - 7, 2, fila, 2).Style.NumberFormat.Format = "₡ #,##0.00";
            ws.Range(fila - 7, 2, fila, 2).Style.NumberFormat.Format = excelCurrencyFormat;
            ws.Cell(fila, 2).Style.Font.Bold = true;
            ws.Cell(fila, 2).Style.Font.FontColor = colorDorado;

            fila += 3;

            ws.Cell(fila, 1).Value = "Productos Vendidos";
            ws.Cell(fila, 1).Style.Font.Bold = true;
            ws.Cell(fila, 1).Style.Font.FontSize = 14;

            fila++;

            ws.Cell(fila, 1).Value = "Funcionario";
            ws.Cell(fila, 2).Value = "Fecha";
            ws.Cell(fila, 3).Value = "Producto";
            ws.Cell(fila, 4).Value = "Precio";
            ws.Cell(fila, 5).Value = "Ganancia Funcionario";

            var headerProductos = ws.Range(fila, 1, fila, 5);
            headerProductos.Style.Fill.BackgroundColor = colorNegro;
            headerProductos.Style.Font.FontColor = XLColor.White;
            headerProductos.Style.Font.Bold = true;

            fila++;

            foreach (var funcionario in resumen.Funcionarios)
            {
                foreach (var producto in funcionario.ProductosVendidos)
                {
                    ws.Cell(fila, 1).Value = funcionario.Nombre;
                    ws.Cell(fila, 2).Value = producto.Fecha;
                    ws.Cell(fila, 3).Value = producto.NombreProducto;
                    ws.Cell(fila, 4).Value = producto.Precio;
                    ws.Cell(fila, 5).Value = producto.GananciaFuncionario;

                    ws.Cell(fila, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                    ws.Cell(fila, 4).Style.NumberFormat.Format = "₡ #,##0.00";
                    ws.Cell(fila, 5).Style.NumberFormat.Format = "₡ #,##0.00";

                    ws.Cell(fila, 4).Style.NumberFormat.Format = excelCurrencyFormat;
                    ws.Cell(fila, 5).Style.NumberFormat.Format = excelCurrencyFormat;
                    fila++;
                }
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelReportFileNameBuilder.Build(nombreNegocio, "Pagos Funcionarios", generatedAt));
        }

        private async Task<IActionResult> SetActivoAsync(int id, bool activo)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
            {
                return NotFound();
            }

            if (funcionario.Activo == activo)
            {
                TempData["Mensaje"] = activo
                    ? "El funcionario ya estaba activo."
                    : "El funcionario ya estaba inactivo.";
                return RedirectToAction(nameof(Index));
            }

            if (activo)
            {
                var capacidad = await ValidateActiveFuncionarioCapacityAsync();
                if (!capacidad.IsAllowed)
                {
                    TempData["Error"] = capacidad.Message;
                    return RedirectToAction(nameof(Index));
                }

                var puestoActivo = await _context.Puestos
                    .AsNoTracking()
                    .AnyAsync(p => p.IdPuesto == funcionario.IdPuesto && p.Activo);

                if (!puestoActivo)
                {
                    TempData["Error"] = "No se puede activar el funcionario porque su puesto actual esta inactivo o ya no existe en este tenant.";
                    return RedirectToAction(nameof(Index));
                }
            }

            funcionario.Activo = activo;

            try
            {
                await _context.SaveChangesAsync();
                TempData["Mensaje"] = activo
                    ? "Funcionario activado."
                    : "Funcionario desactivado.";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al cambiar el estado del funcionario {FuncionarioId}.", id);
                TempData["Error"] = "No fue posible actualizar el estado del funcionario.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueo el cambio de estado del funcionario {FuncionarioId}.", id);
                TempData["Error"] = "No fue posible actualizar el estado del funcionario por una validacion de seguridad o consistencia.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task CargarPuestosAsync(int? selectedPuestoId = null)
        {
            ViewBag.Puestos = await _context.Puestos
                .AsNoTracking()
                .Where(p => p.Activo || (selectedPuestoId.HasValue && p.IdPuesto == selectedPuestoId.Value))
                .OrderBy(p => p.NombrePuesto)
                .ToListAsync();
        }

        private async Task ValidatePuestoAsync(int puestoId, int? currentPuestoId = null)
        {
            var puesto = await _context.Puestos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdPuesto == puestoId);

            if (puesto == null)
            {
                ModelState.AddModelError(nameof(Funcionario.IdPuesto), "El puesto seleccionado no existe o no pertenece al tenant actual.");
                return;
            }

            if (!puesto.Activo && currentPuestoId != puestoId)
            {
                ModelState.AddModelError(nameof(Funcionario.IdPuesto), "El puesto seleccionado esta inactivo. Selecciona un puesto activo del tenant actual.");
            }
        }

        private async Task<(bool IsAllowed, string Message)> ValidateActiveFuncionarioCapacityAsync()
        {
            var plan = await ResolvePlanAsync();
            if (plan == null && !UserIsPlatformSuperAdmin())
            {
                return (false, "No fue posible resolver el acceso comercial del tenant para validar el limite de funcionarios.");
            }

            if (plan?.MaxFuncionarios.HasValue == true)
            {
                var totalActivos = await _context.Funcionarios
                    .AsNoTracking()
                    .CountAsync(f => f.Activo);

                if (totalActivos >= plan.MaxFuncionarios.Value)
                {
                    _logger.LogWarning(
                        "Limite de funcionarios excedido. PlanId {PlanId}. PlanNombre {PlanNombre}. MaxFuncionarios {MaxFuncionarios}. TotalActivos {TotalActivos}.",
                        plan.Id,
                        plan.Nombre,
                        plan.MaxFuncionarios.Value,
                        totalActivos);

                    return (false, $"Tu plan actual permite hasta {plan.MaxFuncionarios.Value} funcionarios. Para agregar mas, actualiza tu plan.");
                }
            }

            return (true, string.Empty);
        }

        private async Task<Plan?> ResolvePlanAsync()
        {
            if (UserIsPlatformSuperAdmin())
            {
                return null;
            }

            if (HttpContext.Items.TryGetValue("TenantCommercialAccess", out var rawAccess) &&
                rawAccess is TenantCommercialAccessResult access &&
                access.EffectivePlanId.HasValue)
            {
                return await _context.Planes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == access.EffectivePlanId.Value);
            }

            _logger.LogWarning(
                "No fue posible resolver TenantCommercialAccess para crear funcionario. UserId {UserId}.",
                User.FindFirstValue(CustomClaimTypes.UserId) ?? User.FindFirstValue(ClaimTypes.NameIdentifier));

            return null;
        }

        private bool UserIsPlatformSuperAdmin() =>
            string.Equals(
                User.FindFirstValue(CustomClaimTypes.PlatformSuperAdmin),
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase);

        private static void NormalizeFuncionario(Funcionario funcionario)
        {
            funcionario.Nombre = NormalizeRequiredText(funcionario.Nombre);
            funcionario.Telefono = NormalizeOptionalText(funcionario.Telefono);
        }

        private static string NormalizeRequiredText(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : CollapseWhitespace(value);

        private static string? NormalizeOptionalText(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : CollapseWhitespace(value);

        private static string CollapseWhitespace(string value) =>
            string.Join(
                " ",
                value.Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        private static List<string> ObtenerMetodosPago() =>
            LiquidacionSemanalDefaults.MetodosPagoPermitidos.ToList();
    }
}
