using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;

namespace LuxuryApp.Models.SaaS
{
    public class TenantSubscriptionAddon : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public Guid TenantId { get; set; }

        public Guid PlanId { get; set; }

        [MaxLength(50)]
        public string AddonCode { get; set; } = string.Empty;

        public EstadoSuscripcion Estado { get; set; } = EstadoSuscripcion.Pendiente;

        public int? TilopayRecurringPlanId { get; set; }

        [MaxLength(100)]
        public string? ProviderSubscriptionId { get; set; }

        [MaxLength(100)]
        public string? ProviderTransactionId { get; set; }

        public decimal? PrecioMensual { get; set; }

        [MaxLength(10)]
        public string? MonedaFacturacion { get; set; }

        public int MonthlyMessageLimit { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

        public DateTime? FechaFin { get; set; }

        public DateTime? FechaProximoCobroUtc { get; set; }

        public DateTime? FechaFinGraciaUtc { get; set; }

        public DateTime? FechaCancelacionUtc { get; set; }

        // ── Cancelación de renovación programada del add-on (cascada base→add-on / cliente) ──
        // Igual que en el plan base: cancelar la RENOVACIÓN no corta el uso ya pagado; el add-on
        // sigue Activa hasta FechaFin y ahí expira solo (GetEffectiveStatus lo pasa a Suspendida).
        // Lo money-critical es dar de baja el suscriptor en TiloPay para que no vuelva a cobrar.

        /// <summary>El cliente/soporte canceló la renovación del add-on. El uso sigue hasta FechaFin.</summary>
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>Fecha efectiva en que la cancelación programada del add-on corta el uso (fin de período pagado).</summary>
        public DateTime? CancellationEffectiveAtUtc { get; set; }

        [MaxLength(450)]
        public string? CancellationRequestedByUserId { get; set; }

        [MaxLength(250)]
        public string? CancellationReason { get; set; }

        // ── Cancelación SALIENTE pendiente del suscriptor en TiloPay (Strategy B + cascada) ──
        // El suscriptor a cancelar puede ser el HUÉRFANO de un upgrade (WA400→WA800: se cancela el
        // viejo DESPUÉS de confirmar el nuevo) o el ACTUAL (cuando el cliente/plataforma cancela el
        // add-on o se canceló el plan base). Se guarda aparte de ProviderSubscriptionId para poder
        // cancelar el viejo sin perder el nuevo. El presupuesto de reintentos vive AQUÍ (nunca se
        // deduce contando auditorías): solo un intento REAL contra TiloPay mueve estos campos.

        /// <summary>Suscriptor de TiloPay que debe darse de baja. Null = nada pendiente.</summary>
        [MaxLength(100)]
        public string? PendingCancellationProviderSubscriptionId { get; set; }

        /// <summary>Plan recurrente de TiloPay del suscriptor a cancelar (para verificar por getSuscriptorRepeat).</summary>
        public int? PendingCancellationTilopayRecurringPlanId { get; set; }

        public ProviderCancellationState ProviderCancellation { get; set; } = ProviderCancellationState.NotRequired;

        /// <summary>Intentos REALES contra TiloPay desde el último reinicio de presupuesto. Los skips no cuentan.</summary>
        public int ProviderCancellationAttemptCount { get; set; }

        public DateTime? ProviderCancellationLastAttemptUtc { get; set; }

        /// <summary>Momento a partir del cual se permite el próximo intento real. NULL = elegible ya.</summary>
        public DateTime? ProviderCancellationNextRetryUtc { get; set; }

        /// <summary>Cuándo se VERIFICÓ contra TiloPay que el suscriptor a cancelar quedó inactivo.</summary>
        public DateTime? ProviderCancelledAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Tenant? Tenant { get; set; }
        public Plan? Plan { get; set; }
    }
}
