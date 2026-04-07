using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using ProyectoIdentity.Datos;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;

namespace LuxuryApp.Controllers
{
    [ApiController]
    [Route("api/webhooks/stripe")]
    public class StripeWebhookController : ControllerBase
    {
        private readonly ILogger<StripeWebhookController> _logger;
        private readonly IConfiguration _config;
        private readonly IServiceProvider _serviceProvider;
        private readonly SuscripcionService _suscripcionService;

        public StripeWebhookController(
            ILogger<StripeWebhookController> logger,
            IConfiguration config,
            IServiceProvider serviceProvider,
            SuscripcionService suscripcionService)
        {
            _logger = logger;
            _config = config;
            _serviceProvider = serviceProvider;
            _suscripcionService = suscripcionService;
        }

        [HttpPost]
        public async Task<IActionResult> Handle()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            Event stripeEvent;

            try
            {
                stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    _config["Stripe:WebhookSecret"]
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Firma inválida");
                return BadRequest();
            }

            using var scopeLog = _logger.BeginScope(new Dictionary<string, object>
            {
                ["EventId"] = stripeEvent.Id,
                ["Type"] = stripeEvent.Type
            });

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // 🔥 IDEMPOTENCIA (rápida)
                var existe = await db.StripeEventos
                    .AnyAsync(e => e.StripeEventId == stripeEvent.Id);

                if (existe)
                {
                    _logger.LogWarning("⚠️ Evento duplicado ignorado (pre-check)");
                    return Ok();
                }

                // 🔥 CREAR EVENTO
                var evento = new StripeEvento
                {
                    Id = Guid.NewGuid(),
                    StripeEventId = stripeEvent.Id,
                    Tipo = stripeEvent.Type,
                    Payload = json,
                    Procesado = false,
                    Fecha = DateTime.UtcNow
                };

                // 🔥 GUARDAR CON PROTECCIÓN REAL (race condition)
                try
                {
                    db.StripeEventos.Add(evento);
                    await db.SaveChangesAsync();

                    _logger.LogInformation("✅ Evento guardado");
                }
                catch (DbUpdateException)
                {
                    _logger.LogWarning(
                        "⚠️ Evento duplicado (race condition) | EventId: {EventId}",
                        stripeEvent.Id);

                    return Ok(); // 🔥 IMPORTANTE: no reprocesar
                }

                // 🔥 PROCESAMIENTO
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        await HandleCheckoutCompleted(stripeEvent);
                        break;

                    case "invoice.payment_succeeded":
                        await HandlePaymentSucceeded(stripeEvent);
                        break;

                    case "invoice.payment_failed":
                        await HandlePaymentFailed(stripeEvent);
                        break;

                    case "customer.subscription.updated":
                        await HandleSubscriptionUpdated(stripeEvent);
                        break;

                    case "customer.subscription.deleted":
                        await HandleSubscriptionDeleted(stripeEvent);
                        break;

                    default:
                        _logger.LogWarning("Evento no manejado");
                        break;
                }

                // 🔥 MARCAR COMO PROCESADO
                evento.Procesado = true;
                await db.SaveChangesAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error procesando webhook");
                return StatusCode(500);
            }
        }

        // ================================
        // 🔹 CHECKOUT COMPLETADO
        // ================================
        private async Task HandleCheckoutCompleted(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
            if (session == null) return;

            if (session.Metadata == null ||
                !session.Metadata.ContainsKey("TenantId") ||
                !session.Metadata.ContainsKey("PlanId"))
            {
                _logger.LogWarning("Metadata incompleta en checkout");
                return;
            }

            if (!Guid.TryParse(session.Metadata["TenantId"], out var tenantId))
            {
                _logger.LogError("TenantId inválido");
                return;
            }
            var planId = Guid.Parse(session.Metadata["PlanId"]);

            _logger.LogInformation(
                "🎉 Checkout OK | Session: {SessionId} | Tenant: {TenantId}",
                session.Id, tenantId);

            await _suscripcionService.ActivarSuscripcionAsync(
                tenantId,
                planId,
                session.SubscriptionId,
                session.CustomerId
            );
        }

        // ================================
        // 🔹 PAGO EXITOSO
        // ================================
        private async Task HandlePaymentSucceeded(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice == null) return;

            var line = invoice.Lines?.Data?.FirstOrDefault();

            var subscriptionId = line?.Subscription?.Id;

            if (string.IsNullOrEmpty(subscriptionId))
            {
                _logger.LogWarning("No se encontró subscription en invoice");
                return;
            }

            _logger.LogInformation("💰 Pago OK | Invoice: {InvoiceId}", invoice.Id);

            await _suscripcionService.RegistrarPagoAsync(
                subscriptionId,
                invoice.Id,
                invoice.AmountPaid / 100m,
                invoice.Currency
            );
        }

        // ================================
        // 🔹 PAGO FALLIDO
        // ================================
        private async Task HandlePaymentFailed(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice == null) return;

            var line = invoice.Lines?.Data?.FirstOrDefault();

            var subscriptionId = line?.Subscription?.Id;

            if (string.IsNullOrEmpty(subscriptionId))
            {
                _logger.LogWarning("No se encontró subscription en invoice");
                return;
            }

            _logger.LogWarning("❌ Pago FALLIDO | {InvoiceId}", invoice.Id);

            await _suscripcionService.MarcarPagoFallidoAsync(
                subscriptionId,
                invoice.Id,
                invoice.AmountDue / 100m,
                invoice.Currency
            );
        }
        // ================================
        // 🔹 SUB ACTUALIZADA
        // ================================
        private async Task HandleSubscriptionUpdated(Event stripeEvent)
        {
            var sub = stripeEvent.Data.Object as Subscription;
            if (sub == null) return;

            _logger.LogInformation(
                "🔄 Sub actualizada | {SubscriptionId} | {Status}",
                sub.Id, sub.Status);

            EstadoSuscripcion nuevoEstado = sub.Status switch
            {
                "trialing" => EstadoSuscripcion.Trial,
                "active" => EstadoSuscripcion.Activa,
                "past_due" => EstadoSuscripcion.Morosa,
                "unpaid" => EstadoSuscripcion.Morosa,
                "canceled" => EstadoSuscripcion.Cancelada,
                _ => EstadoSuscripcion.Activa
            };

            await _suscripcionService.ActualizarEstadoDesdeStripeAsync(
                sub.Id,
                nuevoEstado,
                sub.CancelAtPeriodEnd
            );
        }

        // ================================
        // 🔹 SUB ELIMINADA
        // ================================
        private async Task HandleSubscriptionDeleted(Event stripeEvent)
        {
            var sub = stripeEvent.Data.Object as Subscription;
            if (sub == null) return;

            _logger.LogWarning("🛑 Sub eliminada | {SubscriptionId}", sub.Id);

            await _suscripcionService.CancelarSuscripcionAsync(
                sub.Id,
                false
            );
        }
    }
}