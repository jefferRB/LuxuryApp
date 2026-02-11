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

            TwilioClient.Init(
                _config["Twilio:AccountSid"],
                _config["Twilio:AuthToken"]);
        }

        public async Task SendTemplateAsync(
            string telefono,
            string templateSid,
            Dictionary<string, object> variables)
        {
            if (!telefono.StartsWith("+"))
                telefono = "+506" + telefono;

            await MessageResource.CreateAsync(
                from: new PhoneNumber(_config["Twilio:WhatsAppFrom"]),
                to: new PhoneNumber($"whatsapp:{telefono}"),
                contentSid: templateSid,
                contentVariables: System.Text.Json.JsonSerializer.Serialize(variables)
            );
        }

        // OPCIONAL → dejar para debug
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
