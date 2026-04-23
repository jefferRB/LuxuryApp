using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Services.Finanzas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]
    public class EgresosController : Controller
    {
        private readonly IEgresoService _egresoService;
        private readonly IEgresoQueryService _egresoQueryService;

        public EgresosController(
            IEgresoService egresoService,
            IEgresoQueryService egresoQueryService)
        {
            _egresoService = egresoService;
            _egresoQueryService = egresoQueryService;
        }

        public async Task<IActionResult> Index(EgresoFiltroViewModel filtros)
        {
            var vm = await _egresoQueryService.BuildIndexViewModelAsync(filtros);
            return View(vm);
        }

        public async Task<IActionResult> Create()
        {
            var vm = await _egresoQueryService.BuildCreateViewModelAsync();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EgresoViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(await _egresoQueryService.BuildCreateViewModelAsync(vm.Egreso));
            }

            try
            {
                await _egresoService.RegistrarAsync(MapRequest(vm.Egreso));
                return RedirectToAction(nameof(Index));
            }
            catch (EgresoValidationException ex)
            {
                ModelState.AddModelError(ex.ModelStateKey ?? string.Empty, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            return View(await _egresoQueryService.BuildCreateViewModelAsync(vm.Egreso));
        }

        public async Task<IActionResult> ExportarExcel(EgresoFiltroViewModel filtros)
        {
            var reporte = await _egresoQueryService.BuildIndexViewModelAsync(
                filtros,
                includeFilterOptions: false);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Reporte Egresos");

            var colorNegro = XLColor.FromHtml("#1C1C1C");
            var colorDorado = XLColor.FromHtml("#C6A55C");
            var colorGrisSuave = XLColor.FromHtml("#F5F5F5");
            const string excelCurrencyFormat = "CRC #,##0.00";

            ws.Range("A1:E1").Merge();
            ws.Cell("A1").Value = "LUXE CENTRO DE BELLEZA";
            ws.Cell("A1").Style.Font.FontSize = 20;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Cell("A1").Style.Font.FontColor = colorDorado;
            ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range("A2:E2").Merge();
            ws.Cell("A2").Value = "Reporte Financiero de Egresos";
            ws.Cell("A2").Style.Font.FontSize = 14;
            ws.Cell("A2").Style.Font.Bold = true;
            ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Range("A3:E3").Merge();
            ws.Cell("A3").Value = $"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}";
            ws.Cell("A3").Style.Font.Italic = true;
            ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var kpiRow = 5;

            ws.Cell(kpiRow, 1).Value = "Cantidad Registros";
            ws.Cell(kpiRow, 2).Value = reporte.CantidadRegistros;
            ws.Cell(kpiRow, 3).Value = "Monto Total";
            ws.Cell(kpiRow, 4).Value = reporte.TotalEgresos;

            var kpiRange = ws.Range(kpiRow, 1, kpiRow, 5);
            kpiRange.Style.Fill.BackgroundColor = colorGrisSuave;
            kpiRange.Style.Font.Bold = true;
            ws.Cell(kpiRow, 4).Style.NumberFormat.Format = excelCurrencyFormat;

            var headerRow = 7;

            ws.Cell(headerRow, 1).Value = "Fecha";
            ws.Cell(headerRow, 2).Value = "Categoria";
            ws.Cell(headerRow, 3).Value = "Detalle";
            ws.Cell(headerRow, 4).Value = "Monto";
            ws.Cell(headerRow, 5).Value = "Metodo Pago";

            var header = ws.Range(headerRow, 1, headerRow, 5);
            header.Style.Fill.BackgroundColor = colorNegro;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Font.Bold = true;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var fila = headerRow + 1;

            foreach (var egreso in reporte.Egresos)
            {
                ws.Cell(fila, 1).Value = egreso.FechaEgreso;
                ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                ws.Cell(fila, 2).Value = egreso.CategoriaNombre;
                ws.Cell(fila, 3).Value = egreso.Detalle;
                ws.Cell(fila, 4).Value = egreso.Monto;
                ws.Cell(fila, 4).Style.NumberFormat.Format = excelCurrencyFormat;
                ws.Cell(fila, 5).Value = egreso.MetodoPago;
                fila++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"LuxeReporteEgresos_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        private static EgresoCreateRequest MapRequest(Egreso egreso) =>
            new()
            {
                FechaEgreso = egreso.FechaEgreso,
                Detalle = egreso.Detalle,
                Monto = egreso.Monto,
                MetodoPago = egreso.MetodoPago,
                CategoriaId = egreso.CategoriaId
            };
    }
}
