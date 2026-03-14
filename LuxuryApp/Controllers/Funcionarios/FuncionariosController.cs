using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Productos;
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

        public FuncionariosController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var funcionarios = await _context.Funcionarios
                  .Include(f => f.Puesto)
                .OrderBy(f => f.Nombre)
                .ToListAsync();

            return View(funcionarios);
        }
     
        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.Puestos = _context.Puestos
                .Where(p => p.Activo)
                .OrderBy(p => p.NombrePuesto)
                .ToList();

            var funcionario = new Funcionario
            {
                FechaIngreso = DateTime.Today,
                Activo = true
            };

            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Funcionario funcionario)
        {
            bool nombreExiste = await _context.Funcionarios
                .AnyAsync(f => f.Nombre == funcionario.Nombre);

            if (nombreExiste)
            {
                ModelState.AddModelError("Nombre", "Ya existe un funcionario con ese nombre.");
            }

            if (!ModelState.IsValid)
                return View(funcionario);

            _context.Funcionarios.Add(funcionario);
            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario creado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
                return NotFound();

            ViewBag.Puestos = await _context.Puestos
                .Where(p => p.Activo)
                .OrderBy(p => p.NombrePuesto)
                .ToListAsync();

            return View(funcionario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Funcionario funcionario)
        {
            if (!ModelState.IsValid)
                return View(funcionario);

            var funcionarioDB = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == funcionario.IdFuncionario);

            if (funcionarioDB == null)
                return NotFound();

            funcionarioDB.Nombre = funcionario.Nombre;
            funcionarioDB.Telefono = funcionario.Telefono;
            funcionarioDB.IdPuesto = funcionario.IdPuesto;
            funcionarioDB.ColorCalendario = funcionario.ColorCalendario;
            funcionarioDB.PorcentajeGanancia = funcionario.PorcentajeGanancia;
            funcionarioDB.PorcentajeProducto = funcionario.PorcentajeProducto;
            funcionarioDB.FechaIngreso = funcionario.FechaIngreso;
            funcionarioDB.Activo = funcionario.Activo;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario actualizado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ============================
        // ELIMINAR (Soft delete recomendado)
        // ============================

        [HttpPost]
        public async Task<IActionResult> Eliminar(int IdFuncionario)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == IdFuncionario);

            if (funcionario == null)
                return NotFound();

            _context.Funcionarios.Remove(funcionario);

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario eliminado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // ============================
        // ACTIVAR
        // ============================

        [HttpPost]
        public async Task<IActionResult> Activar(int id)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
                return NotFound();

            funcionario.Activo = true;

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Funcionario activado.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetActivos()
        {
            var funcionarios = await _context.Funcionarios
                .Where(f => f.Activo)
                .OrderBy(f => f.Nombre)
                .Select(f => new
                {
                    id = f.IdFuncionario,
                    nombre = f.Nombre
                })
                .ToListAsync();

            return Json(funcionarios);
        }


        public async Task<IActionResult> PagosSemana(DateTime? fecha)
        {
            var hoy = (fecha ?? DateTime.Today).Date;

            var diff = (7 + (hoy.DayOfWeek - DayOfWeek.Monday)) % 7;
            var inicioSemana = hoy.AddDays(-diff).Date;
            var finSemana = inicioSemana.AddDays(6).Date;

            var funcionarios = await _context.Funcionarios
                .Where(f => f.Activo)
                .ToListAsync();

            var cobros = await _context.Cobros
            .Include(c => c.Producto)
            .Where(c => c.FechaCobro.Date >= inicioSemana && c.FechaCobro.Date <= finSemana)
            .ToListAsync();

            var pagosSemana = await _context.PagosFuncionarios
                .Where(p => p.InicioSemana.Date == inicioSemana && p.FinSemana.Date == finSemana)
                .ToListAsync();

            var pagos = funcionarios.Select(f =>
            {
                var serviciosFuncionario = cobros
                    .Where(c => c.FuncionarioId == f.IdFuncionario)
                    .ToList();

                var total = serviciosFuncionario.Sum(c => c.Monto);

                var servicios = serviciosFuncionario
                .Where(c => c.ServicioId != null)
                .ToList();

                var productos = serviciosFuncionario
                    .Where(c => c.ProductoId != null)
                    .ToList();

                var totalServicios = servicios.Sum(c => c.Monto);
                var totalProductos = productos.Sum(c => c.Monto);

                var impuestos = (totalServicios + totalProductos) * 0.13m;

                var netoServicios = totalServicios - (totalServicios * 0.13m);
                var netoProductos = totalProductos - (totalProductos * 0.13m);

                var pagoServicios = netoServicios * (f.PorcentajeGanancia / 100);
                var pagoProductos = netoProductos * (f.PorcentajeProducto / 100);

                var pagoFuncionario = pagoServicios + pagoProductos;

                var neto = total - impuestos;

                var totalPagado = pagosSemana
                    .Where(p => p.FuncionarioId == f.IdFuncionario)
                    .Sum(p => p.MontoPagado);

                var pendiente = pagoFuncionario - totalPagado;

                var detalleDias = Enumerable.Range(0, 7)
                .Select(i =>
                {
                    var fechaDia = inicioSemana.AddDays(i).Date;

                    var serviciosDia = servicios
                        .Where(c => c.FechaCobro.Date == fechaDia)
                        .ToList();

                    return new DetalleDiaVM
                    {
                        Dia = fechaDia.ToString("dddd"),
                        CantidadServicios = serviciosDia.Count,
                        Monto = serviciosDia.Sum(s => s.Monto)
                    };
                }).ToList();

                var productosVendidos = productos
                 .Select(p => new ProductoVendidoVM
                 {
                     Fecha = p.FechaCobro,
                     NombreProducto = p.Producto?.NombreProducto ?? "Producto",
                     Precio = p.Monto,
                     GananciaFuncionario = (p.Monto - (p.Monto * 0.13m)) * (f.PorcentajeProducto / 100)
                 })
                 .OrderByDescending(p => p.Fecha)
                 .ToList();


                return new PagoFuncionarioVM
                {
                    FuncionarioId = f.IdFuncionario,
                    Nombre = f.Nombre,

                    TotalGenerado = total,
                    Impuestos = impuestos,
                    TotalNeto = neto,

                    Porcentaje = f.PorcentajeGanancia,

                    PorcentajeProducto = f.PorcentajeProducto,

                    PagoFinal = pagoFuncionario,

                    MontoPagado = totalPagado,

                    MontoPendiente = pendiente,

                    DetalleDias = detalleDias,

                    ProductosVendidos = productosVendidos,

                    HistorialPagos = pagosSemana
                        .Where(p => p.FuncionarioId == f.IdFuncionario)
                        .OrderByDescending(p => p.FechaPago)
                        .ToList()
                };

            }).ToList();

            var totalGeneradoServicios = cobros
    .Where(c => c.ServicioId != null)
    .Sum(c => c.Monto);

            var totalGeneradoProductos = cobros
                .Where(c => c.ProductoId != null)
                .Sum(c => c.Monto);

            var totalGeneradoGeneral = totalGeneradoServicios + totalGeneradoProductos;

            var totalImpuestosGeneral = totalGeneradoGeneral * 0.13m;

            var totalSinImpuestosGeneral = totalGeneradoGeneral - totalImpuestosGeneral;

            var totalPagadoGeneral = pagos.Sum(p => p.MontoPagado);

            var totalPendienteGeneral = pagos.Sum(p => p.MontoPendiente);

            // Ganancia del negocio
            var gananciaNegocio = totalSinImpuestosGeneral - pagos.Sum(p => p.PagoFinal);

            ViewBag.TotalGeneradoServicios = totalGeneradoServicios;

            ViewBag.TotalGeneradoProductos = totalGeneradoProductos;

            ViewBag.TotalGeneradoGeneral = totalGeneradoGeneral;

            ViewBag.TotalImpuestosGeneral = totalImpuestosGeneral;

            ViewBag.TotalSinImpuestosGeneral = totalSinImpuestosGeneral;

            ViewBag.TotalPagadoGeneral = totalPagadoGeneral;

            ViewBag.TotalPendienteGeneral = totalPendienteGeneral;

            ViewBag.GananciaNegocio = gananciaNegocio;

            ViewBag.InicioSemana = inicioSemana;
            ViewBag.FinSemana = finSemana;

            return View(pagos);
        }

        [HttpPost]
        public async Task<IActionResult> RegistrarPago(
          int funcionarioId,
          decimal monto,
          DateTime inicioSemana,
          DateTime finSemana,
          string? observacion)
        {
            var pago = new PagoFuncionario
            {
                FuncionarioId = funcionarioId,
                MontoPagado = monto,
                InicioSemana = inicioSemana,
                FinSemana = finSemana,
                FechaPago = DateTime.Now,
                Observacion = observacion
            };

            _context.PagosFuncionarios.Add(pago);

            await _context.SaveChangesAsync();

            return RedirectToAction("PagosSemana", new { fecha = inicioSemana });
        }


        [HttpPost]
        public async Task<IActionResult> PagarTodaLaSemana(DateTime inicioSemana, DateTime finSemana)
        {
            var funcionarios = await _context.Funcionarios
                .Where(f => f.Activo)
                .ToListAsync();

            var cobros = await _context.Cobros
    .Include(c => c.Producto)
    .Where(c => c.FechaCobro.Date >= inicioSemana && c.FechaCobro.Date <= finSemana)
    .ToListAsync();

            foreach (var f in funcionarios)
            {
                var cobrosFuncionario = cobros
     .Where(c => c.FuncionarioId == f.IdFuncionario)
     .ToList();

                var servicios = cobrosFuncionario
                    .Where(c => c.ServicioId != null)
                    .ToList();

                var productos = cobrosFuncionario
                    .Where(c => c.ProductoId != null)
                    .ToList();

                var totalServicios = servicios.Sum(s => s.Monto);
                var totalProductos = productos.Sum(p => p.Monto);

                var netoServicios = totalServicios - (totalServicios * 0.13m);
                var netoProductos = totalProductos - (totalProductos * 0.13m);

                var pagoServicios = netoServicios * (f.PorcentajeGanancia / 100);
                var pagoProductos = netoProductos * (f.PorcentajeProducto / 100);

                var pagoFinal = pagoServicios + pagoProductos;

                var total = servicios.Sum(s => s.Monto);
                var impuestos = total * 0.13m;
                var neto = total - impuestos;

                

                var pagado = await _context.PagosFuncionarios
                    .Where(p => p.FuncionarioId == f.IdFuncionario &&
                                p.InicioSemana == inicioSemana &&
                                p.FinSemana == finSemana)
                    .SumAsync(p => p.MontoPagado);

                var pendiente = pagoFinal - pagado;

                if (pendiente > 0)
                {
                    var pago = new PagoFuncionario
                    {
                        FuncionarioId = f.IdFuncionario,
                        MontoPagado = pendiente,
                        InicioSemana = inicioSemana,
                        FinSemana = finSemana,
                        FechaPago = DateTime.Now,
                        Observacion = "Pago semanal automático"
                    };

                    _context.PagosFuncionarios.Add(pago);
                }
            }

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = "Todos los pagos pendientes de la semana fueron liquidados.";

            return RedirectToAction("PagosSemana", new { fecha = inicioSemana });
        }

        public async Task<IActionResult> ExportarPagosExcel(DateTime inicioSemana, DateTime finSemana)
        {
            var funcionarios = await _context.Funcionarios
                .Where(f => f.Activo)
                .ToListAsync();

            var cobros = await _context.Cobros
                .Where(c => c.FechaCobro.Date >= inicioSemana.Date && c.FechaCobro.Date <= finSemana.Date)
                .ToListAsync();

            var pagosRegistrados = await _context.PagosFuncionarios
                .Where(p => p.InicioSemana.Date == inicioSemana.Date && p.FinSemana.Date == finSemana.Date)
                .ToListAsync();

            var historialPagos = await _context.PagosFuncionarios
                .Include(p => p.Funcionario)
                .Where(p => p.InicioSemana.Date == inicioSemana.Date && p.FinSemana.Date == finSemana.Date)
                .OrderByDescending(p => p.FechaPago)
                .ToListAsync();

            var datos = funcionarios.Select(f =>
            {
                var serviciosFuncionario = cobros
                    .Where(c => c.FuncionarioId == f.IdFuncionario)
                    .ToList();

                var total = serviciosFuncionario.Sum(c => c.Monto);

                var impuestos = total * 0.13m;

                var neto = total - impuestos;

                var montoGenerado = neto * (f.PorcentajeGanancia / 100);

                var pagado = pagosRegistrados
                    .Where(p => p.FuncionarioId == f.IdFuncionario)
                    .Sum(p => p.MontoPagado);

                var pendiente = montoGenerado - pagado;

                return new
                {
                    Funcionario = f.Nombre,
                    TotalGenerado = total,
                    Impuestos = impuestos,
                    TotalNeto = neto,
                    Porcentaje = f.PorcentajeGanancia,
                    MontoGenerado = montoGenerado,
                    MontoPagado = pagado,
                    Pendiente = pendiente
                };
            }).ToList();

            var totalGeneradoSalon = datos.Sum(d => d.TotalGenerado);
            var totalPagadoFuncionarios = datos.Sum(d => d.MontoPagado);
            var gananciaSalon = totalGeneradoSalon - totalPagadoFuncionarios;

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Pagos Funcionarios");

                var colorNegro = XLColor.FromHtml("#1C1C1C");
                var colorDorado = XLColor.FromHtml("#C6A55C");
                var colorGris = XLColor.FromHtml("#F5F5F5");

                ws.Range("A1:H1").Merge();
                ws.Cell("A1").Value = "LUXE CENTRO DE BELLEZA";
                ws.Cell("A1").Style.Font.FontSize = 20;
                ws.Cell("A1").Style.Font.Bold = true;
                ws.Cell("A1").Style.Font.FontColor = colorDorado;
                ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Range("A2:H2").Merge();
                ws.Cell("A2").Value = "Reporte de Pagos a Funcionarios";
                ws.Cell("A2").Style.Font.FontSize = 14;
                ws.Cell("A2").Style.Font.Bold = true;
                ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Range("A3:H3").Merge();
                ws.Cell("A3").Value = $"Semana: {inicioSemana:dd/MM/yyyy} - {finSemana:dd/MM/yyyy}";
                ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int headerRow = 5;

                ws.Cell(headerRow, 1).Value = "Funcionario";
                ws.Cell(headerRow, 2).Value = "Total Generado";
                ws.Cell(headerRow, 3).Value = "Impuestos";
                ws.Cell(headerRow, 4).Value = "Total Neto";
                ws.Cell(headerRow, 5).Value = "%";
                ws.Cell(headerRow, 6).Value = "Monto Generado";
                ws.Cell(headerRow, 7).Value = "Monto Pagado";
                ws.Cell(headerRow, 8).Value = "Pendiente";

                var header = ws.Range(headerRow, 1, headerRow, 8);

                header.Style.Fill.BackgroundColor = colorNegro;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Font.Bold = true;
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                int fila = headerRow + 1;

                foreach (var d in datos)
                {
                    ws.Cell(fila, 1).Value = d.Funcionario;
                    ws.Cell(fila, 2).Value = d.TotalGenerado;
                    ws.Cell(fila, 3).Value = d.Impuestos;
                    ws.Cell(fila, 4).Value = d.TotalNeto;
                    ws.Cell(fila, 5).Value = d.Porcentaje;
                    ws.Cell(fila, 6).Value = d.MontoGenerado;
                    ws.Cell(fila, 7).Value = d.MontoPagado;
                    ws.Cell(fila, 8).Value = d.Pendiente;

                    ws.Range(fila, 2, fila, 4).Style.NumberFormat.Format = "₡ #,##0.00";
                    ws.Range(fila, 6, fila, 8).Style.NumberFormat.Format = "₡ #,##0.00";

                    fila++;
                }

                var dataRange = ws.Range(headerRow + 1, 1, fila - 1, 8);

                dataRange.AddConditionalFormat()
                    .WhenIsTrue("MOD(ROW(),2)=0")
                    .Fill.SetBackgroundColor(colorGris);

                ws.Cell(fila, 6).Value = "TOTAL PAGADO";
                ws.Cell(fila, 6).Style.Font.Bold = true;

                ws.Cell(fila, 7).FormulaA1 = $"SUM(G{headerRow + 1}:G{fila - 1})";
                ws.Cell(fila, 7).Style.NumberFormat.Format = "₡ #,##0.00";
                ws.Cell(fila, 7).Style.Font.Bold = true;
                ws.Cell(fila, 7).Style.Font.FontColor = colorDorado;

                fila += 3;

                ws.Cell(fila, 1).Value = "Resumen del Salón";
                ws.Cell(fila, 1).Style.Font.Bold = true;

                fila++;

                ws.Cell(fila, 1).Value = "Total generado por servicios";
                ws.Cell(fila, 2).Value = totalGeneradoSalon;

                fila++;

                ws.Cell(fila, 1).Value = "Total pagado a funcionarios";
                ws.Cell(fila, 2).Value = totalPagadoFuncionarios;

                fila++;

                ws.Cell(fila, 1).Value = "Ganancia del salón";
                ws.Cell(fila, 2).Value = gananciaSalon;

                ws.Range(fila - 2, 2, fila, 2).Style.NumberFormat.Format = "₡ #,##0.00";

                ws.Cell(fila, 2).Style.Font.Bold = true;
                ws.Cell(fila, 2).Style.Font.FontColor = colorDorado;

                fila += 3;

                ws.Cell(fila, 1).Value = "Historial de Pagos";
                ws.Cell(fila, 1).Style.Font.Bold = true;
                ws.Cell(fila, 1).Style.Font.FontSize = 14;

                fila++;

                ws.Cell(fila, 1).Value = "Funcionario";
                ws.Cell(fila, 2).Value = "Fecha";
                ws.Cell(fila, 3).Value = "Monto";
                ws.Cell(fila, 4).Value = "Observación";

                var headerHistorial = ws.Range(fila, 1, fila, 4);

                headerHistorial.Style.Fill.BackgroundColor = colorNegro;
                headerHistorial.Style.Font.FontColor = XLColor.White;
                headerHistorial.Style.Font.Bold = true;

                fila++;

                foreach (var pago in historialPagos)
                {
                    ws.Cell(fila, 1).Value = pago.Funcionario?.Nombre;
                    ws.Cell(fila, 2).Value = pago.FechaPago;
                    ws.Cell(fila, 3).Value = pago.MontoPagado;
                    ws.Cell(fila, 4).Value = pago.Observacion;

                    ws.Cell(fila, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                    ws.Cell(fila, 3).Style.NumberFormat.Format = "₡ #,##0.00";

                    fila++;
                }

                ws.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);

                    return File(
                        stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"LuxePagosFuncionarios_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                    );
                }
            }
        }




    }
}
