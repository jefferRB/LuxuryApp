using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Services.Funcionarios;
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
            var totalImpuestos = totalGenerado * PagoFuncionarioDevengadoCalculator.TasaImpuesto;
            var totalSinImpuestos = totalGenerado - totalImpuestos;

            var totalPagadoFuncionariosCaja = await CalcularPagoFuncionariosCajaRealAsync(inicioMes, finMes);
            var pagosFuncionariosAnaliticosPorMes = await ObtenerPagoFuncionariosAnaliticoPorMesAsync(anioActual);
            var totalPagadoFuncionariosAnalitico = pagosFuncionariosAnaliticosPorMes.GetValueOrDefault(mesActual);

            var totalEgresosCaja = await _context.Egresos
                .Where(e => e.FechaEgreso >= inicioMes && e.FechaEgreso < finMes)
                .SumAsync(e => (decimal?)e.Monto) ?? 0;

            var otrosEgresos = await CalcularOtrosEgresosNoFuncionariosAsync(inicioMes, finMes);
            var totalEgresosAnaliticos = otrosEgresos + totalPagadoFuncionariosAnalitico;

            var efectivo = cobrosMes
                .Where(c => c.MetodoPago == "EFECTIVO")
                .Sum(c => c.Monto);

            var sinpe = cobrosMes
                .Where(c => c.MetodoPago == "SINPE")
                .Sum(c => c.Monto);

            var tarjeta = cobrosMes
                .Where(c => c.MetodoPago == "TARJETA")
                .Sum(c => c.Monto);

            var resultadoCajaPorMes = new List<decimal>();
            var resultadoAnaliticoPorMes = new List<decimal>();

            for (int m = 1; m <= 12; m++)
            {
                var inicio = new DateTime(anioActual, m, 1);
                var fin = inicio.AddMonths(1);

                var ingresos = await _context.Cobros
                    .Where(c => c.FechaCobro >= inicio && c.FechaCobro < fin)
                    .SumAsync(c => (decimal?)c.Monto) ?? 0;

                var totalSinImpuestosMes = ingresos - (ingresos * PagoFuncionarioDevengadoCalculator.TasaImpuesto);

                var egresosCajaMes = await _context.Egresos
                    .Where(e => e.FechaEgreso >= inicio && e.FechaEgreso < fin)
                    .SumAsync(e => (decimal?)e.Monto) ?? 0;

                var pagoFuncionariosAnaliticoMes = pagosFuncionariosAnaliticosPorMes.GetValueOrDefault(m);
                var otrosEgresosMes = await CalcularOtrosEgresosNoFuncionariosAsync(inicio, fin);

                resultadoCajaPorMes.Add(totalSinImpuestosMes - egresosCajaMes);
                resultadoAnaliticoPorMes.Add(totalSinImpuestosMes - (otrosEgresosMes + pagoFuncionariosAnaliticoMes));
            }

            var vm = new DashboardViewModel
            {
                TotalServicios = servicios,
                TotalProductos = productos,
                TotalGenerado = totalGenerado,
                TotalSinImpuestos = totalSinImpuestos,
                TotalImpuestos = totalImpuestos,
                TotalPagadoFuncionarios = totalPagadoFuncionariosCaja,
                TotalPagadoFuncionariosAnalitico = totalPagadoFuncionariosAnalitico,
                TotalEgresos = totalEgresosCaja,
                TotalEgresosAnaliticos = totalEgresosAnaliticos,
                IngresosEfectivo = efectivo,
                IngresosSinpe = sinpe,
                IngresosTarjeta = tarjeta,
                GananciaPorMes = resultadoCajaPorMes,
                ResultadoAnaliticoPorMes = resultadoAnaliticoPorMes,
                CantidadClientes = clientes,
                CantidadCitasMes = citasMes,
                ValorInventarioProductos = valorInventario,
                TotalProductosInventario = totalProductos,
                MesSeleccionado = mesActual,
                AnioSeleccionado = anioActual
            };

            return View(vm);
        }

        private async Task<decimal> CalcularPagoFuncionariosCajaRealAsync(DateTime inicioMes, DateTime finMes)
        {
            var totalLiquidacionesNuevas = await _context.LiquidacionesSemanales
                .Where(l => l.FechaPago >= inicioMes && l.FechaPago < finMes)
                .SumAsync(l => (decimal?)l.MontoTotal) ?? 0;

            var totalHistorico = await _context.Egresos
                .Where(e => e.FechaEgreso >= inicioMes &&
                            e.FechaEgreso < finMes &&
                            e.Categoria != null &&
                            e.Categoria.Nombre == LiquidacionSemanalDefaults.CategoriaPagoFuncionarios &&
                            !_context.LiquidacionesSemanales.Any(l => l.EgresoId == e.IdEgreso))
                .SumAsync(e => (decimal?)e.Monto) ?? 0;

            return totalLiquidacionesNuevas + totalHistorico;
        }

        private async Task<Dictionary<int, decimal>> ObtenerPagoFuncionariosAnaliticoPorMesAsync(int anio)
        {
            var resultado = Enumerable.Range(1, 12).ToDictionary(mes => mes, _ => 0m);

            var totalNuevo = await _context.LiquidacionesSemanalesDistribucionMensual
                .Where(d => d.Anio == anio)
                .GroupBy(d => d.Mes)
                .Select(group => new
                {
                    Mes = group.Key,
                    Monto = group.Sum(x => x.MontoAsignado)
                })
                .ToListAsync();

            foreach (var item in totalNuevo)
            {
                resultado[item.Mes] += item.Monto;
            }

            foreach (var item in await ObtenerPagoFuncionariosLegacyAnaliticoPorMesAsync(anio))
            {
                resultado[item.Key] += item.Value;
            }

            return resultado;
        }

        private async Task<decimal> CalcularOtrosEgresosNoFuncionariosAsync(DateTime inicioMes, DateTime finMes)
        {
            return await _context.Egresos
                .Where(e => e.FechaEgreso >= inicioMes &&
                            e.FechaEgreso < finMes &&
                            (e.Categoria == null || e.Categoria.Nombre != LiquidacionSemanalDefaults.CategoriaPagoFuncionarios))
                .SumAsync(e => (decimal?)e.Monto) ?? 0;
        }

        private async Task<Dictionary<int, decimal>> ObtenerPagoFuncionariosLegacyAnaliticoPorMesAsync(int anio)
        {
            var resultado = new Dictionary<int, decimal>();
            var inicioAnio = new DateTime(anio, 1, 1);
            var finAnioExclusivo = inicioAnio.AddYears(1);

            var legacyPagos = await _context.PagosFuncionarios
                .Where(p => p.InicioSemana < finAnioExclusivo && p.FinSemana >= inicioAnio)
                .ToListAsync();

            if (legacyPagos.Count == 0)
            {
                return resultado;
            }

            var minInicioSemana = legacyPagos.Min(p => p.InicioSemana).Date;
            var maxFinSemana = legacyPagos.Max(p => p.FinSemana).Date.AddDays(1);
            var funcionarioIds = legacyPagos
                .Select(p => p.FuncionarioId)
                .Distinct()
                .ToList();

            var funcionarios = await _context.Funcionarios
                .Where(f => funcionarioIds.Contains(f.IdFuncionario))
                .ToDictionaryAsync(f => f.IdFuncionario);

            var cobros = await _context.Cobros
                .Where(c => funcionarioIds.Contains(c.FuncionarioId) &&
                            c.FechaCobro >= minInicioSemana &&
                            c.FechaCobro < maxFinSemana)
                .ToListAsync();

            foreach (var pago in legacyPagos)
            {
                if (!funcionarios.TryGetValue(pago.FuncionarioId, out var funcionario))
                {
                    continue;
                }

                var cobrosSemana = cobros
                    .Where(c => c.FuncionarioId == pago.FuncionarioId &&
                                c.FechaCobro >= pago.InicioSemana.Date &&
                                c.FechaCobro < pago.FinSemana.Date.AddDays(1))
                    .ToList();

                foreach (var distribucion in PagoFuncionarioDevengadoCalculator.DistribuirMontoPagadoPorMes(
                             cobrosSemana,
                             funcionario,
                             pago.MontoPagado))
                {
                    if (distribucion.Anio != anio)
                    {
                        continue;
                    }

                    resultado[distribucion.Mes] = resultado.GetValueOrDefault(distribucion.Mes) + distribucion.MontoAsignado;
                }
            }

            return resultado;
        }
    }
}
