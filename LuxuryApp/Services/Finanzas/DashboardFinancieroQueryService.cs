using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Funcionarios;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Finanzas
{
    public sealed class DashboardFinancieroQueryService : IDashboardFinancieroQueryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IBusinessDateTimeProvider _businessDateTimeProvider;

        public DashboardFinancieroQueryService(
            ApplicationDbContext context,
            IBusinessDateTimeProvider businessDateTimeProvider)
        {
            _context = context;
            _businessDateTimeProvider = businessDateTimeProvider;
        }

        public async Task<DashboardViewModel> BuildViewModelAsync(
            int? mes,
            int? anio,
            CancellationToken cancellationToken = default)
        {
            var selection = DashboardPeriodSelection.Resolve(mes, anio, _businessDateTimeProvider.Today());

            var cobroMetrics = await GetCobroMetricsAsync(
                    selection.MonthStart,
                    selection.MonthEnd,
                    cancellationToken)
                ?? new CobroMetricsProjection();

            var operationalMetrics = await GetOperationalMetricsAsync(
                selection.MonthStart,
                selection.MonthEnd,
                cancellationToken);

            var ingresosPorMes = await GetIngresosPorMesAsync(
                selection.YearStart,
                selection.YearEnd,
                cancellationToken);

            var egresosPorMes = await GetEgresosPorMesAsync(
                selection.YearStart,
                selection.YearEnd,
                cancellationToken);

            var pagosFuncionariosHistoricosCajaPorMes = await GetPagosFuncionariosHistoricosCajaPorMesAsync(
                selection.YearStart,
                selection.YearEnd,
                cancellationToken);

            var pagosFuncionariosCajaPorLiquidacionPorMes = await GetPagosFuncionariosCajaPorLiquidacionPorMesAsync(
                selection.YearStart,
                selection.YearEnd,
                cancellationToken);

            var pagosFuncionariosAnaliticosPorMes = await GetPagosFuncionariosAnaliticosPorMesAsync(
                selection.Year,
                cancellationToken);

            var egresosMesSeleccionado = egresosPorMes.GetValueOrDefault(
                selection.Month,
                EgresoMonthAggregateProjection.Empty);

            var totalImpuestos = cobroMetrics.TotalGenerado * PagoFuncionarioDevengadoCalculator.TasaImpuesto;
            var totalSinImpuestos = cobroMetrics.TotalGenerado - totalImpuestos;

            var totalPagadoFuncionariosCaja =
                pagosFuncionariosCajaPorLiquidacionPorMes.GetValueOrDefault(selection.Month)
                + pagosFuncionariosHistoricosCajaPorMes.GetValueOrDefault(selection.Month);

            var totalPagadoFuncionariosAnalitico =
                pagosFuncionariosAnaliticosPorMes.GetValueOrDefault(selection.Month);

            var totalEgresosAnaliticos =
                egresosMesSeleccionado.OtrosEgresos + totalPagadoFuncionariosAnalitico;

            var resultadoCajaPorMes = new List<decimal>(12);
            var resultadoAnaliticoPorMes = new List<decimal>(12);

            for (var currentMonth = 1; currentMonth <= 12; currentMonth++)
            {
                var ingresosMes = ingresosPorMes.GetValueOrDefault(currentMonth);
                var totalSinImpuestosMes = ingresosMes - (ingresosMes * PagoFuncionarioDevengadoCalculator.TasaImpuesto);

                var egresosMes = egresosPorMes.GetValueOrDefault(
                    currentMonth,
                    EgresoMonthAggregateProjection.Empty);

                var pagoFuncionariosAnaliticoMes =
                    pagosFuncionariosAnaliticosPorMes.GetValueOrDefault(currentMonth);

                resultadoCajaPorMes.Add(totalSinImpuestosMes - egresosMes.TotalEgresos);
                resultadoAnaliticoPorMes.Add(
                    totalSinImpuestosMes - (egresosMes.OtrosEgresos + pagoFuncionariosAnaliticoMes));
            }

            return new DashboardViewModel
            {
                TotalServicios = cobroMetrics.TotalServicios,
                TotalProductos = cobroMetrics.TotalProductos,
                TotalGenerado = cobroMetrics.TotalGenerado,
                TotalSinImpuestos = totalSinImpuestos,
                TotalImpuestos = totalImpuestos,
                TotalPagadoFuncionarios = totalPagadoFuncionariosCaja,
                TotalPagadoFuncionariosAnalitico = totalPagadoFuncionariosAnalitico,
                TotalEgresos = egresosMesSeleccionado.TotalEgresos,
                TotalEgresosAnaliticos = totalEgresosAnaliticos,
                IngresosEfectivo = cobroMetrics.IngresosEfectivo,
                IngresosSinpe = cobroMetrics.IngresosSinpe,
                IngresosTarjeta = cobroMetrics.IngresosTarjeta,
                GananciaPorMes = resultadoCajaPorMes,
                ResultadoAnaliticoPorMes = resultadoAnaliticoPorMes,
                CantidadClientes = operationalMetrics.CantidadClientes,
                CantidadCitasMes = operationalMetrics.CantidadCitasMes,
                ValorInventarioProductos = operationalMetrics.ValorInventarioProductos,
                TotalProductosInventario = operationalMetrics.TotalProductosInventario,
                MesSeleccionado = selection.Month,
                AnioSeleccionado = selection.Year
            };
        }

        private Task<CobroMetricsProjection?> GetCobroMetricsAsync(
            DateTime monthStart,
            DateTime monthEnd,
            CancellationToken cancellationToken) =>
            _context.Cobros
                .AsNoTracking()
                .Where(c => c.FechaCobro >= monthStart && c.FechaCobro < monthEnd)
                .GroupBy(_ => 1)
                .Select(group => new CobroMetricsProjection
                {
                    TotalServicios = group.Sum(x => x.ServicioId != null ? x.Monto : 0m),
                    TotalProductos = group.Sum(x => x.ProductoId != null ? x.Monto : 0m),
                    TotalGenerado = group.Sum(x => x.Monto),
                    IngresosEfectivo = group.Sum(x => x.MetodoPago == "EFECTIVO" ? x.Monto : 0m),
                    IngresosSinpe = group.Sum(x => x.MetodoPago == "SINPE" ? x.Monto : 0m),
                    IngresosTarjeta = group.Sum(x => x.MetodoPago == "TARJETA" ? x.Monto : 0m)
                })
                .SingleOrDefaultAsync(cancellationToken);

        private async Task<OperationalMetricsProjection> GetOperationalMetricsAsync(
            DateTime monthStart,
            DateTime monthEnd,
            CancellationToken cancellationToken)
        {
            var cantidadClientes = await _context.Clientes
                .AsNoTracking()
                .CountAsync(cancellationToken);

            var cantidadCitasMes = await _context.Citas
                .AsNoTracking()
                .CountAsync(
                    c => c.FechaHoraCita >= monthStart && c.FechaHoraCita < monthEnd,
                    cancellationToken);

            var inventario = await _context.Productos
                .AsNoTracking()
                .Where(p => p.Activo)
                .GroupBy(_ => 1)
                .Select(group => new OperationalMetricsProjection
                {
                    ValorInventarioProductos = group.Sum(x => x.PrecioProducto * x.CantidadProducto),
                    TotalProductosInventario = group.Count()
                })
                .SingleOrDefaultAsync(cancellationToken)
                ?? new OperationalMetricsProjection();

            inventario.CantidadClientes = cantidadClientes;
            inventario.CantidadCitasMes = cantidadCitasMes;

            return inventario;
        }

        private async Task<Dictionary<int, decimal>> GetIngresosPorMesAsync(
            DateTime yearStart,
            DateTime yearEnd,
            CancellationToken cancellationToken)
        {
            var rows = await _context.Cobros
                .AsNoTracking()
                .Where(c => c.FechaCobro >= yearStart && c.FechaCobro < yearEnd)
                .GroupBy(c => c.FechaCobro.Month)
                .Select(group => new MonthAmountProjection
                {
                    Month = group.Key,
                    Amount = group.Sum(x => x.Monto)
                })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(x => x.Month, x => x.Amount);
        }

        private async Task<Dictionary<int, EgresoMonthAggregateProjection>> GetEgresosPorMesAsync(
            DateTime yearStart,
            DateTime yearEnd,
            CancellationToken cancellationToken)
        {
            var rows = await _context.Egresos
                .AsNoTracking()
                .Where(e => e.FechaEgreso >= yearStart && e.FechaEgreso < yearEnd)
                .GroupBy(e => e.FechaEgreso.Month)
                .Select(group => new EgresoMonthAggregateProjection
                {
                    Month = group.Key,
                    TotalEgresos = group.Sum(x => x.Monto),
                    OtrosEgresos = group.Sum(x =>
                        x.Categoria != null && x.Categoria.Nombre == LiquidacionSemanalDefaults.CategoriaPagoFuncionarios
                            ? 0m
                            : x.Monto)
                })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(x => x.Month, x => x);
        }

        private async Task<Dictionary<int, decimal>> GetPagosFuncionariosHistoricosCajaPorMesAsync(
            DateTime yearStart,
            DateTime yearEnd,
            CancellationToken cancellationToken)
        {
            var rows = await _context.Egresos
                .AsNoTracking()
                .Where(e => e.FechaEgreso >= yearStart && e.FechaEgreso < yearEnd)
                .Where(e => e.Categoria != null && e.Categoria.Nombre == LiquidacionSemanalDefaults.CategoriaPagoFuncionarios)
                .Where(e => !_context.LiquidacionesSemanales.Any(l => l.EgresoId == e.IdEgreso))
                .GroupBy(e => e.FechaEgreso.Month)
                .Select(group => new MonthAmountProjection
                {
                    Month = group.Key,
                    Amount = group.Sum(x => x.Monto)
                })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(x => x.Month, x => x.Amount);
        }

        private async Task<Dictionary<int, decimal>> GetPagosFuncionariosCajaPorLiquidacionPorMesAsync(
            DateTime yearStart,
            DateTime yearEnd,
            CancellationToken cancellationToken)
        {
            var rows = await _context.LiquidacionesSemanales
                .AsNoTracking()
                .Where(l => l.FechaPago >= yearStart && l.FechaPago < yearEnd)
                .GroupBy(l => l.FechaPago.Month)
                .Select(group => new MonthAmountProjection
                {
                    Month = group.Key,
                    Amount = group.Sum(x => x.MontoTotal)
                })
                .ToListAsync(cancellationToken);

            return rows.ToDictionary(x => x.Month, x => x.Amount);
        }

        private async Task<Dictionary<int, decimal>> GetPagosFuncionariosAnaliticosPorMesAsync(
            int year,
            CancellationToken cancellationToken)
        {
            var result = CreateMonthDictionary();

            var newRows = await _context.LiquidacionesSemanalesDistribucionMensual
                .AsNoTracking()
                .Where(d => d.Anio == year)
                .GroupBy(d => d.Mes)
                .Select(group => new MonthAmountProjection
                {
                    Month = group.Key,
                    Amount = group.Sum(x => x.MontoAsignado)
                })
                .ToListAsync(cancellationToken);

            foreach (var row in newRows)
            {
                result[row.Month] += row.Amount;
            }

            foreach (var row in await GetPagosFuncionariosLegacyAnaliticosPorMesAsync(year, cancellationToken))
            {
                result[row.Key] += row.Value;
            }

            return result;
        }

        private async Task<Dictionary<int, decimal>> GetPagosFuncionariosLegacyAnaliticosPorMesAsync(
            int year,
            CancellationToken cancellationToken)
        {
            var result = CreateMonthDictionary();
            var yearStart = new DateTime(year, 1, 1);
            var yearEnd = yearStart.AddYears(1);

            var legacyPayments = await _context.PagosFuncionarios
                .AsNoTracking()
                .Where(p => p.InicioSemana < yearEnd && p.FinSemana >= yearStart)
                .Select(p => new LegacyPaymentProjection
                {
                    FuncionarioId = p.FuncionarioId,
                    MontoPagado = p.MontoPagado,
                    InicioSemana = p.InicioSemana,
                    FinSemana = p.FinSemana
                })
                .ToListAsync(cancellationToken);

            if (legacyPayments.Count == 0)
            {
                return result;
            }

            var funcionarioIds = legacyPayments
                .Select(p => p.FuncionarioId)
                .Distinct()
                .ToList();

            var funcionarios = await _context.Funcionarios
                .AsNoTracking()
                .Where(f => funcionarioIds.Contains(f.IdFuncionario))
                .Select(f => new LegacyFuncionarioProjection
                {
                    IdFuncionario = f.IdFuncionario,
                    PorcentajeGanancia = f.PorcentajeGanancia,
                    PorcentajeProducto = f.PorcentajeProducto,
                    RebajarImpuestosAntesDeComision = f.RebajarImpuestosAntesDeComision
                })
                .ToDictionaryAsync(f => f.IdFuncionario, cancellationToken);

            if (funcionarios.Count == 0)
            {
                return result;
            }

            var minInicioSemana = legacyPayments.Min(p => p.InicioSemana).Date;
            var maxFinSemanaExclusive = legacyPayments.Max(p => p.FinSemana).Date.AddDays(1);

            var cobros = await _context.Cobros
                .AsNoTracking()
                .Where(c =>
                    funcionarioIds.Contains(c.FuncionarioId) &&
                    c.FechaCobro >= minInicioSemana &&
                    c.FechaCobro < maxFinSemanaExclusive)
                .Select(c => new LegacyCobroProjection
                {
                    FuncionarioId = c.FuncionarioId,
                    FechaCobro = c.FechaCobro,
                    Monto = c.Monto,
                    ProductoId = c.ProductoId
                })
                .ToListAsync(cancellationToken);

            var cobrosPorFuncionario = cobros
                .GroupBy(c => c.FuncionarioId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(item => item.FechaCobro).ToList());

            foreach (var payment in legacyPayments)
            {
                if (!funcionarios.TryGetValue(payment.FuncionarioId, out var funcionario))
                {
                    continue;
                }

                if (!cobrosPorFuncionario.TryGetValue(payment.FuncionarioId, out var cobrosFuncionario))
                {
                    continue;
                }

                var start = payment.InicioSemana.Date;
                var endExclusive = payment.FinSemana.Date.AddDays(1);

                var cobrosSemana = cobrosFuncionario
                    .Where(c => c.FechaCobro >= start && c.FechaCobro < endExclusive)
                    .Select(c => c.ToCobro())
                    .ToList();

                foreach (var distribution in PagoFuncionarioDevengadoCalculator.DistribuirMontoPagadoPorMes(
                             cobrosSemana,
                             funcionario.ToFuncionario(),
                             payment.MontoPagado))
                {
                    if (distribution.Anio != year)
                    {
                        continue;
                    }

                    result[distribution.Mes] += distribution.MontoAsignado;
                }
            }

            return result;
        }

        private static Dictionary<int, decimal> CreateMonthDictionary() =>
            Enumerable.Range(1, 12).ToDictionary(month => month, _ => 0m);

        private sealed class DashboardPeriodSelection
        {
            public int Month { get; init; }
            public int Year { get; init; }
            public DateTime MonthStart { get; init; }
            public DateTime MonthEnd { get; init; }
            public DateTime YearStart { get; init; }
            public DateTime YearEnd { get; init; }

            public static DashboardPeriodSelection Resolve(int? mes, int? anio, DateTime today)
            {
                var year = anio ?? today.Year;
                var month = mes ?? today.Month;
                var monthStart = new DateTime(year, month, 1);

                return new DashboardPeriodSelection
                {
                    Month = month,
                    Year = year,
                    MonthStart = monthStart,
                    MonthEnd = monthStart.AddMonths(1),
                    YearStart = new DateTime(year, 1, 1),
                    YearEnd = new DateTime(year + 1, 1, 1)
                };
            }
        }

        private sealed class CobroMetricsProjection
        {
            public decimal TotalServicios { get; init; }
            public decimal TotalProductos { get; init; }
            public decimal TotalGenerado { get; init; }
            public decimal IngresosEfectivo { get; init; }
            public decimal IngresosSinpe { get; init; }
            public decimal IngresosTarjeta { get; init; }
        }

        private sealed class OperationalMetricsProjection
        {
            public int CantidadClientes { get; set; }
            public int CantidadCitasMes { get; set; }
            public decimal ValorInventarioProductos { get; init; }
            public int TotalProductosInventario { get; init; }
        }

        private sealed class MonthAmountProjection
        {
            public int Month { get; init; }
            public decimal Amount { get; init; }
        }

        private sealed class EgresoMonthAggregateProjection
        {
            public static EgresoMonthAggregateProjection Empty { get; } = new();

            public int Month { get; init; }
            public decimal TotalEgresos { get; init; }
            public decimal OtrosEgresos { get; init; }
        }

        private sealed class LegacyPaymentProjection
        {
            public int FuncionarioId { get; init; }
            public decimal MontoPagado { get; init; }
            public DateTime InicioSemana { get; init; }
            public DateTime FinSemana { get; init; }
        }

        private sealed class LegacyFuncionarioProjection
        {
            public int IdFuncionario { get; init; }
            public decimal PorcentajeGanancia { get; init; }
            public decimal PorcentajeProducto { get; init; }
            public bool RebajarImpuestosAntesDeComision { get; init; }

            public Funcionario ToFuncionario() =>
                new()
                {
                    IdFuncionario = IdFuncionario,
                    PorcentajeGanancia = PorcentajeGanancia,
                    PorcentajeProducto = PorcentajeProducto,
                    RebajarImpuestosAntesDeComision = RebajarImpuestosAntesDeComision
                };
        }

        private sealed class LegacyCobroProjection
        {
            public int FuncionarioId { get; init; }
            public DateTime FechaCobro { get; init; }
            public decimal Monto { get; init; }
            public int? ProductoId { get; init; }

            public Cobro ToCobro() =>
                new()
                {
                    FechaCobro = FechaCobro,
                    Monto = Monto,
                    ProductoId = ProductoId
                };
        }
    }
}
