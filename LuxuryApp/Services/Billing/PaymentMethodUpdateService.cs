using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Security;
using LuxuryApp.Services.Tilopay;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.Billing
{
    public sealed record PaymentMethodUpdateResult
    {
        public required bool Succeeded { get; init; }

        /// <summary>URL de actualización (recurrentUrl). SOLO en éxito; nunca se persiste ni se loguea.</summary>
        public string? Url { get; init; }

        /// <summary>Mensaje seguro para la UI (sin secretos ni la URL).</summary>
        public string? Message { get; init; }

        /// <summary>True si el camino correcto es un checkout NUEVO (no recurrentUrl).</summary>
        public bool RequiresNewCheckout { get; init; }

        public static PaymentMethodUpdateResult Ok(string url) => new() { Succeeded = true, Url = url };
        public static PaymentMethodUpdateResult Fail(string message, bool requiresNewCheckout = false) =>
            new() { Succeeded = false, Message = message, RequiresNewCheckout = requiresNewCheckout };
    }

    public interface IPaymentMethodUpdateService
    {
        bool IsEnabled { get; }

        /// <summary>
        /// Genera ON-DEMAND la URL de TiloPay para actualizar tarjeta / reintentar el cobro
        /// (recurrentUrl) de la suscripción recurrente del tenant. Valida ownership, estado y que la
        /// URL sea HTTPS del dominio de TiloPay (anti open-redirect). NUNCA almacena la URL.
        /// </summary>
        Task<PaymentMethodUpdateResult> GenerateUpdateUrlAsync(
            Guid tenantId,
            string? email,
            string actorUserId,
            string actorEmail,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Igual que <see cref="GenerateUpdateUrlAsync"/> pero resolviendo el correo del tenant
        /// server-side (para uso de plataforma/soporte, que no debe pasar el email por el formulario).
        /// </summary>
        Task<PaymentMethodUpdateResult> GenerateUpdateUrlForTenantAsync(
            Guid tenantId,
            string actorUserId,
            string actorEmail,
            CancellationToken cancellationToken = default);
    }

    public sealed class PaymentMethodUpdateService : IPaymentMethodUpdateService
    {
        private readonly ApplicationDbContext _db;
        private readonly ITilopayRepeatAdminService _adminService;
        private readonly IBusinessDateTimeProvider _clock;
        private readonly ILogger<PaymentMethodUpdateService> _logger;

        public PaymentMethodUpdateService(
            ApplicationDbContext db,
            ITilopayRepeatAdminService adminService,
            IBusinessDateTimeProvider clock,
            ILogger<PaymentMethodUpdateService> logger)
        {
            _db = db;
            _adminService = adminService;
            _clock = clock;
            _logger = logger;
        }

        public bool IsEnabled => _adminService.IsEnabled;

        public async Task<PaymentMethodUpdateResult> GenerateUpdateUrlForTenantAsync(
            Guid tenantId,
            string actorUserId,
            string actorEmail,
            CancellationToken cancellationToken = default)
        {
            // Correo del admin del tenant resuelto server-side (no se confía en el formulario).
            var email = await _db.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.Email != null)
                .OrderBy(u => u.Email)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            return await GenerateUpdateUrlAsync(tenantId, email, actorUserId, actorEmail, cancellationToken);
        }

        public async Task<PaymentMethodUpdateResult> GenerateUpdateUrlAsync(
            Guid tenantId,
            string? email,
            string actorUserId,
            string actorEmail,
            CancellationToken cancellationToken = default)
        {
            if (!_adminService.IsEnabled)
            {
                return PaymentMethodUpdateResult.Fail("La actualización de tarjeta en línea no está disponible en este momento. Contactá soporte.");
            }

            if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(email))
            {
                return PaymentMethodUpdateResult.Fail("No pudimos generar el enlace de actualización. Contactá soporte.");
            }

            var subscription = await _db.Suscripciones
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.TilopayRecurringPlanId != null)
                .OrderByDescending(s => s.FechaUltimaActualizacionUtc ?? s.FechaInicio)
                .Select(s => new
                {
                    s.TilopayRecurringPlanId,
                    s.ProviderSubscriptionId,
                    s.Estado,
                    s.CancelAtPeriodEnd,
                    s.CancellationEffectiveAtUtc,
                    s.FechaFin,
                    s.ProviderExpiresAtUtc
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription?.TilopayRecurringPlanId is not { } recurringPlanId)
            {
                return PaymentMethodUpdateResult.Fail(
                    "No encontramos una suscripción recurrente para actualizar. Elegí un plan para continuar.",
                    requiresNewCheckout: true);
            }

            // Renovación cancelada: hay que reactivar primero (si vigente) o suscribirse de nuevo (si venció).
            if (subscription.CancelAtPeriodEnd)
            {
                var effectiveEndUtc = subscription.CancellationEffectiveAtUtc
                    ?? SubscriptionEffectiveDates.GetEffectiveEndUtc(subscription.FechaFin, subscription.ProviderExpiresAtUtc);

                if (effectiveEndUtc is { } end && end > GetUtcNow())
                {
                    return PaymentMethodUpdateResult.Fail("Tu renovación está cancelada. Reactivá tu renovación antes de actualizar el método de pago.");
                }

                return PaymentMethodUpdateResult.Fail(
                    "Tu suscripción finalizó. Iniciá una nueva suscripción para continuar.",
                    requiresNewCheckout: true);
            }

            if (subscription.Estado == EstadoSuscripcion.Cancelada)
            {
                return PaymentMethodUpdateResult.Fail(
                    "Tu suscripción está cancelada. Iniciá una nueva suscripción para continuar.",
                    requiresNewCheckout: true);
            }

            var nowUtc = GetUtcNow();
            var result = await _adminService.GetRecurrentUrlAsync(recurringPlanId, email, cancellationToken);

            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Url) || !IsSafeTilopayUrl(result.Url))
            {
                _db.PlatformAuditLogs.Add(BuildAudit(
                    PlatformAuditActions.PaymentMethodUpdateUrlFailed,
                    tenantId, actorUserId, actorEmail,
                    $"No se pudo generar/validar la recurrentUrl. Plan {recurringPlanId}. UrlValida {(!string.IsNullOrWhiteSpace(result.Url) && IsSafeTilopayUrl(result.Url))}.",
                    nowUtc));
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "recurrentUrl no disponible/insegura para actualizar tarjeta. TenantId {TenantId}. PlanId {PlanId}.",
                    tenantId, recurringPlanId);
                return PaymentMethodUpdateResult.Fail("No pudimos generar el enlace de actualización. Contactá soporte.");
            }

            _db.PlatformAuditLogs.Add(BuildAudit(
                PlatformAuditActions.PaymentMethodUpdateUrlGenerated,
                tenantId, actorUserId, actorEmail,
                $"recurrentUrl generada para actualizar método de pago. Plan {recurringPlanId}. EmailMasked {SensitiveDataMasker.MaskEmail(email)}.",
                nowUtc));
            await _db.SaveChangesAsync(cancellationToken);

            return PaymentMethodUpdateResult.Ok(result.Url!);
        }

        /// <summary>Anti open-redirect: solo se acepta redirigir a HTTPS del dominio de TiloPay.</summary>
        private static bool IsSafeTilopayUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            var host = uri.Host;
            return host.Equals("tilopay.com", StringComparison.OrdinalIgnoreCase) ||
                   host.EndsWith(".tilopay.com", StringComparison.OrdinalIgnoreCase);
        }

        private PlatformAuditLog BuildAudit(string action, Guid tenantId, string actorUserId, string actorEmail, string reason, DateTime nowUtc) =>
            new()
            {
                Id = Guid.NewGuid(),
                ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? "system" : actorUserId,
                ActorEmail = string.IsNullOrWhiteSpace(actorEmail) ? "system" : actorEmail,
                Action = action,
                EntityType = PlatformAuditEntityTypes.Subscription,
                TenantId = tenantId,
                Reason = reason.Length <= 500 ? reason : reason[..500],
                CreatedAtUtc = nowUtc
            };

        private DateTime GetUtcNow() => _clock.NowOffset().UtcDateTime;
    }
}
