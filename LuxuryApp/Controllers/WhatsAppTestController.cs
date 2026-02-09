using LuxuryApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers
{
    [Route("api/test/whatsapp")]
    [ApiController]
    public class WhatsAppTestController : ControllerBase
    {
        private readonly WhatsAppService _whatsappService;

        public WhatsAppTestController(WhatsAppService whatsappService)
        {
            _whatsappService = whatsappService;
        }

        [HttpGet("send")]
        public async Task<IActionResult> SendTest()
        {
            var telefono = "86720450"; 
            var mensaje = "💎 Mensaje de prueba LuxuryApp funcionando correctamente";

            await _whatsappService.SendMessageAsync(telefono, mensaje);

            return Ok("Mensaje enviado");
        }
    }
}
