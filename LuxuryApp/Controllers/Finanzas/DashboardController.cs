using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    [Authorize(Roles = "Administrador")]

    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? mes, int? anio)
        {
            var hoy = DateTime.Today;

            int mesActual = mes ?? hoy.Month;
            int anioActual = anio ?? hoy.Year;

            var inicioMes = new DateTime(anioActual, mesActual, 1);
            var finMes = inicioMes.AddMonths(1);

            

            var cobrosMes = await _context.Cobros
    .Where(c => c.FechaCobro >= inicioMes && c.FechaCobro < finMes)
    .ToListAsync();

            var servicios = cobrosMes
    .Where(c => c.ServicioId != null)
    .Sum(c => c.Monto);

            var productos = cobrosMes
                .Where(c => c.ProductoId != null)
                .Sum(c => c.Monto);

            var clientes = await _context.Clientes.CountAsync();

            var citasMes = await _context.Citas
                .CountAsync(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes);

            var valorInventario = await _context.Productos
                .Where(p => p.Activo)
                .SumAsync(p => (decimal?)(p.PrecioProducto * p.CantidadProducto)) ?? 0;

            var totalProductos = await _context.Productos
                .Where(p => p.Activo)
                .CountAsync();

            var totalGenerado = servicios + productos;

            var totalImpuestos = totalGenerado * 0.13m;

            var totalSinImpuestos = totalGenerado - totalImpuestos;

            var funcionarios = await _context.Funcionarios
    .Where(f => f.Activo)
    .ToListAsync();

            decimal pagadoFuncionarios = 0;

            foreach (var f in funcionarios)
            {
                var cobrosFuncionario = cobrosMes
                    .Where(c => c.FuncionarioId == f.IdFuncionario)
                    .ToList();

                var serviciosFuncionario = cobrosFuncionario
                    .Where(c => c.ServicioId != null)
                    .ToList();

                var productosFuncionario = cobrosFuncionario
                    .Where(c => c.ProductoId != null)
                    .ToList();

                var totalServiciosFuncionario = serviciosFuncionario.Sum(s => s.Monto);
                var totalProductosFuncionario = productosFuncionario.Sum(p => p.Monto);

                var netoServicios = totalServiciosFuncionario - (totalServiciosFuncionario * 0.13m);
                var netoProductos = totalProductosFuncionario - (totalProductosFuncionario * 0.13m);

                var pagoServicios = netoServicios * (f.PorcentajeGanancia / 100);
                var pagoProductos = netoProductos * (f.PorcentajeProducto / 100);

                pagadoFuncionarios += pagoServicios + pagoProductos;
            }

            var egresos = await _context.Egresos
    .Where(e => e.FechaEgreso >= inicioMes && e.FechaEgreso < finMes)
    .SumAsync(e => (decimal?)e.Monto) ?? 0;

            var efectivo = cobrosMes
    .Where(c => c.MetodoPago == "EFECTIVO")
    .Sum(c => c.Monto);

            var sinpe = cobrosMes
                .Where(c => c.MetodoPago == "SINPE")
                .Sum(c => c.Monto);

            var tarjeta = cobrosMes
                .Where(c => c.MetodoPago == "TARJETA")
                .Sum(c => c.Monto);

            var gananciaPorMes = new List<decimal>();

            for (int m = 1; m <= 12; m++)
            {
                var inicio = new DateTime(anioActual, m, 1);
                var fin = inicio.AddMonths(1);

                var ingresos = await _context.Cobros
                    .Where(c => c.FechaCobro >= inicio && c.FechaCobro < fin)
                    .SumAsync(c => (decimal?)c.Monto) ?? 0;

                var egresosMesLoop = await _context.Egresos
                    .Where(e => e.FechaEgreso >= inicio && e.FechaEgreso < fin)
                    .SumAsync(e => (decimal?)e.Monto) ?? 0;

                var totalSinImpuestosMes = ingresos - (ingresos * 0.13m);

                gananciaPorMes.Add(totalSinImpuestosMes - egresosMesLoop);
            }


            var vm = new DashboardViewModel
            {
                TotalServicios = servicios,
                TotalProductos = productos,
                TotalGenerado = totalGenerado,
                TotalSinImpuestos = totalSinImpuestos,
                TotalImpuestos = totalImpuestos,

                TotalPagadoFuncionarios = pagadoFuncionarios,
                TotalEgresos = egresos,

                IngresosEfectivo = efectivo,
                IngresosSinpe = sinpe,
                IngresosTarjeta = tarjeta,

                GananciaPorMes = gananciaPorMes,

                CantidadClientes = clientes,
                CantidadCitasMes = citasMes,
                ValorInventarioProductos = valorInventario,
                TotalProductosInventario = totalProductos,

                MesSeleccionado = mesActual,
                AnioSeleccionado = anioActual
            };

            return View(vm);
        }

    }
}
