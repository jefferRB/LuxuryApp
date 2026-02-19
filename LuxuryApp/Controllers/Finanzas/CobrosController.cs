using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Productos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]

    public class CobrosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CobrosController(ApplicationDbContext context)
        {
            _context = context;
        }

        // LISTADO GENERAL COBROS

        public async Task<IActionResult> Index(CobroFiltroViewModel filtros)
        {
            var cobros = await ObtenerCobrosFiltrados(filtros);
            var totalCobrado = cobros.Sum(c => c.Monto);
            var cantidadServicios = cobros.Count;
            var totalImpuestos = totalCobrado * 0.13m;
            var totalNeto = totalCobrado - totalImpuestos;
            var pagoColaboradores = totalNeto * 0.50m;
            


            var vm = new CobroIndexViewModel
            {
                Cobros = cobros,
                Filtros = filtros,
                TotalCobrado = totalCobrado,
                CantidadServicios = cantidadServicios,
                TotalImpuestos = totalImpuestos,
                PagoColaboradores = pagoColaboradores,
                TotalNeto = totalNeto,

                Barberos = await _context.Barberos
                    .Where(b => b.Activo)
                    .Select(b => new SelectListItem
                    {
                        Value = b.Id.ToString(),
                        Text = b.Nombre
                    }).ToListAsync(),

                MetodosPago = ObtenerMetodosPago()
            };

            return View(vm);
        }



        // CREAR COBRO (GET)
        public async Task<IActionResult> Create()
        {
            var now = DateTime.Now;
            var vm = new CobroViewModel
            {
                Cobro = new Cobro
                {
                    FechaCobro = new DateTime(
                now.Year,
                now.Month,
                now.Day,
                now.Hour,
                now.Minute,
                0 // 🔥 elimina segundos y milisegundos
            )
                },


                Barberos = await _context.Barberos
                    .Where(b => b.Activo)
                    .Select(b => new SelectListItem
                    {
                        Value = b.Id.ToString(),
                        Text = b.Nombre
                    }).ToListAsync(),

                Productos = await _context.Productos
                  .Where(p => p.Activo && p.CantidadProducto > 0)
                  .Select(p => new SelectListItem
                  {
                     Value = p.IdProducto.ToString(),
                     Text = p.NombreProducto
                  }).ToListAsync(),

                Servicios = await _context.Servicios
                    .Where(s => s.Activo)
                    .Select(s => new SelectListItem
                    {
                        Value = s.Id.ToString(),
                        Text = s.Nombre
                    }).ToListAsync(),

                MetodosPago = ObtenerMetodosPago()
            };

            return View(vm);
        }

        // ====================================
        // CREAR COBRO (POST)
        // ====================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CobroViewModel vm)
        {
            // Debe elegir servicio o producto
            if (!vm.Cobro.ServicioId.HasValue && !vm.Cobro.ProductoId.HasValue)
            {
                ModelState.AddModelError("", "Debe seleccionar un servicio o un producto.");
            }

            if (!ModelState.IsValid)
                return await RecargarCombos(vm);

            // Normalizar fecha
            vm.Cobro.FechaCobro = new DateTime(
                vm.Cobro.FechaCobro.Year,
                vm.Cobro.FechaCobro.Month,
                vm.Cobro.FechaCobro.Day,
                vm.Cobro.FechaCobro.Hour,
                vm.Cobro.FechaCobro.Minute,
                0
            );

            // Solo puede existir uno
            if (vm.Cobro.ServicioId.HasValue)
                vm.Cobro.ProductoId = null;

            if (vm.Cobro.ProductoId.HasValue)
                vm.Cobro.ServicioId = null;

            // ===============================
            // VALIDAR STOCK SI ES PRODUCTO
            // ===============================
            Producto producto = null;

            if (vm.Cobro.ProductoId.HasValue)
            {
                producto = await _context.Productos
                    .FirstOrDefaultAsync(p => p.IdProducto == vm.Cobro.ProductoId.Value);

                if (producto == null)
                {
                    ModelState.AddModelError("", "Producto no encontrado.");
                    return await RecargarCombos(vm);
                }

                if (producto.CantidadProducto <= 0)
                {
                    ModelState.AddModelError("", $"No hay stock disponible para {producto.NombreProducto}");
                    return await RecargarCombos(vm);
                }

                // Asegurar monto correcto
                vm.Cobro.Monto = producto.PrecioProducto;
            }

            // ===============================
            // GUARDAR COBRO
            // ===============================
            _context.Cobros.Add(vm.Cobro);
            await _context.SaveChangesAsync();

            // ===============================
            // REBAJAR INVENTARIO SI ES PRODUCTO
            // ===============================
            if (producto != null)
            {
                int stockAnterior = producto.CantidadProducto;

                producto.CantidadProducto -= 1;

                var detalle = new DetalleCobroProducto
                {
                    CobroId = vm.Cobro.IdCobro,
                    ProductoId = producto.IdProducto,
                    Cantidad = 1,
                    PrecioUnitario = producto.PrecioProducto,
                    Subtotal = producto.PrecioProducto
                };

                _context.DetalleCobroProductos.Add(detalle);

                var movimiento = new MovimientoInventario
                {
                    ProductoId = producto.IdProducto,
                    FechaMovimiento = DateTime.Now,
                    TipoMovimiento = "VENTA",
                    Cantidad = 1,
                    StockAnterior = stockAnterior,
                    StockNuevo = producto.CantidadProducto,
                    Observacion = $"Venta en cobro #{vm.Cobro.IdCobro}"
                };

                _context.MovimientosInventario.Add(movimiento);

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }


        // AJAX → OBTENER PRECIO SERVICIO

        [HttpGet]
        public async Task<JsonResult> ObtenerPrecioServicio(int id)
        {
            var servicio = await _context.Servicios
                .Where(s => s.Id == id)
                .Select(s => new { s.Precio })
                .FirstOrDefaultAsync();

            return Json(servicio);
        }

        [HttpGet]
        public async Task<JsonResult> ObtenerPrecioProducto(int id)
        {
            var producto = await _context.Productos
                .Where(p => p.IdProducto == id)
                .Select(p => new { precio = p.PrecioProducto })
                .FirstOrDefaultAsync();

            return Json(producto);
        }



        // MÉTODOS DE PAGO

        private List<SelectListItem> ObtenerMetodosPago()
        {
            return new List<SelectListItem>
            {
                new SelectListItem { Value = "EFECTIVO", Text = "Efectivo" },
                new SelectListItem { Value = "TARJETA", Text = "Tarjeta" },
                new SelectListItem { Value = "SINPE", Text = "Sinpe" }
            };
        }





        public async Task<IActionResult> ExportarExcel(CobroFiltroViewModel filtros)
        {
            var cobros = await ObtenerCobrosFiltrados(filtros);

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Reporte Cobros");

                var colorNegro = XLColor.FromHtml("#1C1C1C");
                var colorDorado = XLColor.FromHtml("#C6A55C");
                var colorGrisSuave = XLColor.FromHtml("#F5F5F5");

                ws.Range("A1:F1").Merge();
                ws.Cell("A1").Value = "LUXE CENTRO DE BELLEZA";
                ws.Cell("A1").Style.Font.FontSize = 20;
                ws.Cell("A1").Style.Font.Bold = true;
                ws.Cell("A1").Style.Font.FontColor = colorDorado;
                ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Range("A2:F2").Merge();
                ws.Cell("A2").Value = "Reporte Financiero de Cobros";
                ws.Cell("A2").Style.Font.FontSize = 14;
                ws.Cell("A2").Style.Font.Bold = true;
                ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Range("A3:F3").Merge();
                ws.Cell("A3").Value = $"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}";
                ws.Cell("A3").Style.Font.Italic = true;
                ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int kpiRow = 5;

                ws.Cell(kpiRow, 1).Value = "Cantidad Cobros";
                ws.Cell(kpiRow, 2).Value = cobros.Count();

                ws.Cell(kpiRow, 3).Value = "Monto Total";
                ws.Cell(kpiRow, 4).Value = cobros.Sum(x => x.Monto);

                var kpiRange = ws.Range(kpiRow, 1, kpiRow, 6);
                kpiRange.Style.Fill.BackgroundColor = colorGrisSuave;
                kpiRange.Style.Font.Bold = true;

                ws.Cell(kpiRow, 4).Style.NumberFormat.Format = "₡ #,##0.00";

                int headerRow = 7;

                ws.Cell(headerRow, 1).Value = "Fecha";
                ws.Cell(headerRow, 2).Value = "Cliente";
                ws.Cell(headerRow, 3).Value = "Barbero";
                ws.Cell(headerRow, 4).Value = "Detalle";
                ws.Cell(headerRow, 5).Value = "Monto";
                ws.Cell(headerRow, 6).Value = "Método Pago";

                var header = ws.Range(headerRow, 1, headerRow, 6);

                header.Style.Fill.BackgroundColor = colorNegro;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Font.Bold = true;
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int fila = headerRow + 1;

                foreach (var c in cobros)
                {
                    ws.Cell(fila, 1).Value = c.FechaCobro;
                    ws.Cell(fila, 1).Style.DateFormat.Format = "dd/MM/yyyy";

                    ws.Cell(fila, 2).Value = c.NombreCliente;
                    ws.Cell(fila, 3).Value = c.Barbero?.Nombre;

                    // 🔥 NUEVA LOGICA SERVICIO / PRODUCTO
                    string detalle = c.Servicio != null
                        ? $"Servicio: {c.Servicio.Nombre}"
                        : c.Producto != null
                            ? $"Producto: {c.Producto.NombreProducto}"
                            : "-";

                    ws.Cell(fila, 4).Value = detalle;

                    ws.Cell(fila, 5).Value = c.Monto;
                    ws.Cell(fila, 5).Style.NumberFormat.Format = "₡ #,##0.00";

                    ws.Cell(fila, 6).Value = c.MetodoPago;

                    fila++;
                }

                var dataRange = ws.Range(headerRow + 1, 1, fila - 1, 6);

                dataRange.AddConditionalFormat()
                    .WhenIsTrue("MOD(ROW(),2)=0")
                    .Fill.SetBackgroundColor(colorGrisSuave);

                ws.Cell(fila, 4).Value = "TOTAL GENERAL";
                ws.Cell(fila, 4).Style.Font.Bold = true;

                ws.Cell(fila, 5).FormulaA1 = $"SUM(E{headerRow + 1}:E{fila - 1})";
                ws.Cell(fila, 5).Style.NumberFormat.Format = "₡ #,##0.00";
                ws.Cell(fila, 5).Style.Font.Bold = true;
                ws.Cell(fila, 5).Style.Font.FontColor = colorDorado;

                ws.Columns().AdjustToContents();

                ws.Range(headerRow, 1, fila - 1, 6).SetAutoFilter();
                ws.SheetView.FreezeRows(headerRow);

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"LuxeReporteCobros_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                    );
                }
            }
        }



        private async Task<List<Cobro>> ObtenerCobrosFiltrados(CobroFiltroViewModel filtros)
        {
            var query = _context.Cobros
                .Include(c => c.Barbero)
                .Include(c => c.Servicio)
                .Include(c => c.Producto)
                .AsQueryable();

            if (filtros.BarberoId.HasValue)
                query = query.Where(c => c.BarberoId == filtros.BarberoId);

            if (!string.IsNullOrEmpty(filtros.MetodoPago))
                query = query.Where(c => c.MetodoPago == filtros.MetodoPago);

            if (!string.IsNullOrEmpty(filtros.VistaTiempo))
            {
                var hoy = DateTime.Today;

                switch (filtros.VistaTiempo)
                {
                    case "todo":
                        // No aplicar filtro
                        break;

                    case "dia":
                        query = query.Where(c => c.FechaCobro.Date == hoy);
                        break;

                    case "semana":
                        var diff = (7 + (hoy.DayOfWeek - DayOfWeek.Monday)) % 7;
                        var inicioSemana = hoy.AddDays(-diff).Date;
                        var finSemana = inicioSemana.AddDays(7);

                        query = query.Where(c =>
                            c.FechaCobro >= inicioSemana &&
                            c.FechaCobro < finSemana);
                        break;

                    case "mes":
                        query = query.Where(c =>
                            c.FechaCobro.Month == hoy.Month &&
                            c.FechaCobro.Year == hoy.Year);
                        break;

                    case "anio":
                        query = query.Where(c =>
                            c.FechaCobro.Year == hoy.Year);
                        break;

                    case "fechas":

                        if (filtros.FechaInicio.HasValue)
                            query = query.Where(c =>
                                c.FechaCobro >= filtros.FechaInicio.Value);

                        if (filtros.FechaFin.HasValue)
                        {
                            var fin = filtros.FechaFin.Value.AddDays(1);

                            query = query.Where(c =>
                                c.FechaCobro < fin);
                        }

                        break;
                }
            }
            // FILTRO SERVICIO / PRODUCTO
            if (filtros.MostrarServicios && !filtros.MostrarProductos)
            {
                query = query.Where(c => c.ServicioId != null);
            }

            if (!filtros.MostrarServicios && filtros.MostrarProductos)
            {
                query = query.Where(c => c.ProductoId != null);
            }

            // Si ambos están en false → no mostrar nada
            if (!filtros.MostrarServicios && !filtros.MostrarProductos)
            {
                query = query.Where(c => false);
            }

            return await query
                .OrderByDescending(c => c.FechaCobro)
                .ToListAsync();
        }


        private async Task<IActionResult> RecargarCombos(CobroViewModel vm)
        {
            vm.Barberos = await _context.Barberos
                .Where(b => b.Activo)
                .Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.Nombre
                }).ToListAsync();

            vm.Servicios = await _context.Servicios
                .Where(s => s.Activo)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Nombre
                }).ToListAsync();

            vm.Productos = await _context.Productos
                .Where(p => p.Activo && p.CantidadProducto > 0)
                .Select(p => new SelectListItem
                {
                    Value = p.IdProducto.ToString(),
                    Text = p.NombreProducto
                }).ToListAsync();

            vm.MetodosPago = ObtenerMetodosPago();

            return View(vm);
        }


    }
}
