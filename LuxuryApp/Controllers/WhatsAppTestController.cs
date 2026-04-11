using LuxuryApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers
{
    [Route("api/test/whatsapp")]
    [ApiController]
    [Authorize(Roles = "Administrador")]
    public class WhatsAppTestController : ControllerBase
    {
        private readonly WhatsAppService _whatsappService;
        private readonly IWebHostEnvironment _environment;

        public WhatsAppTestController(
            WhatsAppService whatsappService,
            IWebHostEnvironment environment)
        {
            _whatsappService = whatsappService;
            _environment = environment;
        }

        [HttpGet("send")]
        public async Task<IActionResult> SendTest()
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            var telefono = "86720450";
            var mensaje = "Mensaje de prueba LuxuryApp funcionando correctamente";

            await _whatsappService.SendMessageAsync(telefono, mensaje);

            return Ok("Mensaje enviado");
        }
    }
}
