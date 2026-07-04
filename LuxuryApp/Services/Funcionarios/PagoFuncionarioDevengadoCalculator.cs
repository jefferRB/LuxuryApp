using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Fiscal;
using LuxuryApp.Models.Funcionarios;

namespace LuxuryApp.Services.Funcionarios
{
    public static class PagoFuncionarioDevengadoCalculator
    {
        /// <summary>Tarifa de IVA por defecto como factor (0.13). Compatibilidad; ver <see cref="FiscalDefaults"/>.</summary>
        public const decimal TasaImpuesto = FiscalDefaults.TarifaIvaPorDefecto / 100m;

        /// <summary>
        /// Base de venta sin IVA a partir de un total que YA incluye IVA (contexto CR).
        /// </summary>
        private static decimal BaseSinIva(decimal totalConIva) =>
            FiscalMath.Redondear(totalConIva / (1m + (FiscalDefaults.TarifaIvaPorDefecto / 100m)));

        /// <summary>Base sin IVA (a la tarifa por defecto) de un total con IVA incluido.</summary>
        public static decimal CalcularBaseSinIvaIncluido(decimal totalConIva) => BaseSinIva(totalConIva);

        /// <summary>IVA contenido (a la tarifa por defecto) en un total con IVA incluido.</summary>
        public static decimal CalcularIvaIncluido(decimal totalConIva) => totalConIva - BaseSinIva(totalConIva);

        /// <summary>
        /// Calcula la base sobre la que se aplica el porcentaje de comisión del funcionario.
        /// Si <paramref name="rebajarImpuestos"/> es true, se usa la base SIN IVA (precio con IVA
        /// incluido → Total / 1.13); si es false, se usa el total cobrado.
        /// </summary>
        public static decimal CalcularBaseComision(decimal monto, bool rebajarImpuestos) =>
            rebajarImpuestos ? BaseSinIva(monto) : monto;

        /// <summary>
        /// Base de comisión según la configuración explícita del colaborador
        /// (<see cref="ComisionCalculadaSobre"/>), fuente de verdad frente al flag histórico.
        /// </summary>
        public static decimal CalcularBaseComision(decimal monto, ComisionCalculadaSobre comisionSobre) =>
            comisionSobre == ComisionCalculadaSobre.BaseSinIva ? BaseSinIva(monto) : monto;

        public static decimal CalcularMontoDevengado(Cobro cobro, Funcionario funcionario)
        {
            var porcentaje = cobro.ProductoId != null
                ? funcionario.PorcentajeProducto
                : funcionario.PorcentajeGanancia;

            var baseComision = CalcularBaseComision(cobro.Monto, funcionario.ComisionCalculadaSobre);
            return baseComision * (porcentaje / 100m);
        }

        public static decimal CalcularPagoColaboradores(IEnumerable<Cobro> cobros)
        {
            decimal total = 0;

            foreach (var cobro in cobros)
            {
                if (cobro.Funcionario == null)
                {
                    continue;
                }

                total += CalcularMontoDevengado(cobro, cobro.Funcionario);
            }

            return total;
        }

        public static IReadOnlyList<PagoFuncionarioDistribucionMensual> DistribuirMontoPagadoPorMes(
            IEnumerable<Cobro> cobros,
            Funcionario funcionario,
            decimal montoPagado)
        {
            var baseMensual = cobros
                .Select(cobro => new
                {
                    Cobro = cobro,
                    MontoDevengado = CalcularMontoDevengado(cobro, funcionario)
                })
                .Where(x => x.MontoDevengado > 0)
                .GroupBy(
                    x => new { x.Cobro.FechaCobro.Year, x.Cobro.FechaCobro.Month },
                    x => x)
                .Select(group => new PagoFuncionarioDistribucionMensual
                {
                    Anio = group.Key.Year,
                    Mes = group.Key.Month,
                    MontoAsignado = group.Sum(x => x.MontoDevengado),
                    DiasAplicados = group
                        .Select(x => x.Cobro.FechaCobro.Date)
                        .Distinct()
                        .Count()
                })
                .OrderBy(x => x.Anio)
                .ThenBy(x => x.Mes)
                .ToList();

            if (baseMensual.Count == 0 || montoPagado <= 0)
            {
                return Array.Empty<PagoFuncionarioDistribucionMensual>();
            }

            var totalBase = baseMensual.Sum(x => x.MontoAsignado);
            if (totalBase <= 0)
            {
                return Array.Empty<PagoFuncionarioDistribucionMensual>();
            }

            var distribucionFinal = new List<PagoFuncionarioDistribucionMensual>(baseMensual.Count);
            decimal montoAcumulado = 0;

            for (var index = 0; index < baseMensual.Count; index++)
            {
                var actual = baseMensual[index];
                var esUltimo = index == baseMensual.Count - 1;

                var montoAsignado = esUltimo
                    ? montoPagado - montoAcumulado
                    : Math.Round(
                        montoPagado * actual.MontoAsignado / totalBase,
                        2,
                        MidpointRounding.AwayFromZero);

                montoAcumulado += montoAsignado;

                distribucionFinal.Add(new PagoFuncionarioDistribucionMensual
                {
                    Anio = actual.Anio,
                    Mes = actual.Mes,
                    MontoAsignado = montoAsignado,
                    DiasAplicados = actual.DiasAplicados
                });
            }

            return distribucionFinal;
        }
    }

    public sealed class PagoFuncionarioDistribucionMensual
    {
        public int Anio { get; init; }
        public int Mes { get; init; }
        public decimal MontoAsignado { get; init; }
        public int DiasAplicados { get; init; }
    }
}
