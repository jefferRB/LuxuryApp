using System.Security.Cryptography;
using System.Text;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("api/webhooks/tilopay")]
    public class TilopayWebhookController : ControllerBase
    {
        private readonly ILogger<TilopayWebhookController> _logger;
        private readonly SaaSPaymentService _paymentService;
        private readonly OpcionesTilopay _options;

        public TilopayWebhookController(
            ILogger<TilopayWebhookController> logger,
            SaaSPaymentService paymentService,
            IOptions<OpcionesTilopay> options)
        {
            _logger = logger;
            _paymentService = paymentService;
            _options = options.Value;
        }

        [HttpPost]
        public async Task<IActionResult> Handle(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.WebhookAccessToken))
            {
                _logger.LogCritical("Tilopay webhook rechazado porque no existe WebhookAccessToken configurado.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (!Request.Query.TryGetValue(_options.WebhookAccessTokenQueryParameter, out var incomingToken) ||
                !SecureEquals(incomingToken.ToString(), _options.WebhookAccessToken))
            {
                _logger.LogWarning("Tilopay webhook rechazado por token de acceso inválido.");
                return Unauthorized();
            }

            var body = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest("Payload vacío.");
            }

            var correlationId = HttpContext.TraceIdentifier;

            try
            {
                var result = await _paymentService.ProcessTilopayWebhookAsync(
                    body,
                    correlationId,
                    cancellationToken);

                return Ok(new
                {
                    accepted = true,
                    duplicate = result.IsDuplicate,
                    processed = result.IsProcessed
                });
            }
            catch (PaymentWebhookValidationException ex)
            {
                await _paymentService.RegisterRejectedWebhookAsync(
                    PaymentProviderType.Tilopay,
                    body,
                    ex.Message,
                    correlationId,
                    cancellationToken);

                _logger.LogWarning(ex, "Tilopay webhook rechazado por payload invalido.");
                return BadRequest("Payload invalido.");
            }
            catch (PaymentProviderConfigurationException ex)
            {
                _logger.LogCritical(ex, "Tilopay webhook rechazado por configuracion incompleta.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando webhook Tilopay.");
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        private static bool SecureEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);

            if (leftBytes.Length != rightBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
