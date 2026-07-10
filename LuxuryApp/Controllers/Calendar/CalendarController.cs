using System.Globalization;
using System.Security.Claims;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Comprobantes;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Fiscal;
using LuxuryApp.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Calendar
{
    [Authorize(Roles = "Administrador")]
    public class CalendarController : Controller
    {
        private const string TenantWhatsAppEnabledViewDataKey = "TenantWhatsAppEnabled";
        private readonly ICalendarCommandService _calendarCommandService;
        private readonly ICalendarQueryService _calendarQueryService;
        private readonly IControlCobrosQueryService _controlCobrosQueryService;
        private readonly ICobroService _cobroService;
        private readonly IComprobanteCobroService _comprobanteService;
        private readonly ITenantWhatsAppFeatureService _tenantWhatsAppFeatureService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ICobroFiscalPreviewService _cobroFiscalPreviewService;

        public CalendarController(
            ICalendarCommandService calendarCommandService,
            ICalendarQueryService calendarQueryService,
            IControlCobrosQueryService controlCobrosQueryService,
            ICobroService cobroService,
            IComprobanteCobroService comprobanteService,
            ITenantWhatsAppFeatureService tenantWhatsAppFeatureService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ICobroFiscalPreviewService cobroFiscalPreviewService)
        {
            _calendarCommandService = calendarCommandService;
            _calendarQueryService = calendarQueryService;
            _controlCobrosQueryService = controlCobrosQueryService;
            _cobroService = cobroService;
            _comprobanteService = comprobanteService;
            _tenantWhatsAppFeatureService = tenantWhatsAppFeatureService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _cobroFiscalPreviewService = cobroFiscalPreviewService;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var hasAddon = await _tenantWhatsAppFeatureService
                .HasWhatsAppAddonAsync(cancellationToken);

            var whatsAppEnabled = await _tenantWhatsAppFeatureService
                .IsWhatsAppEnabledForCurrentTenantAsync(cancellationToken);

            ViewData[TenantWhatsAppEnabledViewDataKey] = whatsAppEnabled;

            // Tenants sin add-on solo necesitan el conteo simple; evita la query
            // de agrupación por EstadoConfirmacionWhatsApp que es irrelevante para ellos.
            CalendarHeaderStatsResponse stats;
            if (hasAddon)
            {
                stats = await _calendarQueryService.GetHeaderStatsAsync(cancellationToken);
            }
            else
            {
                var citasHoy = await _calendarQueryService.GetCitasHoyCountAsync(cancellationToken);
                stats = new CalendarHeaderStatsResponse { CitasHoy = citasHoy };
            }

            return View(new CalendarIndexViewModel
            {
                HasWhatsAppAddon = hasAddon,
                TenantWhatsAppEnabled = whatsAppEnabled,
                Stats = stats,
                BusinessTodayIso = _businessDateTimeProvider.Today().ToString("yyyy-MM-dd")
            });
        }

        // ─────────────── Control de citas y cobros (vista admin) ───────────────

        [HttpGet("Calendar/ControlCobros")]
        public async Task<IActionResult> ControlCobros(
            string? rango,
            string? fecha,
            int? funcionarioId,
            string? estado,
            string? buscar,
            CancellationToken cancellationToken)
        {
            var model = await BuildControlCobrosAsync(rango, fecha, funcionarioId, estado, buscar, cancellationToken);
            return View(model);
        }

        [HttpGet("Calendar/ControlCobrosData")]
        public async Task<IActionResult> ControlCobrosData(
            string? rango,
            string? fecha,
            int? funcionarioId,
            string? estado,
            string? buscar,
            CancellationToken cancellationToken)
        {
            var model = await BuildControlCobrosAsync(rango, fecha, funcionarioId, estado, buscar, cancellationToken);
            return PartialView("_ControlCobrosResultados", model);
        }

        // Desglose fiscal (informativo) para el modal de cobro. El cálculo lo hace el motor
        // fiscal central en el backend → NO hay fórmula de IVA duplicada en el frontend.
        [HttpGet("Calendar/PreviewCobroFiscal")]
        public async Task<IActionResult> PreviewCobroFiscal(
            int citaId,
            decimal monto,
            CancellationToken cancellationToken)
        {
            var preview = await _cobroFiscalPreviewService.PreviewCitaAsync(citaId, monto, cancellationToken);
            if (preview is null)
            {
                return BadRequest(new { error = "La cita no existe o no pertenece a tu negocio." });
            }

            return Ok(new
            {
                total = preview.Total,
                baseSinIva = preview.BaseSinIva,
                iva = preview.IvaIncluido,
                tarifaIva = preview.TarifaIva,
                aplicaIva = preview.AplicaIva,
                tipoLinea = preview.TipoLinea
            });
        }

        [HttpPost("Calendar/CobrarCita")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CobrarCita(
            int citaId,
            decimal monto,
            string? metodoPago,
            string? observacion,
            bool enviarComprobante,
            string? emailComprobante,
            bool guardarEmailEnCliente,
            CancellationToken cancellationToken)
        {
            if (citaId <= 0)
            {
                return BadRequest(new { error = "La cita indicada no es válida." });
            }

            if (enviarComprobante && !ComprobanteEmailHelper.EsValido(emailComprobante))
            {
                return BadRequest(new { error = "Indica un correo válido para enviar el comprobante." });
            }

            // Resuelve la cita en backend (tenant-safe). El FuncionarioId del cobro es el de
            // la cita, nunca un valor del formulario, para que la comisión vaya a quien corresponde.
            var cita = await _controlCobrosQueryService.ObtenerCitaParaCobroAsync(citaId, cancellationToken);
            if (cita is null)
            {
                return BadRequest(new { error = "La cita no existe o no pertenece a tu negocio." });
            }

            if (cita.YaCobrada)
            {
                return BadRequest(new { error = "Esta cita ya tiene un cobro registrado." });
            }

            // Una cita con servicio personalizado no tiene ServicioId; el cobro conserva el nombre
            // del servicio como snapshot. El monto final lo captura el modal (puede no haber precio base).
            var request = new CobroCreateRequest
            {
                FechaCobro = _businessDateTimeProvider.Now(),
                NombreCliente = cita.NombreCliente,
                FuncionarioId = cita.FuncionarioId,
                ClienteId = cita.ClienteId,
                ServicioId = cita.ServicioId,
                ServicioNombrePersonalizado = cita.ServicioId.HasValue ? null : cita.ServicioNombrePersonalizado,
                CitaId = cita.CitaId,
                Monto = monto,
                MetodoPago = metodoPago ?? string.Empty,
                Observaciones = observacion
            };

            // 1) Registrar el cobro. Solo los errores DE COBRO devuelven BadRequest.
            int cobroId;
            try
            {
                // CobroService valida monto/método, pertenencia y unicidad (anti doble cobro
                // a nivel de servicio + índice único UX_Cobros_TenantId_CitaId).
                cobroId = await _cobroService.RegistrarAsync(request, cancellationToken);
            }
            catch (CobroValidationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            // 2) Cobro YA registrado. Comprobante best-effort (el servicio nunca lanza).
            if (!enviarComprobante)
            {
                return Ok(new { success = true, message = "Cobro registrado correctamente." });
            }

            var comprobante = await _comprobanteService.CrearYEnviarDesdeCobroAsync(
                cobroId,
                emailComprobante!,
                guardarEmailEnCliente,
                User.Identity?.Name,
                funcionarioScopeId: null,
                cancellationToken);

            var enviado = comprobante is not null &&
                comprobante.EstadoEnvio == Models.Comprobantes.ComprobanteEstadoEnvio.Sent;

            return Ok(new
            {
                success = true,
                message = enviado
                    ? "Cobro registrado y comprobante enviado."
                    : "Cobro registrado correctamente, pero no se pudo enviar el comprobante. Puedes reenviarlo desde el historial."
            });
        }

        private async Task<ControlCitasCobrosViewModel> BuildControlCobrosAsync(
            string? rango,
            string? fecha,
            int? funcionarioId,
            string? estado,
            string? buscar,
            CancellationToken cancellationToken)
        {
            var hasAddon = await _tenantWhatsAppFeatureService.HasWhatsAppAddonAsync(cancellationToken);

            var filtro = new ControlCitasCobrosFiltroViewModel
            {
                Rango = rango ?? "dia",
                Fecha = TryParseLocalDate(fecha, out var parsed) ? parsed : _businessDateTimeProvider.Today(),
                FuncionarioId = funcionarioId,
                EstadoPago = estado ?? "todos",
                Buscar = buscar
            };

            return await _controlCobrosQueryService.ObtenerAsync(filtro, hasAddon, cancellationToken);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CitaCreateVM vm, CancellationToken cancellationToken)
        {
            if (vm == null)
            {
                return BadRequest("Datos invalidos.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(GetValidationMessage());
            }

            try
            {
                var created = await _calendarCommandService.CreateAsync(
                    MapUpsertRequest(vm, ResolveCurrentUserId()),
                    cancellationToken);
                return Ok(created);
            }
            catch (CalendarValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCitasByDay(string date, CancellationToken cancellationToken)
        {
            if (!TryParseLocalDate(date, out var parsedDate))
            {
                return BadRequest("La fecha solicitada no es valida.");
            }

            var citas = await _calendarQueryService.GetAppointmentsByDayAsync(parsedDate, cancellationToken);
            return Ok(citas);
        }

        [HttpGet("Calendar/GetById/{id}")]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var cita = await _calendarQueryService.GetByIdAsync(id, cancellationToken);
            return cita is null ? NotFound() : Ok(cita);
        }

        [HttpPut("Calendar/Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [FromBody] CitaCreateVM vm, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            if (vm == null)
            {
                return BadRequest("Datos invalidos.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(GetValidationMessage());
            }

            try
            {
                var updated = await _calendarCommandService.UpdateAsync(
                    id,
                    MapUpsertRequest(vm, ResolveCurrentUserId()),
                    cancellationToken);
                return Ok(updated);
            }
            catch (CalendarValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("Calendar/ResizeDuration/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResizeDuration(int id, [FromBody] ResizeDurationVM vm, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            if (vm == null)
            {
                return BadRequest("Datos invalidos.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(GetValidationMessage());
            }

            try
            {
                await _calendarCommandService.ResizeDurationAsync(id, vm.DuracionMinutos, cancellationToken);
                return Ok(new { success = true });
            }
            catch (CalendarValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("Calendar/Delete/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            try
            {
                await _calendarCommandService.DeleteAsync(id, cancellationToken);
                return Ok(new { success = true });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCitasCountByMonth(int year, int month, CancellationToken cancellationToken)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12)
            {
                return BadRequest("El ano o mes solicitado no es valido.");
            }

            var data = await _calendarQueryService.GetCitasCountByMonthAsync(year, month, cancellationToken);
            return Ok(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetUpcomingAppointments(string date, int? funcionarioId, CancellationToken cancellationToken)
        {
            if (!TryParseLocalDate(date, out var parsedDate))
            {
                return BadRequest("La fecha solicitada no es valida.");
            }

            if (!await ValidateFuncionarioFilterAsync(funcionarioId, cancellationToken))
            {
                return BadRequest("El funcionario solicitado no es valido.");
            }

            var citas = await _calendarQueryService.GetUpcomingAppointmentsAsync(parsedDate, funcionarioId, cancellationToken);
            return Ok(citas);
        }

        private async Task<bool> ValidateFuncionarioFilterAsync(int? funcionarioId, CancellationToken cancellationToken)
        {
            if (!funcionarioId.HasValue)
            {
                return true;
            }

            return await _calendarQueryService.FuncionarioExistsForCurrentTenantAsync(
                funcionarioId.Value,
                cancellationToken);
        }

        [HttpGet]
        public async Task<IActionResult> GetServiciosActivos(CancellationToken cancellationToken)
        {
            var servicios = await _calendarQueryService.GetServiciosActivosAsync(cancellationToken);
            return Ok(servicios);
        }

        [HttpPost("Calendar/ProcesarVisitas")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcesarVisitas(CancellationToken cancellationToken)
        {
            await _calendarCommandService.ProcessVisitsAsync(cancellationToken);
            return Ok(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetFechasOcupadas(
            int funcionarioId,
            string? startDate,
            string? endDate,
            CancellationToken cancellationToken)
        {
            if (funcionarioId <= 0)
            {
                return BadRequest("Debe seleccionar un funcionario valido.");
            }

            if (!TryParseOptionalLocalDate(startDate, out var parsedStartDate) ||
                !TryParseOptionalLocalDate(endDate, out var parsedEndDate))
            {
                return BadRequest("El rango solicitado no es valido.");
            }

            var citas = await _calendarQueryService.GetFechasOcupadasAsync(
                funcionarioId,
                parsedStartDate,
                parsedEndDate,
                cancellationToken);

            return Ok(citas);
        }

        [HttpPut("Calendar/Move/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Move(int id, [FromBody] MoveCitaVM vm, CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            if (vm == null)
            {
                return BadRequest("Datos invalidos.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(GetValidationMessage());
            }

            try
            {
                await _calendarCommandService.MoveAsync(id, MapMoveRequest(vm), cancellationToken);
                return Ok(new { success = true });
            }
            catch (CalendarValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private static CalendarUpsertRequest MapUpsertRequest(CitaCreateVM vm, string? capturedByUserId) =>
            new()
            {
                NombreCliente = vm.NombreCliente,
                TelefonoCliente = vm.TelefonoCliente,
                ClienteId = vm.ClienteId,
                ServicioId = vm.ServicioId,
                EsServicioPersonalizado = vm.EsServicioPersonalizado,
                ServicioNombrePersonalizado = vm.ServicioNombrePersonalizado,
                FechaHoraCita = vm.FechaHoraCita,
                FuncionarioId = vm.FuncionarioId,
                Tipo = vm.Tipo,
                DuracionMinutos = vm.DuracionMinutos,
                WhatsAppConsentAtCreation = vm.WhatsAppConsentAtCreation,
                WhatsAppConsentSource = vm.WhatsAppConsentSource,
                WhatsAppConsentCapturedAtUtc = vm.WhatsAppConsentCapturedAtUtc,
                AutorizarWhatsAppAlGuardar = vm.AutorizarWhatsAppAlGuardar,
                // El id del usuario proviene de los claims autenticados, no del cuerpo enviado.
                WhatsAppConsentCapturedByUserId = capturedByUserId,
                Duplicar = vm.Duplicar,
                FechasDuplicadas = vm.FechasDuplicadas
            };

        private string? ResolveCurrentUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private static CalendarMoveRequest MapMoveRequest(MoveCitaVM vm) =>
            new()
            {
                FechaHoraCita = vm.FechaHoraCita,
                FuncionarioId = vm.FuncionarioId
            };

        private static bool TryParseLocalDate(string? value, out DateTime parsedDate) =>
            DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);

        private static bool TryParseOptionalLocalDate(string? value, out DateTime? parsedDate)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                parsedDate = null;
                return true;
            }

            var parsed = TryParseLocalDate(value, out var date);
            parsedDate = parsed ? date : null;
            return parsed;
        }

        private string GetValidationMessage()
        {
            var errors = ModelState.Values
                .SelectMany(value => value.Errors)
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Datos invalidos."
                    : error.ErrorMessage)
                .Distinct()
                .ToList();

            return errors.Count == 0
                ? "Datos invalidos."
                : string.Join(" ", errors);
        }
    }
}
