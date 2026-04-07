using LuxuryApp.Models.SaaS;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Services.SaaS
{
    public class SuscripcionService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SuscripcionService> _logger;

        public SuscripcionService(ApplicationDbContext db, ILogger<SuscripcionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ================================
        // 🔹 ACTIVAR SUSCRIPCIÓN
        // ================================
        public async Task ActivarSuscripcionAsync(
            Guid tenantId,
            Guid planId,
            string subscriptionId,
            string customerId,
            DateTime? trialEnd = null)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (suscripcion == null)
            {
                suscripcion = new Suscripcion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PlanId = planId,
                    StripeSubscriptionId = subscriptionId,
                    StripeCustomerId = customerId,
                    Estado = trialEnd.HasValue ? EstadoSuscripcion.Trial : EstadoSuscripcion.Activa,
                    FechaInicio = DateTime.UtcNow,
                    FechaTrialFin = trialEnd,
                    CancelAtPeriodEnd = false
                };

                _db.Suscripciones.Add(suscripcion);
            }
            else
            {
                var planAnterior = suscripcion.PlanId;

                suscripcion.PlanId = planId;
                suscripcion.StripeSubscriptionId = subscriptionId;
                suscripcion.StripeCustomerId = customerId;
                suscripcion.Estado = EstadoSuscripcion.Activa;

                // 🔥 HISTORIAL CORRECTO
                _db.HistorialSuscripciones.Add(new HistorialSuscripcion
                {
                    Id = Guid.NewGuid(),
                    SuscripcionId = suscripcion.Id,
                    PlanIdAnterior = planAnterior,
                    PlanIdNuevo = planId,
                    FechaCambio = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("✅ Suscripción activada | Tenant: {TenantId}", tenantId);
        }

        // ================================
        // 🔹 REGISTRAR PAGO
        // ================================
        public async Task RegistrarPagoAsync(
          string subscriptionId,
          string stripeInvoiceId,
          decimal monto,
          string moneda)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscriptionId);

            if (suscripcion == null) return;

            // 🔥 PROTECCIÓN ANTI DUPLICADO
            var yaExiste = await _db.Facturas
                .AnyAsync(f => f.StripeInvoiceId == stripeInvoiceId);

            if (yaExiste)
            {
                _logger.LogWarning("⚠️ Factura ya registrada | InvoiceId: {InvoiceId}", stripeInvoiceId);
                return;
            }

            // 🔥 CREAR FACTURA
            _db.Facturas.Add(new Factura
            {
                Id = Guid.NewGuid(),
                TenantId = suscripcion.TenantId,
                StripeInvoiceId = stripeInvoiceId,
                Monto = monto,
                Moneda = moneda,
                Estado = "Pagado",
                Fecha = DateTime.UtcNow
            });

            // 🔥 RECUPERAR SI ESTABA MOROSA
            if (suscripcion.Estado == EstadoSuscripcion.Morosa)
                suscripcion.Estado = EstadoSuscripcion.Activa;

            await _db.SaveChangesAsync();

            _logger.LogInformation("💰 Pago registrado correctamente");
        }

        // ================================
        // 🔹 PAGO FALLIDO
        // ================================
        public async Task MarcarPagoFallidoAsync(
            string subscriptionId,
            string stripeInvoiceId,
            decimal monto,
            string moneda)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscriptionId);

            if (suscripcion == null) return;

            // 🔥 FACTURA FALLIDA
            _db.Facturas.Add(new Factura
            {
                Id = Guid.NewGuid(),
                TenantId = suscripcion.TenantId,
                StripeInvoiceId = stripeInvoiceId,
                Monto = monto,
                Moneda = moneda,
                Estado = "Fallido",
                Fecha = DateTime.UtcNow
            });

            suscripcion.Estado = EstadoSuscripcion.Morosa;

            await _db.SaveChangesAsync();

            _logger.LogWarning("⚠️ Pago fallido → suscripción morosa");
        }

        // ================================
        // 🔹 CANCELAR SUSCRIPCIÓN
        // ================================
        public async Task CancelarSuscripcionAsync(string subscriptionId, bool cancelAtPeriodEnd)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscriptionId);

            if (suscripcion == null) return;

            suscripcion.CancelAtPeriodEnd = cancelAtPeriodEnd;

            if (!cancelAtPeriodEnd)
            {
                suscripcion.Estado = EstadoSuscripcion.Cancelada;
                suscripcion.FechaFin = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            _logger.LogWarning("🛑 Suscripción cancelada");
        }

        public async Task ActualizarEstadoDesdeStripeAsync(
            string subscriptionId,
            EstadoSuscripcion nuevoEstado,
            bool cancelAtPeriodEnd)
        {
            var suscripcion = await _db.Suscripciones
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == subscriptionId);

            if (suscripcion == null) return;

            suscripcion.Estado = nuevoEstado;
            suscripcion.CancelAtPeriodEnd = cancelAtPeriodEnd;

            if (nuevoEstado == EstadoSuscripcion.Cancelada)
            {
                suscripcion.FechaFin = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "🔄 Estado actualizado desde Stripe | Sub: {SubscriptionId} | Estado: {Estado}",
                subscriptionId, nuevoEstado);
        }

    }
}