using System.Globalization;
using System.Security.Claims;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Funcionarios
{
    /// <summary>
    /// Portal limitado del funcionario. Solo accesible con el rol Funcionario.
    /// El FuncionarioId se toma del claim (firmado en la cookie); jamás de la URL.
    /// Los permisos se validan en backend contra base de datos en cada acción sensible.
    /// </summary>
    [Authorize(Policy = AppAuthorizationPolicies.RequireFuncionario)]
    [Route("MiPortal")]
    public sealed class MiPortalController : Controller
    {
        private readonly IFuncionarioPortalQueryService _portalQueryService;
        private readonly IFuncionarioPortalPermissionService _permissionService;
        private readonly ICalendarCommandService _calendarCommandService;
        private readonly ICobroService _cobroService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ILogger<MiPortalController> _logger;

        public MiPortalController(
            IFuncionarioPortalQueryService portalQueryService,
            IFuncionarioPortalPermissionService permissionService,
            ICalendarCommandService calendarCommandService,
            ICobroService cobroService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ILogger<MiPortalController> logger)
        {
            _portalQueryService = portalQueryService;
            _permissionService = permissionService;
            _calendarCommandService = calendarCommandService;
            _cobroService = cobroService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _logger = logger;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            var model = await _portalQueryService.ObtenerPanelAsync(
                ctx.FuncionarioId,
                ctx.Permisos!.RegistrarMisCobros,
                cancellationToken);

            return View(model);
        }

        [HttpGet("Calendario")]
        public async Task<IActionResult> Calendario(string? fecha, CancellationToken cancellationToken)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            if (!ctx.Permisos!.VerMiCalendario)
            {
                return SinPermiso("tu calendario");
            }

            var fechaSeleccionada = TryParseFecha(fecha, out var parsed)
                ? parsed
                : _businessDateTimeProvider.Today();

            var model = await _portalQueryService.ObtenerCalendarioAsync(
                ctx.FuncionarioId,
                fechaSeleccionada,
                ctx.Permisos.CrearMisCitas,
                ctx.Permisos.RegistrarMisCobros,
                cancellationToken);

            return View(model);
        }

        [HttpGet("Ganancias")]
        public async Task<IActionResult> Ganancias(CancellationToken cancellationToken)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            if (!ctx.Permisos!.VerMisGanancias)
            {
                return SinPermiso("tus ganancias");
            }

            var model = await _portalQueryService.ObtenerGananciasAsync(ctx.FuncionarioId, cancellationToken);
            return View(model);
        }

        [HttpGet("Pagos")]
        public async Task<IActionResult> Pagos(int pagina = 1, CancellationToken cancellationToken = default)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            if (!ctx.Permisos!.VerMisPagos)
            {
                return SinPermiso("tus pagos");
            }

            var model = await _portalQueryService.ObtenerPagosAsync(ctx.FuncionarioId, pagina, cancellationToken);
            return View(model);
        }

        [HttpGet("Cobros")]
        public async Task<IActionResult> Cobros(int pagina = 1, CancellationToken cancellationToken = default)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            if (!ctx.Permisos!.VerMisCobros)
            {
                return SinPermiso("tus cobros");
            }

            var model = await _portalQueryService.ObtenerCobrosAsync(
                ctx.FuncionarioId,
                pagina,
                ctx.Permisos.RegistrarMisCobros,
                cancellationToken);

            return View(model);
        }

        [HttpPost("Calendario/CrearCita")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearCita(
            string? fechaHora,
            string? nombreCliente,
            string? telefonoCliente,
            int? servicioId,
            string? servicioPersonalizado,
            int? duracionMinutos,
            CancellationToken cancellationToken)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            if (!ctx.Permisos!.CrearMisCitas)
            {
                _logger.LogWarning(
                    "Intento de crear cita sin permiso. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                    ctx.FuncionarioId,
                    User.FindFirstValue(CustomClaimTypes.UserId));
                return SinPermiso("agendar citas");
            }

            if (!TryParseFechaHora(fechaHora, out var fechaHoraCita))
            {
                TempData["PortalError"] = "Indica una fecha y hora válidas para la cita.";
                return RedirectToAction(nameof(Calendario));
            }

            var esPersonalizado = !servicioId.HasValue || servicioId.Value <= 0;

            var request = new CalendarUpsertRequest
            {
                // 🔒 FuncionarioId SIEMPRE del claim. Se ignora cualquier valor del navegador.
                FuncionarioId = ctx.FuncionarioId,
                Tipo = "CITA",
                FechaHoraCita = fechaHoraCita,
                NombreCliente = nombreCliente,
                TelefonoCliente = telefonoCliente,
                ServicioId = esPersonalizado ? null : servicioId,
                EsServicioPersonalizado = esPersonalizado,
                ServicioNombrePersonalizado = esPersonalizado ? servicioPersonalizado : null,
                DuracionMinutos = esPersonalizado ? duracionMinutos : null
            };

            try
            {
                var creada = await _calendarCommandService.CreateAsync(request, cancellationToken);
                _logger.LogInformation(
                    "Cita creada desde portal. TenantId-claim {Tenant}. FuncionarioId {FuncionarioId}. CitaId {CitaId}. UserId {UserId}.",
                    User.FindFirstValue(CustomClaimTypes.TenantId),
                    ctx.FuncionarioId,
                    creada.Id,
                    User.FindFirstValue(CustomClaimTypes.UserId));
                TempData["PortalMensaje"] = "Cita agendada correctamente.";
            }
            catch (CalendarValidationException ex)
            {
                TempData["PortalError"] = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                TempData["PortalError"] = ex.Message;
            }

            return RedirectToAction(nameof(Calendario), new { fecha = fechaHoraCita.ToString("yyyy-MM-dd") });
        }

        [HttpPost("Cobros/Registrar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarCobro(
            int citaId,
            decimal monto,
            string? metodoPago,
            string? observacion,
            string? retorno,
            CancellationToken cancellationToken)
        {
            var ctx = await ResolverContextoAsync(cancellationToken);
            if (ctx.Bloqueo is not null)
            {
                return ctx.Bloqueo;
            }

            if (!ctx.Permisos!.RegistrarMisCobros)
            {
                _logger.LogWarning(
                    "Intento de registrar cobro sin permiso. FuncionarioId {FuncionarioId}. UserId {UserId}.",
                    ctx.FuncionarioId,
                    User.FindFirstValue(CustomClaimTypes.UserId));
                return SinPermiso("registrar cobros");
            }

            // Resuelve la cita en backend; valida pertenencia al funcionario y al tenant.
            var cita = await _portalQueryService.ObtenerCitaCobrableAsync(ctx.FuncionarioId, citaId, cancellationToken);
            if (cita is null)
            {
                TempData["PortalError"] = "La cita no existe, no es tuya o no se puede cobrar.";
                return RedirectToActionRetorno(retorno);
            }

            if (!cita.ServicioId.HasValue)
            {
                TempData["PortalError"] = "Esta cita usa un servicio personalizado y debe cobrarse desde el sistema del negocio.";
                return RedirectToActionRetorno(retorno);
            }

            if (cita.YaCobrada)
            {
                TempData["PortalError"] = "Esta cita ya tiene un cobro registrado.";
                return RedirectToActionRetorno(retorno);
            }

            var request = new CobroCreateRequest
            {
                FechaCobro = _businessDateTimeProvider.Now(),
                NombreCliente = string.IsNullOrWhiteSpace(cita.Cliente) ? "Cliente" : cita.Cliente,
                FuncionarioId = ctx.FuncionarioId, // 🔒 del claim, no del form
                ServicioId = cita.ServicioId,
                CitaId = cita.Id,
                Monto = monto,
                MetodoPago = metodoPago ?? string.Empty,
                Observaciones = observacion
            };

            try
            {
                await _cobroService.RegistrarAsync(request, cancellationToken);
                _logger.LogInformation(
                    "Cobro registrado desde portal. FuncionarioId {FuncionarioId}. CitaId {CitaId}. UserId {UserId}.",
                    ctx.FuncionarioId,
                    cita.Id,
                    User.FindFirstValue(CustomClaimTypes.UserId));
                TempData["PortalMensaje"] = "Cobro registrado correctamente.";
            }
            catch (CobroValidationException ex)
            {
                TempData["PortalError"] = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                TempData["PortalError"] = ex.Message;
            }

            return RedirectToActionRetorno(retorno);
        }

        [HttpGet("NoDisponible")]
        public IActionResult NoDisponible() => View();

        // ─────────────────────────── Helpers ───────────────────────────

        private async Task<PortalContexto> ResolverContextoAsync(CancellationToken cancellationToken)
        {
            var claim = User.FindFirstValue(CustomClaimTypes.FuncionarioId);
            if (!int.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var funcionarioId) ||
                funcionarioId <= 0)
            {
                return new PortalContexto { Bloqueo = RedirectToAction(nameof(NoDisponible)) };
            }

            var funcionario = await _portalQueryService.ResolverFuncionarioAsync(funcionarioId, cancellationToken);
            if (funcionario is null || !funcionario.Activo)
            {
                return new PortalContexto { FuncionarioId = funcionarioId, Bloqueo = RedirectToAction(nameof(NoDisponible)) };
            }

            var permisos = await _permissionService.ObtenerAsync(funcionarioId, cancellationToken);

            // El layout usa esto para mostrar/ocultar tabs según permisos.
            ViewData["PortalPerms"] = permisos;

            return new PortalContexto
            {
                FuncionarioId = funcionarioId,
                Permisos = permisos
            };
        }

        private IActionResult SinPermiso(string seccion)
        {
            TempData["PortalError"] = $"No tienes permiso para acceder a {seccion}.";
            return RedirectToAction(nameof(Index));
        }

        private IActionResult RedirectToActionRetorno(string? retorno) => retorno switch
        {
            "calendario" => RedirectToAction(nameof(Calendario)),
            "cobros" => RedirectToAction(nameof(Cobros)),
            _ => RedirectToAction(nameof(Index))
        };

        private static bool TryParseFecha(string? value, out DateTime fecha)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                DateTime.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out fecha))
            {
                return true;
            }

            fecha = default;
            return false;
        }

        private static bool TryParseFechaHora(string? value, out DateTime fechaHora)
        {
            string[] formatos = { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm" };
            if (!string.IsNullOrWhiteSpace(value) &&
                DateTime.TryParseExact(
                    value,
                    formatos,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out fechaHora))
            {
                return true;
            }

            fechaHora = default;
            return false;
        }

        private sealed class PortalContexto
        {
            public int FuncionarioId { get; init; }
            public FuncionarioPortalPermisosSet? Permisos { get; init; }
            public IActionResult? Bloqueo { get; init; }
        }
    }
}
