using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using System.Security.Claims;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Productos;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
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
        private readonly ILogger<FuncionariosController> _logger;

        public FuncionariosController(
            ApplicationDbContext context,
            ILiquidacionSemanalService liquidacionSemanalService,
            ILogger<FuncionariosController> logger)
        {
            _context = context;
            _liquidacionSemanalService = liquidacionSemanalService;
            _logger = logger;
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
                nameof(Funcionario.FechaIngreso))]
            Funcionario funcionario)
        {
            NormalizeFuncionario(funcionario);

            var plan = await ResolvePlanAsync();
            if (plan == null && !UserIsPlatformSuperAdmin())
            {
                TempData["Error"] = "No fue posible resolver el acceso comercial del tenant para validar el límite de funcionarios.";
                return RedirectToAction("Index");
            }

            var totalFuncionarios = await _context.Funcionarios.CountAsync();

            if (plan?.MaxFuncionarios.HasValue == true &&
                totalFuncionarios >= plan.MaxFuncionarios.Value)
            {
                TempData["Error"] = $"Has alcanzado el límite de funcionarios de tu plan ({plan.Nombre}).";
                return RedirectToAction("Index");
            }

            bool nombreExiste = await _context.Funcionarios
                .AnyAsync(f => f.Nombre == funcionario.Nombre);

            if (nombreExiste)
            {
                ModelState.AddModelError("Nombre", "Ya existe un funcionario con ese nombre.");
            }

            var puesto = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == funcionario.IdPuesto && p.Activo);

            if (puesto == null)
            {
                ModelState.AddModelError(nameof(funcionario.IdPuesto), "El puesto seleccionado no existe o no pertenece al tenant actual.");
            }

            if (!ModelState.IsValid)
            {
                await CargarPuestosAsync();
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
                ModelState.AddModelError(string.Empty, "No fue posible guardar el funcionario. Verifica los datos e inténtalo de nuevo.");
                await CargarPuestosAsync();
                return View(funcionario);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueó la creación del funcionario {Nombre}.", funcionario.Nombre);
                ModelState.AddModelError(string.Empty, "No fue posible guardar el funcionario por una validación de seguridad o consistencia.");
                await CargarPuestosAsync();
                return View(funcionario);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == id);

            if (funcionario == null)
                return NotFound();

            await CargarPuestosAsync();

            return View(funcionario);
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
                nameof(Funcionario.FechaIngreso))]
            Funcionario funcionario)
        {
            NormalizeFuncionario(funcionario);

            if (!ModelState.IsValid)
            {
                await CargarPuestosAsync();
                return View(funcionario);
            }

            var funcionarioDB = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == funcionario.IdFuncionario);

            if (funcionarioDB == null)
                return NotFound();

            var puesto = await _context.Puestos
                .FirstOrDefaultAsync(p => p.IdPuesto == funcionario.IdPuesto && p.Activo);

            if (puesto == null)
            {
                ModelState.AddModelError(nameof(funcionario.IdPuesto), "El puesto seleccionado no existe o no pertenece al tenant actual.");
                await CargarPuestosAsync();
                return View(funcionario);
            }

            funcionarioDB.Nombre = funcionario.Nombre;
            funcionarioDB.Telefono = funcionario.Telefono;
            funcionarioDB.IdPuesto = funcionario.IdPuesto;
            funcionarioDB.ColorCalendario = funcionario.ColorCalendario;
            funcionarioDB.PorcentajeGanancia = funcionario.PorcentajeGanancia;
            funcionarioDB.PorcentajeProducto = funcionario.PorcentajeProducto;
            funcionarioDB.FechaIngreso = funcionario.FechaIngreso;
            funcionarioDB.Activo = funcionario.Activo;

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
                await CargarPuestosAsync();
                return View(funcionario);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Guard bloqueó la edición del funcionario {FuncionarioId}.", funcionario.IdFuncionario);
                ModelState.AddModelError(string.Empty, "No fue posible actualizar el funcionario por una validación de seguridad o consistencia.");
                await CargarPuestosAsync();
                return View(funcionario);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(int IdFuncionario)
        {
            var funcionario = await _context.Funcionarios
                .FirstOrDefaultAsync(f => f.IdFuncionario == IdFuncionario);

            if (funcionario == null)
                return NotFound();

            var tieneCitas = await _context.Citas.AnyAsync(c => c.FuncionarioId == IdFuncionario);
            var tieneCobros = await _context.Cobros.AnyAsync(c => c.FuncionarioId == IdFuncionario);
            var tienePagos = await _context.PagosFuncionarios.AnyAsync(p => p.FuncionarioId == IdFuncionario);
            var tieneLiquidaciones = await _context.LiquidacionesSemanalesDetalle.AnyAsync(d => d.FuncionarioId == IdFuncionario);

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
                _logger.LogError(ex, "Error al eliminar funcionario {FuncionarioId}.", IdFuncionario);
                TempData["Error"] = "No fue posible eliminar el funcionario porque tiene relaciones activas.";
            }

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
                    nombre = f.Nombre,
                    colorCalendario = f.ColorCalendario
                })
                .ToListAsync();

            return Json(funcionarios);
        }


        public async Task<IActionResult> PagosSemana(DateTime? fecha)
        {
            var resumen = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(
                fecha ?? DateTime.Today,
                HttpContext.RequestAborted);

            ViewBag.TotalGeneradoServicios = resumen.TotalGeneradoServicios;
            ViewBag.TotalGeneradoProductos = resumen.TotalGeneradoProductos;
            ViewBag.TotalGeneradoGeneral = resumen.TotalGeneradoGeneral;
            ViewBag.TotalImpuestosGeneral = resumen.TotalImpuestosGeneral;
            ViewBag.TotalSinImpuestosGeneral = resumen.TotalSinImpuestosGeneral;
            ViewBag.TotalPagadoGeneral = resumen.TotalPagadoGeneral;
            ViewBag.TotalPendienteGeneral = resumen.TotalPendienteGeneral;
            ViewBag.GananciaNegocio = resumen.GananciaNegocio;
            ViewBag.InicioSemana = resumen.InicioSemana;
            ViewBag.FinSemana = resumen.FinSemana;
            ViewBag.MetodosPago = ObtenerMetodosPago();

            return View(resumen.Funcionarios);
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
                return RedirectToAction("PagosSemana", new { fecha = inicioSemana.ToString("yyyy-MM-dd") });
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
                _logger.LogWarning(ex, "Validación de negocio al registrar pago semanal para funcionario {FuncionarioId}.", funcionarioId);
                TempData["Error"] = ex.Message;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al registrar pago semanal para funcionario {FuncionarioId}.", funcionarioId);
                TempData["Error"] = "No fue posible registrar el pago semanal por un error de persistencia.";
            }

            return RedirectToAction("PagosSemana", new { fecha = inicioSemana });
        }

        private async Task CargarPuestosAsync()
        {
            ViewBag.Puestos = await _context.Puestos
                .Where(p => p.Activo)
                .OrderBy(p => p.NombrePuesto)
                .ToListAsync();
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
            funcionario.Nombre = string.IsNullOrWhiteSpace(funcionario.Nombre)
                ? string.Empty
                : funcionario.Nombre.Trim();

            funcionario.Telefono = string.IsNullOrWhiteSpace(funcionario.Telefono)
                ? null
                : funcionario.Telefono.Trim();
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
                return RedirectToAction("PagosSemana", new { fecha = inicioSemana });
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
                            ? "Pago semanal automático"
                            : observacion,
                        CreadoPor = User.FindFirstValue(CustomClaimTypes.UserId) ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
                        Detalles = detalles
                    },
                    HttpContext.RequestAborted);

                TempData["Mensaje"] = "Todos los pagos pendientes de la semana fueron liquidados.";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Validación de negocio al liquidar la semana {InicioSemana:yyyy-MM-dd} - {FinSemana:yyyy-MM-dd}.", inicioSemana, finSemana);
                TempData["Error"] = ex.Message;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error al liquidar pagos semanales para el rango {InicioSemana:yyyy-MM-dd} - {FinSemana:yyyy-MM-dd}.", inicioSemana, finSemana);
                TempData["Error"] = "No fue posible registrar la liquidación semanal.";
            }

            return RedirectToAction("PagosSemana", new { fecha = inicioSemana });
        }

        public async Task<IActionResult> ExportarPagosExcel(DateTime inicioSemana, DateTime finSemana)
        {
            var resumen = await _liquidacionSemanalService.ObtenerResumenSemanaAsync(
                inicioSemana,
                finSemana,
                HttpContext.RequestAborted);

            var datos = resumen.Funcionarios
                .Select(f => new
                {
                    Funcionario = f.Nombre,
                    TotalGenerado = f.TotalGenerado,
                    Impuestos = f.Impuestos,
                    TotalNeto = f.TotalNeto,
                    Porcentaje = f.Porcentaje,
                    PorcentajeProducto = f.PorcentajeProducto,
                    MontoGenerado = f.PagoFinal,
                    MontoPagado = f.MontoPagado,
                    Pendiente = f.MontoPendiente,
                    TotalServicios = f.TotalGenerado - f.ProductosVendidos.Sum(p => p.Precio),
                    TotalProductos = f.ProductosVendidos.Sum(p => p.Precio),
                    ProductosVendidos = f.ProductosVendidos.Select(p => new
                    {
                        Funcionario = f.Nombre,
                        p.Fecha,
                        Producto = p.NombreProducto,
                        Precio = p.Precio,
                        Ganancia = p.GananciaFuncionario
                    }).ToList()
                })
                .ToList();

            var totalGeneradoServicios = resumen.TotalGeneradoServicios;
            var totalGeneradoProductos = resumen.TotalGeneradoProductos;
            var totalGeneradoGeneral = resumen.TotalGeneradoGeneral;
            var totalImpuestosGeneral = resumen.TotalImpuestosGeneral;
            var totalSinImpuestosGeneral = resumen.TotalSinImpuestosGeneral;
            var totalPagadoFuncionarios = resumen.TotalPagadoGeneral;
            var totalPendienteFuncionarios = resumen.TotalPendienteGeneral;
            var gananciaNegocio = resumen.GananciaNegocio;

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Pagos Funcionarios");

                var colorNegro = XLColor.FromHtml("#1C1C1C");
                var colorDorado = XLColor.FromHtml("#C6A55C");
                var colorGris = XLColor.FromHtml("#F5F5F5");

                ws.Range("A1:I1").Merge();
                ws.Cell("A1").Value = "LUXE CENTRO DE BELLEZA";
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

                int headerRow = 5;

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

                int fila = headerRow + 1;

                foreach (var d in datos)
                {
                    ws.Cell(fila, 1).Value = d.Funcionario;
                    ws.Cell(fila, 2).Value = d.TotalGenerado;
                    ws.Cell(fila, 3).Value = d.Impuestos;
                    ws.Cell(fila, 4).Value = d.TotalNeto;
                    ws.Cell(fila, 5).Value = d.Porcentaje;
                    ws.Cell(fila, 6).Value = d.PorcentajeProducto;
                    ws.Cell(fila, 7).Value = d.MontoGenerado;
                    ws.Cell(fila, 8).Value = d.MontoPagado;
                    ws.Cell(fila, 9).Value = d.Pendiente;

                    ws.Range(fila, 2, fila, 4).Style.NumberFormat.Format = "₡ #,##0.00";
                    ws.Range(fila, 7, fila, 9).Style.NumberFormat.Format = "₡ #,##0.00";

                    fila++;
                }

                var dataRange = ws.Range(headerRow + 1, 1, fila - 1, 9);

                dataRange.AddConditionalFormat()
                    .WhenIsTrue("MOD(ROW(),2)=0")
                    .Fill.SetBackgroundColor(colorGris);

                ws.Cell(fila, 7).Value = "TOTAL PAGADO";
                ws.Cell(fila, 7).Style.Font.Bold = true;

                ws.Cell(fila, 8).FormulaA1 = $"SUM(H{headerRow + 1}:H{fila - 1})";
                ws.Cell(fila, 8).Style.NumberFormat.Format = "₡ #,##0.00";
                ws.Cell(fila, 8).Style.Font.Bold = true;
                ws.Cell(fila, 8).Style.Font.FontColor = colorDorado;

                fila += 3;

                ws.Cell(fila, 1).Value = "Resumen del Salón";
                ws.Cell(fila, 1).Style.Font.Bold = true;

                fila++;

                ws.Cell(fila, 1).Value = "Total generado servicios";
                ws.Cell(fila, 2).Value = totalGeneradoServicios;

                fila++;

                ws.Cell(fila, 1).Value = "Total generado productos";
                ws.Cell(fila, 2).Value = totalGeneradoProductos;

                fila++;

                ws.Cell(fila, 1).Value = "Total generado general";
                ws.Cell(fila, 2).Value = totalGeneradoGeneral;

                fila++;

                ws.Cell(fila, 1).Value = "Total impuestos";
                ws.Cell(fila, 2).Value = totalImpuestosGeneral;

                fila++;

                ws.Cell(fila, 1).Value = "Total sin impuestos";
                ws.Cell(fila, 2).Value = totalSinImpuestosGeneral;

                fila++;

                ws.Cell(fila, 1).Value = "Total pagado funcionarios";
                ws.Cell(fila, 2).Value = totalPagadoFuncionarios;

                fila++;

                ws.Cell(fila, 1).Value = "Total pendiente funcionarios";
                ws.Cell(fila, 2).Value = totalPendienteFuncionarios;

                fila++;

                ws.Cell(fila, 1).Value = "Ganancia del negocio";
                ws.Cell(fila, 2).Value = gananciaNegocio;

                ws.Range(fila - 7, 2, fila, 2).Style.NumberFormat.Format = "₡ #,##0.00";

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

                foreach (var d in datos)
                {
                    foreach (var p in d.ProductosVendidos)
                    {
                        ws.Cell(fila, 1).Value = p.Funcionario;
                        ws.Cell(fila, 2).Value = p.Fecha;
                        ws.Cell(fila, 3).Value = p.Producto;
                        ws.Cell(fila, 4).Value = p.Precio;
                        ws.Cell(fila, 5).Value = p.Ganancia;

                        ws.Cell(fila, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                        ws.Cell(fila, 4).Style.NumberFormat.Format = "₡ #,##0.00";
                        ws.Cell(fila, 5).Style.NumberFormat.Format = "₡ #,##0.00";

                        fila++;
                    }
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

        private static List<string> ObtenerMetodosPago() =>
            LiquidacionSemanalDefaults.MetodosPagoPermitidos.ToList();




    }
}
