using System.Globalization;
using LuxuryApp.Models.Calendar;
using LuxuryApp.Services.Calendar;
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
        private readonly ITenantWhatsAppFeatureService _tenantWhatsAppFeatureService;

        public CalendarController(
            ICalendarCommandService calendarCommandService,
            ICalendarQueryService calendarQueryService,
            ITenantWhatsAppFeatureService tenantWhatsAppFeatureService)
        {
            _calendarCommandService = calendarCommandService;
            _calendarQueryService = calendarQueryService;
            _tenantWhatsAppFeatureService = tenantWhatsAppFeatureService;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var whatsAppEnabled = await _tenantWhatsAppFeatureService
                .IsWhatsAppEnabledForCurrentTenantAsync(cancellationToken);

            // Se mantiene en ViewData por compatibilidad; la vista usa el modelo fuertemente tipado.
            ViewData[TenantWhatsAppEnabledViewDataKey] = whatsAppEnabled;

            var stats = await _calendarQueryService.GetHeaderStatsAsync(cancellationToken);

            return View(new CalendarIndexViewModel
            {
                TenantWhatsAppEnabled = whatsAppEnabled,
                Stats = stats
            });
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
                var created = await _calendarCommandService.CreateAsync(MapUpsertRequest(vm), cancellationToken);
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
                var updated = await _calendarCommandService.UpdateAsync(id, MapUpsertRequest(vm), cancellationToken);
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

            var citas = await _calendarQueryService.GetUpcomingAppointmentsAsync(parsedDate, funcionarioId, cancellationToken);
            return Ok(citas);
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

        private static CalendarUpsertRequest MapUpsertRequest(CitaCreateVM vm) =>
            new()
            {
                NombreCliente = vm.NombreCliente,
                TelefonoCliente = vm.TelefonoCliente,
                ClienteId = vm.ClienteId,
                ServicioId = vm.ServicioId,
                FechaHoraCita = vm.FechaHoraCita,
                FuncionarioId = vm.FuncionarioId,
                Tipo = vm.Tipo,
                DuracionMinutos = vm.DuracionMinutos,
                WhatsAppConsentAtCreation = vm.WhatsAppConsentAtCreation,
                WhatsAppConsentSource = vm.WhatsAppConsentSource,
                WhatsAppConsentCapturedAtUtc = vm.WhatsAppConsentCapturedAtUtc,
                Duplicar = vm.Duplicar,
                FechasDuplicadas = vm.FechasDuplicadas
            };

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
