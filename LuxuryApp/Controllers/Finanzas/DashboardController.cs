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

            var ingresosMes = await _context.Cobros
                .Where(c => c.FechaCobro >= inicioMes && c.FechaCobro < finMes)
                .SumAsync(c => (decimal?)c.Monto) ?? 0;

            var egresosMes = await _context.Egresos
                .Where(e => e.FechaEgreso >= inicioMes && e.FechaEgreso < finMes)
                .SumAsync(e => (decimal?)e.Monto) ?? 0;

            var clientes = await _context.Clientes.CountAsync();

            var citasMes = await _context.Citas
                .CountAsync(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes);

            var valorInventario = await _context.Productos
                .Where(p => p.Activo)
                .SumAsync(p => (decimal?)(p.PrecioProducto * p.CantidadProducto)) ?? 0;

            var totalProductos = await _context.Productos
                .Where(p => p.Activo)
                .CountAsync();

            var vm = new DashboardViewModel
            {
                TotalIngresosMes = ingresosMes,
                TotalEgresosMes = egresosMes,
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
