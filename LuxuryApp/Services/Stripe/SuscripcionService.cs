using LuxuryApp.Models.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.SaaS
{
    public class SuscripcionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SuscripcionService> _logger;

        public SuscripcionService(
            ApplicationDbContext db,
            IMemoryCache cache,
            ILogger<SuscripcionService> logger)
        {
            _db = db;
            _cache = cache;
            _logger = logger;
        }

        public async Task ActivarSuscripcionAsync(
            Guid tenantId,
            Guid planId,
            PaymentProviderType provider,
            string? providerCustomerId,
            string? providerSubscriptionId,
            string? providerPaymentLinkId,
            string? providerTransactionId,
            string? providerReference,
            DateTime? trialEnd = null,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["PlanId"] = planId,
                ["Provider"] = provider,
                ["ProviderReference"] = providerReference
            });

            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            var nuevoEstado = trialEnd.HasValue ? EstadoSuscripcion.Trial : EstadoSuscripcion.Activa;

            if (suscripcion is null)
            {
                suscripcion = new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PlanId = planId,
                    Proveedor = provider,
                    ProviderCustomerId = providerCustomerId,
                    ProviderSubscriptionId = providerSubscriptionId,
                    ProviderPaymentLinkId = providerPaymentLinkId,
                    ProviderTransactionId = providerTransactionId,
                    ProviderReference = providerReference,
                    Estado = nuevoEstado,
                    FechaInicio = DateTime.UtcNow,
                    FechaTrialFin = trialEnd,
                    FechaUltimaActualizacionUtc = DateTime.UtcNow,
                    MotivoEstado = motivo,
                    CancelAtPeriodEnd = false
                };

                _db.Suscripciones.Add(suscripcion);
            }
            else
            {
                var planAnterior = suscripcion.PlanId;
                var estadoAnterior = suscripcion.Estado;

                suscripcion.PlanId = planId;
                suscripcion.Proveedor = provider;
                suscripcion.ProviderCustomerId = providerCustomerId ?? suscripcion.ProviderCustomerId;
                suscripcion.ProviderSubscriptionId = providerSubscriptionId ?? suscripcion.ProviderSubscriptionId;
                suscripcion.ProviderPaymentLinkId = providerPaymentLinkId ?? suscripcion.ProviderPaymentLinkId;
                suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
                suscripcion.ProviderReference = providerReference ?? suscripcion.ProviderReference;
                suscripcion.Estado = nuevoEstado;
                suscripcion.FechaTrialFin = trialEnd;
                suscripcion.FechaFin = null;
                suscripcion.CancelAtPeriodEnd = false;
                suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
                suscripcion.MotivoEstado = motivo;

                if (planAnterior != planId || estadoAnterior != nuevoEstado)
                {
                    _db.HistorialSuscripciones.Add(new HistorialSuscripcion
                    {
                        Id = Guid.NewGuid(),
                        SuscripcionId = suscripcion.Id,
                        PlanIdAnterior = planAnterior,
                        PlanIdNuevo = planId,
                        FechaCambio = DateTime.UtcNow,
                        Proveedor = provider,
                        Motivo = motivo ?? "Actualización de suscripción"
                    });
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);

            _logger.LogInformation(
                "Suscripción activada o actualizada correctamente. Estado {Estado}.",
                suscripcion.Estado);
        }

        public async Task RegistrarPagoConfirmadoAsync(
            Guid tenantId,
            Guid planId,
            Guid? pagoSuscripcionId,
            PaymentProviderType provider,
            string providerReference,
            string? providerTransactionId,
            string? providerAuthorizationCode,
            decimal monto,
            string moneda,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await EnsureSubscriptionForPaymentAsync(
                tenantId,
                planId,
                provider,
                providerReference,
                providerTransactionId,
                cancellationToken);

            var facturaExiste = await _db.Facturas
                .IgnoreQueryFilters()
                .AnyAsync(
                    factura => factura.Proveedor == provider &&
                    (
                        (!string.IsNullOrWhiteSpace(providerTransactionId) &&
                         factura.ProviderTransactionId == providerTransactionId) ||
                        factura.ProviderReference == providerReference
                    ),
                    cancellationToken);

            if (!facturaExiste)
            {
                _db.Facturas.Add(new Factura
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SuscripcionId = suscripcion.Id,
                    PagoSuscripcionId = pagoSuscripcionId,
                    Proveedor = provider,
                    ProviderInvoiceId = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    ProviderReference = providerReference,
                    Monto = monto,
                    Moneda = moneda,
                    Estado = "Pagado",
                    Fecha = DateTime.UtcNow
                });
            }

            suscripcion.Estado = EstadoSuscripcion.Activa;
            suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
            suscripcion.ProviderReference = providerReference;
            suscripcion.FechaUltimoPagoUtc = DateTime.UtcNow;
            suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
            suscripcion.MotivoEstado = motivo ?? "Pago confirmado";
            suscripcion.CancelAtPeriodEnd = false;

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);

            _logger.LogInformation("Pago confirmado registrado correctamente.");
        }

        public async Task RegistrarPagoFallidoAsync(
            Guid tenantId,
            Guid planId,
            Guid? pagoSuscripcionId,
            PaymentProviderType provider,
            string providerReference,
            string? providerTransactionId,
            decimal monto,
            string moneda,
            string? motivo = null,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            if (suscripcion is null)
            {
                suscripcion = new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PlanId = planId,
                    Proveedor = provider,
                    ProviderReference = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    Estado = EstadoSuscripcion.Fallida,
                    FechaInicio = DateTime.UtcNow,
                    FechaUltimaActualizacionUtc = DateTime.UtcNow,
                    MotivoEstado = motivo ?? "Pago fallido"
                };

                _db.Suscripciones.Add(suscripcion);
            }
            else
            {
                suscripcion.PlanId = planId;
                suscripcion.Proveedor = provider;
                suscripcion.ProviderReference = providerReference;
                suscripcion.ProviderTransactionId = providerTransactionId ?? suscripcion.ProviderTransactionId;
                suscripcion.Estado = suscripcion.Estado == EstadoSuscripcion.Activa || suscripcion.Estado == EstadoSuscripcion.Trial
                    ? EstadoSuscripcion.Morosa
                    : EstadoSuscripcion.Fallida;
                suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
                suscripcion.MotivoEstado = motivo ?? "Pago fallido";
            }

            var facturaExiste = await _db.Facturas
                .IgnoreQueryFilters()
                .AnyAsync(
                    factura => factura.Proveedor == provider &&
                    (
                        (!string.IsNullOrWhiteSpace(providerTransactionId) &&
                         factura.ProviderTransactionId == providerTransactionId) ||
                        factura.ProviderReference == providerReference
                    ),
                    cancellationToken);

            if (!facturaExiste)
            {
                _db.Facturas.Add(new Factura
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SuscripcionId = suscripcion.Id,
                    PagoSuscripcionId = pagoSuscripcionId,
                    Proveedor = provider,
                    ProviderInvoiceId = providerReference,
                    ProviderTransactionId = providerTransactionId,
                    ProviderReference = providerReference,
                    Monto = monto,
                    Moneda = moneda,
                    Estado = "Fallido",
                    Fecha = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(tenantId);

            _logger.LogWarning("Pago fallido registrado. Estado de suscripción actualizado a {Estado}.", suscripcion.Estado);
        }

        public async Task CancelarSuscripcionAsync(
            string providerSubscriptionId,
            bool cancelAtPeriodEnd,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.ProviderSubscriptionId == providerSubscriptionId,
                    cancellationToken);

            if (suscripcion is null)
            {
                return;
            }

            suscripcion.CancelAtPeriodEnd = cancelAtPeriodEnd;
            suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
            suscripcion.MotivoEstado = cancelAtPeriodEnd
                ? "Cancelación programada"
                : "Cancelación inmediata";

            if (!cancelAtPeriodEnd)
            {
                suscripcion.Estado = EstadoSuscripcion.Cancelada;
                suscripcion.FechaFin = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(suscripcion.TenantId);
        }

        public async Task ActualizarEstadoDesdeStripeAsync(
            string providerSubscriptionId,
            EstadoSuscripcion nuevoEstado,
            bool cancelAtPeriodEnd,
            CancellationToken cancellationToken = default)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => s.ProviderSubscriptionId == providerSubscriptionId,
                    cancellationToken);

            if (suscripcion is null)
            {
                return;
            }

            suscripcion.Estado = nuevoEstado;
            suscripcion.CancelAtPeriodEnd = cancelAtPeriodEnd;
            suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
            suscripcion.MotivoEstado = "Actualización de estado desde proveedor externo";

            if (nuevoEstado == EstadoSuscripcion.Cancelada)
            {
                suscripcion.FechaFin = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);
            InvalidateSubscriptionCache(suscripcion.TenantId);
        }

        // Compatibilidad razonable con el código legado de Stripe.
        public Task ActivarSuscripcionAsync(
            Guid tenantId,
            Guid planId,
            string subscriptionId,
            string customerId,
            DateTime? trialEnd = null) =>
            ActivarSuscripcionAsync(
                tenantId,
                planId,
                PaymentProviderType.Stripe,
                customerId,
                subscriptionId,
                providerPaymentLinkId: null,
                providerTransactionId: null,
                providerReference: subscriptionId,
                trialEnd: trialEnd,
                motivo: "Activación desde Stripe");

        public async Task RegistrarPagoAsync(
            string subscriptionId,
            string invoiceId,
            decimal monto,
            string moneda)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == subscriptionId);

            if (suscripcion is null)
            {
                return;
            }

            await RegistrarPagoConfirmadoAsync(
                suscripcion.TenantId,
                suscripcion.PlanId,
                pagoSuscripcionId: null,
                provider: PaymentProviderType.Stripe,
                providerReference: invoiceId,
                providerTransactionId: invoiceId,
                providerAuthorizationCode: null,
                monto: monto,
                moneda: moneda,
                motivo: "Pago confirmado desde Stripe");
        }

        public async Task MarcarPagoFallidoAsync(
            string subscriptionId,
            string invoiceId,
            decimal monto,
            string moneda)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == subscriptionId);

            if (suscripcion is null)
            {
                return;
            }

            await RegistrarPagoFallidoAsync(
                suscripcion.TenantId,
                suscripcion.PlanId,
                pagoSuscripcionId: null,
                provider: PaymentProviderType.Stripe,
                providerReference: invoiceId,
                providerTransactionId: invoiceId,
                monto: monto,
                moneda: moneda,
                motivo: "Pago fallido desde Stripe");
        }

        private async Task<Suscripcion> EnsureSubscriptionForPaymentAsync(
            Guid tenantId,
            Guid planId,
            PaymentProviderType provider,
            string providerReference,
            string? providerTransactionId,
            CancellationToken cancellationToken)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

            if (suscripcion is not null)
            {
                return suscripcion;
            }

            suscripcion = new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Proveedor = provider,
                ProviderReference = providerReference,
                ProviderTransactionId = providerTransactionId,
                Estado = EstadoSuscripcion.Pendiente,
                FechaInicio = DateTime.UtcNow,
                FechaUltimaActualizacionUtc = DateTime.UtcNow,
                MotivoEstado = "Suscripción creada desde confirmación de pago"
            };

            _db.Suscripciones.Add(suscripcion);
            await _db.SaveChangesAsync(cancellationToken);

            return suscripcion;
        }

        private void InvalidateSubscriptionCache(Guid tenantId)
        {
            _cache.Remove($"suscripcion_{tenantId}");
        }
    }
}
