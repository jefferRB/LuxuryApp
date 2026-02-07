using LuxuryApp.Models.Finanzas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Controllers.Finanzas
{
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var hoy = DateTime.Today;

            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var finMes = inicioMes.AddMonths(1);

            // INGRESOS
            var ingresosMes = await _context.Cobros
                .Where(c => c.FechaCobro >= inicioMes && c.FechaCobro < finMes)
                .SumAsync(c => (decimal?)c.Monto) ?? 0;

            // EGRESOS
            var egresosMes = await _context.Egresos
                .Where(e => e.FechaEgreso >= inicioMes && e.FechaEgreso < finMes)
                .SumAsync(e => (decimal?)e.Monto) ?? 0;

            // CLIENTES
            var clientes = await _context.Clientes.CountAsync();

            // CITAS
            var citasMes = await _context.Citas
                .CountAsync(c => c.FechaHoraCita >= inicioMes && c.FechaHoraCita < finMes);

            // 🔥 INVENTARIO PRODUCTOS
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
                TotalProductosInventario = totalProductos
            };

            return View(vm);
        }
    }
}
