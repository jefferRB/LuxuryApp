using LuxuryApp.Services;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Workers
{
    public class ReminderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReminderWorker> _logger;
        private readonly IConfiguration _config;

        public ReminderWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<ReminderWorker> logger,
            IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tzCR = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");

            _logger.LogInformation("ReminderWorker iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var whatsapp = scope.ServiceProvider.GetRequiredService<WhatsAppService>();

                    var nowCR = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzCR);

                    // Templates desde configuración
                    var template24 = _config["TwilioTemplates:Recordatorio24h"];
                    var template3 = _config["TwilioTemplates:Recordatorio3h"];

                    var citas = await context.Citas
                        .Include(c => c.Funcionario)
                        .Where(c =>
    c.ConfirmacionEnviada &&
    (!c.Recordatorio24hEnviado ||
     !c.Recordatorio3hEnviado))
                        .ToListAsync(stoppingToken);

                    foreach (var cita in citas)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(cita.TelefonoCliente))
                                continue;

                            var diff = cita.FechaHoraCita - nowCR;

                            if (diff.TotalMinutes <= 0)
                                continue;

                            var funcionario = cita.Funcionario?.Nombre ?? "—";

                            // =========================
                            // RECORDATORIO 24 HORAS
                            // =========================
                            if (!cita.Recordatorio24hEnviado &&
                                diff.TotalHours <= 24 &&
                                diff.TotalHours > 23)
                            {
                                await whatsapp.SendTemplateAsync(
                                    cita.TelefonoCliente!,
                                    template24!,
                                    new Dictionary<string, object>
                                    {
                                        { "1", cita.NombreCliente },
                                        { "2", cita.FechaHoraCita.ToString("dd/MM/yyyy") },
                                        { "3", cita.FechaHoraCita.ToString("hh:mm tt") },
                                        { "4", cita.Servicio },
                                        { "5", funcionario }
                                    });

                                cita.Recordatorio24hEnviado = true;

                                _logger.LogInformation(
                                    "Recordatorio 24h enviado a CitaId {Id}",
                                    cita.Id);
                            }

                            // =========================
                            // RECORDATORIO 3 HORAS
                            // =========================
                            if (!cita.Recordatorio3hEnviado &&
                                diff.TotalHours <= 3 &&
                                diff.TotalHours > 2)
                            {
                                await whatsapp.SendTemplateAsync(
                                    cita.TelefonoCliente!,
                                    template3!,
                                    new Dictionary<string, object>
                                    {
                                        { "1", cita.NombreCliente },
                                        { "2", cita.FechaHoraCita.ToString("dd/MM/yyyy") },
                                        { "3", cita.FechaHoraCita.ToString("hh:mm tt") },
                                        { "4", cita.Servicio },
                                        { "5", funcionario }
                                    });

                                cita.Recordatorio3hEnviado = true;

                                _logger.LogInformation(
                                    "Recordatorio 3h enviado a CitaId {Id}",
                                    cita.Id);
                            }
                        }
                        catch (Exception exCita)
                        {
                            _logger.LogError(exCita,
                                "Error enviando recordatorio para CitaId {Id}",
                                cita.Id);
                        }
                    }

                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error general en ReminderWorker");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}