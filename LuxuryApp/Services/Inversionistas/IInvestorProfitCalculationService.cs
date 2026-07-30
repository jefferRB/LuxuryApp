using LuxuryApp.Models.Inversionistas;

namespace LuxuryApp.Services.Inversionistas
{
    /// <summary>
    /// Desglose de la ganancia distribuible de un periodo, ANTES de aplicar ajustes manuales y
    /// pérdidas arrastradas (esos los aplica <c>IInvestorStatementService</c> porque dependen del
    /// estado de cuenta concreto).
    /// </summary>
    public sealed record InvestorProfitBreakdown
    {
        public DateOnly PeriodoInicio { get; init; }

        public DateOnly PeriodoFin { get; init; }

        /// <summary>Cobros reales del periodo, con IVA incluido.</summary>
        public decimal IngresosCobrados { get; init; }

        /// <summary>IVA contenido en esos cobros. Cero si la política no excluye IVA.</summary>
        public decimal IvaExcluido { get; init; }

        /// <summary>Ingresos base del cálculo (sin IVA si la política lo excluye).</summary>
        public decimal IngresosNetos { get; init; }

        /// <summary>Gastos operativos elegibles según la política.</summary>
        public decimal GastosElegibles { get; init; }

        /// <summary>Liquidaciones/comisiones de colaboradores. Cero si la política las excluye.</summary>
        public decimal Liquidaciones { get; init; }

        /// <summary>Resultado operativo antes de ajustes y pérdidas arrastradas. Puede ser negativo.</summary>
        public decimal ResultadoOperativo => IngresosNetos - GastosElegibles - Liquidaciones;

        /// <summary>Descripción de la política usada, para congelar en el snapshot.</summary>
        public string PoliticaVersion { get; init; } = InvestorDefaults.PolicyVersion;

        /// <summary>Desglose de gastos por categoría, solo informativo para la vista previa.</summary>
        public IReadOnlyList<InvestorExpenseCategoryBreakdown> GastosPorCategoria { get; init; } =
            Array.Empty<InvestorExpenseCategoryBreakdown>();
    }

    public sealed record InvestorExpenseCategoryBreakdown(
        int CategoriaId,
        string CategoriaNombre,
        decimal Monto,
        bool Incluido,
        string? MotivoExclusion);

    /// <summary>
    /// Motor único de la ganancia distribuible. Toda la lógica financiera del módulo de
    /// inversionistas vive acá: los controladores nunca calculan dinero.
    /// </summary>
    public interface IInvestorProfitCalculationService
    {
        Task<InvestorProfitBreakdown> CalculateAsync(
            DateOnly periodoInicio,
            DateOnly periodoFin,
            InvestorProfitPolicy policy,
            CancellationToken cancellationToken = default);
    }
}
