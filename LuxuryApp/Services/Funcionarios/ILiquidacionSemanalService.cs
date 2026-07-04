using LuxuryApp.Models.Funcionarios;

namespace LuxuryApp.Services.Funcionarios
{
    public interface ILiquidacionSemanalService
    {
        Task<PagosSemanaResumen> ObtenerResumenSemanaAsync(DateTime fechaReferencia, CancellationToken cancellationToken = default);
        Task<PagosSemanaResumen> ObtenerResumenSemanaAsync(DateTime inicioSemana, DateTime finSemana, CancellationToken cancellationToken = default);
        Task<int> RegistrarPagoAsync(RegistrarLiquidacionSemanalCommand command, CancellationToken cancellationToken = default);

        /// <summary>Diagnóstico de atribución de pagos al rango (incluidos/excluidos y por qué).</summary>
        Task<IReadOnlyList<PagoAtribucionDiagnostico>> ObtenerDiagnosticoPagosAsync(
            DateTime inicioSemana, DateTime finSemana, CancellationToken cancellationToken = default);
    }
}
