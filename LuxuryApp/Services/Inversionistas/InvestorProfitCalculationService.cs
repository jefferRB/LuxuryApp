using LuxuryApp.Models.Fiscal;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Inversionistas;
using LuxuryApp.Services.Funcionarios;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Inversionistas
{
    /// <summary>
    /// Calcula la ganancia distribuible de un periodo REUTILIZANDO los servicios que ya alimentan
    /// las pantallas del negocio. No se duplica ni una fórmula fiscal:
    ///
    /// <list type="bullet">
    ///   <item>Ingresos e IVA salen de <see cref="ILiquidacionSemanalService"/>, que a su vez usa el
    ///   motor fiscal (<c>ITaxCalculationService</c> + <c>ITenantFiscalConfigService</c>) línea por
    ///   línea. Así el "sin IVA" del inversionista es idéntico al de Liquidaciones y Dashboard.</item>
    ///   <item>Las liquidaciones de colaboradores salen del mismo resumen, sin recalcular comisiones.</item>
    ///   <item>Los gastos se leen de Egresos con las mismas exclusiones que usa el Dashboard.</item>
    /// </list>
    ///
    /// <para>Redondeo: <see cref="FiscalMath.Redondear"/> (2 decimales, half-even), igual que el
    /// resto de la aplicación.</para>
    /// </summary>
    public sealed class InvestorProfitCalculationService : IInvestorProfitCalculationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILiquidacionSemanalService _liquidacionService;

        public InvestorProfitCalculationService(
            ApplicationDbContext context,
            ILiquidacionSemanalService liquidacionService)
        {
            _context = context;
            _liquidacionService = liquidacionService;
        }

        public async Task<InvestorProfitBreakdown> CalculateAsync(
            DateOnly periodoInicio,
            DateOnly periodoFin,
            InvestorProfitPolicy policy,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(policy);

            if (periodoFin < periodoInicio)
            {
                throw new ArgumentException("El periodo indicado no es válido.", nameof(periodoFin));
            }

            var inicio = periodoInicio.ToDateTime(TimeOnly.MinValue);
            var fin = periodoFin.ToDateTime(TimeOnly.MinValue);

            // Ingresos + IVA + liquidaciones: una sola pasada del servicio de liquidación, que
            // opera sobre rangos arbitrarios y aplica el motor fiscal por línea.
            var resumen = await _liquidacionService.ObtenerResumenSemanaAsync(inicio, fin, cancellationToken);

            var ingresosCobrados = FiscalMath.Redondear(resumen.TotalGeneradoGeneral);

            // Sin exclusión de IVA la base es el total cobrado tal cual (caso raro, pero configurable).
            var ingresosNetos = policy.ExcluirIva
                ? FiscalMath.Redondear(resumen.TotalBaseVentaSinIvaGeneral)
                : ingresosCobrados;

            var ivaExcluido = policy.ExcluirIva
                ? FiscalMath.Redondear(ingresosCobrados - ingresosNetos)
                : 0m;

            var liquidaciones = 0m;
            if (policy.IncluirLiquidaciones)
            {
                liquidaciones = policy.BaseLiquidaciones == InvestorSettlementBasis.Pagado
                    ? FiscalMath.Redondear(resumen.TotalPagadoAplicadoGeneral)
                    : FiscalMath.Redondear(resumen.TotalAPagarColaboradoresGeneral);
            }

            var gastos = await CalcularGastosAsync(periodoInicio, periodoFin, policy, cancellationToken);

            return new InvestorProfitBreakdown
            {
                PeriodoInicio = periodoInicio,
                PeriodoFin = periodoFin,
                IngresosCobrados = ingresosCobrados,
                IvaExcluido = ivaExcluido,
                IngresosNetos = ingresosNetos,
                GastosElegibles = gastos.Total,
                Liquidaciones = liquidaciones,
                PoliticaVersion = policy.BuildVersionDescription(),
                GastosPorCategoria = gastos.Detalle
            };
        }

        private async Task<(decimal Total, IReadOnlyList<InvestorExpenseCategoryBreakdown> Detalle)> CalcularGastosAsync(
            DateOnly periodoInicio,
            DateOnly periodoFin,
            InvestorProfitPolicy policy,
            CancellationToken cancellationToken)
        {
            var desde = periodoInicio.ToDateTime(TimeOnly.MinValue);
            var hastaExclusive = periodoFin.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var filas = await _context.Egresos
                .AsNoTracking()
                .Where(egreso => egreso.FechaEgreso >= desde && egreso.FechaEgreso < hastaExclusive)
                .GroupBy(egreso => new
                {
                    egreso.CategoriaId,
                    Nombre = egreso.Categoria != null ? egreso.Categoria.Nombre : null
                })
                .Select(group => new
                {
                    group.Key.CategoriaId,
                    group.Key.Nombre,
                    Monto = group.Sum(egreso => egreso.Monto)
                })
                .ToListAsync(cancellationToken);

            var seleccionadas = policy.CategoriasSeleccionadas
                .Select(link => link.CategoriaId)
                .ToHashSet();

            var detalle = new List<InvestorExpenseCategoryBreakdown>(filas.Count);
            var total = 0m;

            foreach (var fila in filas.OrderBy(row => row.Nombre ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
            {
                var nombre = string.IsNullOrWhiteSpace(fila.Nombre) ? "Sin categoría" : fila.Nombre!;
                var motivo = ResolverMotivoExclusion(fila.CategoriaId, nombre, policy, seleccionadas);
                var incluido = motivo is null;
                var monto = FiscalMath.Redondear(fila.Monto);

                if (incluido)
                {
                    total += monto;
                }

                detalle.Add(new InvestorExpenseCategoryBreakdown(
                    fila.CategoriaId,
                    nombre,
                    monto,
                    incluido,
                    motivo));
            }

            return (FiscalMath.Redondear(total), detalle);
        }

        /// <summary>
        /// Devuelve el motivo por el que una categoría NO cuenta como gasto elegible, o null si sí cuenta.
        /// Las dos exclusiones estructurales son obligatorias y no dependen de la configuración.
        /// </summary>
        private static string? ResolverMotivoExclusion(
            int categoriaId,
            string nombre,
            InvestorProfitPolicy policy,
            IReadOnlySet<int> seleccionadas)
        {
            // 1) Pago a colaboradores: ya se resta como "Liquidaciones". Contarlo también como gasto
            //    lo restaría dos veces. Misma exclusión que hace el Dashboard financiero (OtrosEgresos).
            if (string.Equals(nombre, LiquidacionSemanalDefaults.CategoriaPagoFuncionarios, StringComparison.OrdinalIgnoreCase))
            {
                return "Los pagos a colaboradores ya se restan en la línea de liquidaciones.";
            }

            // 2) Distribución a inversionistas: si contara como gasto, pagarle al inversionista
            //    reduciría la ganancia distribuible y con ella su propia participación (recursividad).
            if (string.Equals(nombre, InvestorDefaults.CategoriaDistribucionInversionistas, StringComparison.OrdinalIgnoreCase))
            {
                return "Los pagos a inversionistas no reducen la ganancia distribuible.";
            }

            return policy.ModoCategoriasGasto switch
            {
                InvestorExpenseCategoryMode.SoloSeleccionadas when !seleccionadas.Contains(categoriaId) =>
                    "La categoría no está marcada como elegible.",
                InvestorExpenseCategoryMode.TodasExceptoSeleccionadas when seleccionadas.Contains(categoriaId) =>
                    "La categoría está excluida por configuración.",
                _ => null
            };
        }
    }
}
