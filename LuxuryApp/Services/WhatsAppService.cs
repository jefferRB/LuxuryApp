using Microsoft.Extensions.Configuration;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace LuxuryApp.Services
{
    public class WhatsAppService
    {
        private readonly IConfiguration _config;

        public WhatsAppService(IConfiguration config)
        {
            _config = config;

            var sid = _config["Twilio:AccountSid"];
            var token = _config["Twilio:AuthToken"];

            TwilioClient.Init(sid, token);
        }

        public async Task SendMessageAsync(string telefono, string mensaje)
        {
            if (!telefono.StartsWith("+"))
                telefono = "+506" + telefono;

            await MessageResource.CreateAsync(
                from: new PhoneNumber(_config["Twilio:WhatsAppFrom"]),
                to: new PhoneNumber($"whatsapp:{telefono}"),
                body: mensaje
            );
        }
    }
}
