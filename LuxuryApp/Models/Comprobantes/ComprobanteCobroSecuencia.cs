using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.Comprobantes
{
    /// <summary>
    /// Contador de numeración interna de comprobantes por tenant. Se incrementa en una
    /// transacción serializable del lado servidor para garantizar números únicos y
    /// consecutivos por tenant (nunca se confía en el cliente). El índice único
    /// TenantId + NumeroInterno en <see cref="ComprobanteCobro"/> es la red de seguridad final.
    /// </summary>
    public class ComprobanteCobroSecuencia : ITenantEntity
    {
        public Guid TenantId { get; set; }

        /// <summary>Último número emitido. El siguiente comprobante usa UltimoNumero + 1.</summary>
        public long UltimoNumero { get; set; }
    }
}
