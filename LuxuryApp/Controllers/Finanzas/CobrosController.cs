using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Comprobantes;
using LuxuryApp.Services.Contracts;
using LuxuryApp.Services.Exports;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]
    public class CobrosController : Controller
    {
        private readonly ICobroService _cobroService;
        private readonly ICobroQueryService _cobroQueryService;
        private readonly IComprobanteCobroService _comprobanteService;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;
        private readonly ITenantDisplayNameService _tenantDisplayNameService;

        public CobrosController(
            ICobroService cobroService,
            ICobroQueryService cobroQueryService,
            IComprobanteCobroService comprobanteService,
            IBusinessDateTimeProvider businessDateTimeProvider,
            ITenantDisplayNameService tenantDisplayNameService)
        {
            _cobroService = cobroService;
            _cobroQueryService = cobroQueryService;
            _comprobanteService = comprobanteService;
            _businessDateTimeProvider = businessDateTimeProvider;
            _tenantDisplayNameService = tenantDisplayNameService;
        }

        public async Task<IActionResult> Index(CobroFiltroViewModel filtros)
        {
            var vm = await _cobroQueryService.BuildIndexViewModelAsync(filtros);
            return View(vm);
        }

        public async Task<IActionResult> Create()
        {
            var vm = await _cobroQueryService.BuildCreateViewModelAsync();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CobroViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(await _cobroQueryService.BuildCreateViewModelAsync(vm.Cobro));
            }

            // Opciones de comprobante (no están en el modelo Cobro; se leen del formulario).
            var enviarComprobante = ContractAcceptanceBindingHelper.IsAccepted(Request.Form, "EnviarComprobante");
            var emailComprobante = (Request.Form["EmailComprobante"].FirstOrDefault() ?? string.Empty).Trim();
            var guardarEmailEnCliente = ContractAcceptanceBindingHelper.IsAccepted(Request.Form, "GuardarEmailEnCliente");

            if (enviarComprobante && !ComprobanteEmailHelper.EsValido(emailComprobante))
            {
                ModelState.AddModelError("EmailComprobante", "Indica un correo válido para enviar el comprobante.");
                return View(await _cobroQueryService.BuildCreateViewModelAsync(vm.Cobro));
            }

            // 1) Registrar el cobro. Solo los errores DE COBRO re-renderizan el formulario.
            int cobroId;
            try
            {
                var actualizarNotas = ContractAcceptanceBindingHelper.IsAccepted(Request.Form, "ActualizarNotasServicio");
                var notasTexto = Request.Form["NotasServicioTexto"].FirstOrDefault();
                cobroId = await _cobroService.RegistrarAsync(MapRequest(vm.Cobro, actualizarNotas, notasTexto));
            }
            catch (CobroValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
                return View(await _cobroQueryService.BuildCreateViewModelAsync(vm.Cobro));
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(await _cobroQueryService.BuildCreateViewModelAsync(vm.Cobro));
            }

            // 2) Cobro YA registrado. El comprobante es best-effort: el servicio nunca lanza;
            //    si no se pudo enviar, el cobro queda igual y se reintenta desde el historial.
            if (enviarComprobante)
            {
                var comprobante = await _comprobanteService.CrearYEnviarDesdeCobroAsync(
                    cobroId,
                    emailComprobante,
                    guardarEmailEnCliente,
                    User.Identity?.Name,
                    funcionarioScopeId: null);

                TempData["Mensaje"] = ComprobanteFueEnviado(comprobante)
                    ? "Cobro registrado y comprobante enviado correctamente."
                    : "Cobro registrado correctamente, pero no se pudo enviar el comprobante. Puedes reenviarlo desde el historial.";
            }
            else
            {
                TempData["Mensaje"] = "Cobro registrado correctamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var vm = await _cobroQueryService.BuildEditViewModelAsync(id);

            if (vm is null)
            {
                return NotFound();
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CobroViewModel vm)
        {
            if (id != vm.Cobro.IdCobro)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                var invalidVm = await _cobroQueryService.BuildEditViewModelAsync(id, vm.Cobro);
                return invalidVm is null ? NotFound() : View(invalidVm);
            }

            try
            {
                var updated = await _cobroService.ActualizarAsync(MapUpdateRequest(vm.Cobro));

                if (!updated)
                {
                    return NotFound();
                }

                TempData["Mensaje"] = "Cobro actualizado correctamente.";
                return RedirectToAction(nameof(Index));
            }
            catch (CobroValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            var editVm = await _cobroQueryService.BuildEditViewModelAsync(id, vm.Cobro);
            return editVm is null ? NotFound() : View(editVm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var deleted = await _cobroService.EliminarAsync(id);

                if (!deleted)
                {
                    return NotFound();
                }

                TempData["Mensaje"] = "Cobro eliminado correctamente.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReenviarComprobante(int comprobanteId, CancellationToken cancellationToken)
        {
            var comprobante = await _comprobanteService.ReenviarAsync(comprobanteId, funcionarioScopeId: null, cancellationToken);

            if (comprobante is null)
            {
                TempData["Error"] = "No se encontró el comprobante indicado.";
            }
            else
            {
                TempData["Mensaje"] = ComprobanteFueEnviado(comprobante)
                    ? "Comprobante reenviado correctamente."
                    : "No fue posible reenviar el comprobante. Intenta de nuevo en unos minutos.";
            }

            return RedirectToAction(nameof(Index));
        }

        private static bool ComprobanteFueEnviado(LuxuryApp.Models.Comprobantes.ComprobanteCobro? comprobante) =>
            comprobante is not null &&
            comprobante.EstadoEnvio == LuxuryApp.Models.Comprobantes.ComprobanteEstadoEnvio.Sent;

        [HttpGet]
        public async Task<JsonResult> ObtenerPrecioServicio(int id)
        {
            var precio = await _cobroQueryService.ObtenerPrecioServicioAsync(id);
            return Json(precio.HasValue ? new { precio = precio.Value } : null);
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerPrecioProducto(int id)
        {
            var precio = await _cobroQueryService.ObtenerPrecioProductoAsync(id);
            return Json(precio.HasValue ? new { precio = precio.Value } : null);
        }

        public async Task<IActionResult> ExportarExcel(CobroFiltroViewModel filtros)
        {
            var reporte = await _cobroQueryService.BuildIndexViewModelAsync(
                filtros,
                includeFilterOptions: false);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Reporte Cobros");

            var colorNegro = XLColor.FromHtml("#1C1C1C");
            var colorDorado = XLColor.FromHtml("#C6A55C");
            var colorGris = XLColor.FromHtml("#F5F5F5");
            const string excelCurrencyFormat = "CRC #,##0.00";

            var nombreNegocio = await _tenantDisplayNameService.GetCurrentTenantDisplayNameAsync(HttpContext.RequestAborted);

            ws.Range("A1:G1").Merge();
            ws.Cell("A1").Value = nombreNegocio;
            ws.Cell("A1").Style.Font.FontSize = 20;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontColor = colorDorado;
            ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range("A2:G2").Merge();
            ws.Cell("A2").Value = "Reporte Financiero de Cobros";
            ws.Cell("A2").Style.Font.FontSize = 14;
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range("A3:G3").Merge();
            var generatedAt = _businessDateTimeProvider.Now();

            ws.Cell("A3").Value = $"Generado el {generatedAt:dd/MM/yyyy HH:mm}";
            ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var fila = 5;

            ws.Cell(fila, 1).Value = "Resumen Financiero";
            ws.Cell(fila, 1).Style.Font.Bold = true;
            ws.Cell(fila, 1).Style.Font.FontSize = 14;

            fila += 2;

            ws.Cell(fila, 1).Value = "Cantidad Cobros";
            ws.Cell(fila, 2).Value = reporte.Cobros.Count;
            ws.Cell(fila, 2).Style.NumberFormat.Format = "0";

            fila++;
            var filaInicioMontos = fila;

            ws.Cell(fila, 1).Value = "Total Servicios";
            ws.Cell(fila, 2).Value = reporte.TotalServicios;
            fila++;

            ws.Cell(fila, 1).Value = "Total Productos";
            ws.Cell(fila, 2).Value = reporte.TotalProductos;
            fila++;

            ws.Cell(fila, 1).Value = "Total Generado";
            ws.Cell(fila, 2).Value = reporte.TotalGenerado;
            fila++;

            ws.Cell(fila, 1).Value = "Impuestos";
            ws.Cell(fila, 2).Value = reporte.TotalImpuestos;
            fila++;

            ws.Cell(fila, 1).Value = "Total sin impuestos";
            ws.Cell(fila, 2).Value = reporte.TotalSinImpuestos;
            fila++;

            ws.Cell(fila, 1).Value = "Pago colaboradores";
            ws.Cell(fila, 2).Value = reporte.PagoColaboradores;
            fila++;

            ws.Cell(fila, 1).Value = "Ganancia negocio";
            ws.Cell(fila, 2).Value = reporte.GananciaNegocio;
            fila++;

            ws.Cell(fila, 1).Value = "Ventas efectivo";
            ws.Cell(fila, 2).Value = reporte.GananciaEfectivo;
            fila++;

            ws.Cell(fila, 1).Value = "Ventas tarjeta";
            ws.Cell(fila, 2).Value = reporte.GananciaTarjeta;
            fila++;

            ws.Cell(fila, 1).Value = "Ventas SINPE";
            ws.Cell(fila, 2).Value = reporte.GananciaSinpe;

            var filaFinMontos = fila;
            ws.Range(filaInicioMontos, 2, filaFinMontos, 2)
                .Style.NumberFormat.Format = excelCurrencyFormat;

            fila += 3;
            var headerRow = fila;

            ws.Cell(headerRow, 1).Value = "Fecha";
            ws.Cell(headerRow, 2).Value = "Cliente";
            ws.Cell(headerRow, 3).Value = "Funcionario";
            ws.Cell(headerRow, 4).Value = "Tipo";
            ws.Cell(headerRow, 5).Value = "Detalle";
            ws.Cell(headerRow, 6).Value = "Monto";
            ws.Cell(headerRow, 7).Value = "Método Pago";

            var header = ws.Range(headerRow, 1, headerRow, 7);
            header.Style.Fill.BackgroundColor = colorNegro;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            fila = headerRow + 1;

            foreach (var cobro in reporte.Cobros)
            {
                ws.Cell(fila, 1).Value = cobro.FechaCobro;
                ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

                ws.Cell(fila, 2).Value = cobro.NombreCliente;
                ws.Cell(fila, 3).Value = cobro.FuncionarioNombre;
                ws.Cell(fila, 4).Value = cobro.EsServicio ? "Servicio" : "Producto";
                ws.Cell(fila, 5).Value = cobro.Detalle;
                ws.Cell(fila, 6).Value = cobro.Monto;
                ws.Cell(fila, 6).Style.NumberFormat.Format = excelCurrencyFormat;
                ws.Cell(fila, 7).Value = cobro.MetodoPago;
                fila++;
            }

            if (reporte.Cobros.Count > 0)
            {
                var dataRange = ws.Range(headerRow + 1, 1, fila - 1, 7);
                dataRange.AddConditionalFormat()
                    .WhenIsTrue("MOD(ROW(),2)=0")
                    .Fill.SetBackgroundColor(colorGris);

                ws.Range(headerRow, 1, fila - 1, 7).SetAutoFilter();
            }

            ws.Cell(fila, 5).Value = "TOTAL GENERAL";
            ws.Cell(fila, 5).Style.Font.Bold = true;
            ws.Cell(fila, 6).Value = reporte.TotalGenerado;
            ws.Cell(fila, 6).Style.NumberFormat.Format = excelCurrencyFormat;
            ws.Cell(fila, 6).Style.Font.Bold = true;
            ws.Cell(fila, 6).Style.Font.FontColor = colorDorado;

            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(headerRow);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExcelReportFileNameBuilder.Build(nombreNegocio, "Reporte Cobros", generatedAt));
        }

        private static CobroCreateRequest MapRequest(Cobro cobro, bool actualizarNotas, string? notasTexto) =>
            new()
            {
                FechaCobro = cobro.FechaCobro,
                NombreCliente = cobro.NombreCliente,
                ClienteId = cobro.ClienteId,
                FuncionarioId = cobro.FuncionarioId,
                ServicioId = cobro.ServicioId,
                ProductoId = cobro.ProductoId,
                Monto = cobro.Monto,
                MetodoPago = cobro.MetodoPago,
                Observaciones = cobro.Observaciones,
                ActualizarNotasServicio = actualizarNotas,
                NotasServicioTexto = notasTexto
            };

        private static CobroUpdateRequest MapUpdateRequest(Cobro cobro) =>
            new()
            {
                IdCobro = cobro.IdCobro,
                FechaCobro = cobro.FechaCobro,
                NombreCliente = cobro.NombreCliente,
                ClienteId = cobro.ClienteId,
                FuncionarioId = cobro.FuncionarioId,
                ServicioId = cobro.ServicioId,
                Monto = cobro.Monto,
                MetodoPago = cobro.MetodoPago,
                Observaciones = cobro.Observaciones
            };
    }
}
