using ClosedXML.Excel;
using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
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


            var vm = new CobroIndexViewModel
            {
                Cobros = cobros,
                Filtros = filtros,
                TotalCobrado = totalCobrado,
                CantidadServicios = cantidadServicios,

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
            if (ModelState.IsValid)
            {
                vm.Cobro.FechaCobro = new DateTime(
           vm.Cobro.FechaCobro.Year,
           vm.Cobro.FechaCobro.Month,
           vm.Cobro.FechaCobro.Day,
           vm.Cobro.FechaCobro.Hour,
           vm.Cobro.FechaCobro.Minute,
           0
       );
                _context.Cobros.Add(vm.Cobro);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            // Recargar combos si falla validación
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

            vm.MetodosPago = ObtenerMetodosPago();

            return View(vm);
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
                var worksheet = workbook.Worksheets.Add("Cobros");

                worksheet.Cell(1, 1).Value = "Fecha";
                worksheet.Cell(1, 2).Value = "Cliente";
                worksheet.Cell(1, 3).Value = "Barbero";
                worksheet.Cell(1, 4).Value = "Servicio";
                worksheet.Cell(1, 5).Value = "Monto";
                worksheet.Cell(1, 6).Value = "Método Pago";

                int fila = 2;

                foreach (var c in cobros)
                {
                    worksheet.Cell(fila, 1).Value = c.FechaCobro;
                    worksheet.Cell(fila, 2).Value = c.NombreCliente;
                    worksheet.Cell(fila, 3).Value = c.Barbero?.Nombre;
                    worksheet.Cell(fila, 4).Value = c.Servicio?.Nombre;
                    worksheet.Cell(fila, 5).Value = c.Monto;
                    worksheet.Cell(fila, 6).Value = c.MetodoPago;

                    fila++;
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();

                    return File(
     content,
     "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
     $"ReporteCobros_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
 );
                }
            }
        }


        private async Task<List<Cobro>> ObtenerCobrosFiltrados(CobroFiltroViewModel filtros)
        {
            var query = _context.Cobros
                .Include(c => c.Barbero)
                .Include(c => c.Servicio)
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


            return await query
                .OrderByDescending(c => c.FechaCobro)
                .ToListAsync();
        }



    }
}
