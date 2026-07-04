using System.ComponentModel.DataAnnotations;
using LuxuryApp.Models.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxuryApp.Models.SaaS
{
    /// <summary>
    /// Intento de cambio de plan (típicamente "aumentar funcionarios") de un tenant.
    /// Sirve para (a) impedir múltiples cambios abiertos a la vez, (b) saber que existe una
    /// suscripción recurrente ANTERIOR en el proveedor que debe cancelarse manualmente
    /// (TiloPay no expone API de cancelación), y (c) alertar al admin. Tenant-scoped (RLS).
    /// </summary>
    public class PlanChangeIntent : ITenantEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [BindNever]
        public Guid TenantId { get; set; }

        // Origen (plan actual al iniciar el cambio).
        public Guid? FromPlanId { get; set; }

        [MaxLength(50)]
        public string? FromPlanCode { get; set; }

        public int? FromWorkerCount { get; set; }

        public int? FromTilopayRecurringPlanId { get; set; }

        [MaxLength(100)]
        public string? FromProviderSubscriptionId { get; set; }

        // Destino (plan nuevo deseado).
        public Guid ToPlanId { get; set; }

        [MaxLength(50)]
        public string ToPlanCode { get; set; } = string.Empty;

        public int ToWorkerCount { get; set; }

        public BillingCycle ToBillingCycle { get; set; }

        public int ToTilopayRecurringPlanId { get; set; }

        public PlanChangeIntentState Estado { get; set; } = PlanChangeIntentState.Pending;

        public ProviderCancellationState OldProviderCancellation { get; set; } = ProviderCancellationState.NotRequired;

        public Guid? PagoSuscripcionId { get; set; }

        [MaxLength(100)]
        public string? NewProviderSubscriptionId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? AppliedAtUtc { get; set; }

        [MaxLength(300)]
        public string? Notes { get; set; }
    }

    public enum PlanChangeIntentState
    {
        Pending = 0,
        Applied = 1,
        Failed = 2,
        Cancelled = 3,
        Superseded = 4
    }

    public enum ProviderCancellationState
    {
        NotRequired = 0,
        PendingManualCancellation = 1,
        Cancelled = 2
    }
}
