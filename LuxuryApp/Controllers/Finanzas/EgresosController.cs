using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    public class EgresosController : Controller
    {
         private readonly ApplicationDbContext _context;

        public EgresosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =============================
        // LISTADO GENERAL
        // =============================
        public async Task<IActionResult> Index(EgresoFiltroViewModel filtros)
        {
            var egresos = await ObtenerEgresosFiltrados(filtros);

            var vm = new EgresoIndexViewModel
            {
                Egresos = egresos,
                Filtros = filtros,
                TotalEgresos = egresos.Sum(x => x.Monto),
                CantidadRegistros = egresos.Count(),

                Categorias = await _context.Categorias
                    .Where(c => c.Activo)
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Nombre
                    }).ToListAsync(),

                MetodosPago = ObtenerMetodosPago()
            };

            return View(vm);
        }

        // =============================
        // CREATE GET
        // =============================
        public async Task<IActionResult> Create()
        {
            var now = DateTime.Now;

            var vm = new EgresoViewModel
            {
                Egreso = new Egreso
                {
                    FechaEgreso = new DateTime(
                        now.Year,
                        now.Month,
                        now.Day,
                        now.Hour,
                        now.Minute,
                        0)
                },

                Categorias = await _context.Categorias
                    .Where(c => c.Activo)
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Nombre
                    }).ToListAsync(),

                MetodosPago = ObtenerMetodosPago()
            };

            return View(vm);
        }

        // =============================
        // CREATE POST
        // =============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EgresoViewModel vm)
        {
            if (ModelState.IsValid)
            {
                vm.Egreso.FechaEgreso = new DateTime(
                    vm.Egreso.FechaEgreso.Year,
                    vm.Egreso.FechaEgreso.Month,
                    vm.Egreso.FechaEgreso.Day,
                    vm.Egreso.FechaEgreso.Hour,
                    vm.Egreso.FechaEgreso.Minute,
                    0);

                _context.Egresos.Add(vm.Egreso);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            vm.Categorias = await _context.Categorias
                .Where(c => c.Activo)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Nombre
                }).ToListAsync();

            vm.MetodosPago = ObtenerMetodosPago();

            return View(vm);
        }

        // =============================
        // MÉTODOS DE PAGO
        // =============================
        private List<SelectListItem> ObtenerMetodosPago()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "EFECTIVO", Text = "Efectivo" },
                new SelectListItem { Value = "TARJETA", Text = "Tarjeta" },
                new SelectListItem { Value = "SINPE", Text = "Sinpe" }
            };
        }

        // =============================
        // EXPORTAR EXCEL
        // =============================
        public async Task<IActionResult> ExportarExcel(EgresoFiltroViewModel filtros)
        {
            var egresos = await ObtenerEgresosFiltrados(filtros);

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Reporte Egresos");

                var colorNegro = XLColor.FromHtml("#1C1C1C");
                var colorDorado = XLColor.FromHtml("#C6A55C");
                var colorGrisSuave = XLColor.FromHtml("#F5F5F5");

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

                int kpiRow = 5;

                ws.Cell(kpiRow, 1).Value = "Cantidad Registros";
                ws.Cell(kpiRow, 2).Value = egresos.Count();

                ws.Cell(kpiRow, 3).Value = "Monto Total";
                ws.Cell(kpiRow, 4).Value = egresos.Sum(x => x.Monto);

                var kpiRange = ws.Range(kpiRow, 1, kpiRow, 5);
                kpiRange.Style.Fill.BackgroundColor = colorGrisSuave;
                kpiRange.Style.Font.Bold = true;

                ws.Cell(kpiRow, 4).Style.NumberFormat.Format = "₡ #,##0.00";

                int headerRow = 7;

                ws.Cell(headerRow, 1).Value = "Fecha";
                ws.Cell(headerRow, 2).Value = "Categoría";
                ws.Cell(headerRow, 3).Value = "Detalle";
                ws.Cell(headerRow, 4).Value = "Monto";
                ws.Cell(headerRow, 5).Value = "Método Pago";

                var header = ws.Range(headerRow, 1, headerRow, 5);

                header.Style.Fill.BackgroundColor = colorNegro;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Font.Bold = true;
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int fila = headerRow + 1;

                foreach (var e in egresos)
                {
                    ws.Cell(fila, 1).Value = e.FechaEgreso;
                    ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";

                    ws.Cell(fila, 2).Value = e.Categoria?.Nombre;
                    ws.Cell(fila, 3).Value = e.Detalle;

                    ws.Cell(fila, 4).Value = e.Monto;
                    ws.Cell(fila, 4).Style.NumberFormat.Format = "₡ #,##0.00";

                    ws.Cell(fila, 5).Value = e.MetodoPago;

                    fila++;
                }

                ws.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);

                    return File(
                        stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"LuxeReporteEgresos_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                    );
                }
            }
        }

        // =============================
        // FILTROS
        // =============================
        private async Task<List<Egreso>> ObtenerEgresosFiltrados(EgresoFiltroViewModel filtros)
        {
            var query = _context.Egresos
                .Include(e => e.Categoria)
                .AsQueryable();

            if (filtros.CategoriaId.HasValue)
                query = query.Where(e => e.CategoriaId == filtros.CategoriaId);

            if (!string.IsNullOrEmpty(filtros.MetodoPago))
                query = query.Where(e => e.MetodoPago == filtros.MetodoPago);

            if (!string.IsNullOrEmpty(filtros.VistaTiempo))
            {
                var hoy = DateTime.Today;

                switch (filtros.VistaTiempo)
                {
                    case "dia":
                        query = query.Where(e => e.FechaEgreso.Date == hoy);
                        break;

                    case "mes":
                        query = query.Where(e =>
                            e.FechaEgreso.Month == hoy.Month &&
                            e.FechaEgreso.Year == hoy.Year);
                        break;

                    case "anio":
                        query = query.Where(e =>
                            e.FechaEgreso.Year == hoy.Year);
                        break;

                    case "fechas":
                        if (filtros.FechaInicio.HasValue)
                            query = query.Where(e =>
                                e.FechaEgreso >= filtros.FechaInicio.Value);

                        if (filtros.FechaFin.HasValue)
                        {
                            var fin = filtros.FechaFin.Value.AddDays(1);
                            query = query.Where(e =>
                                e.FechaEgreso < fin);
                        }
                        break;
                }
            }

            return await query
                .OrderByDescending(e => e.FechaEgreso)
                .ToListAsync();
        }
    }
}
