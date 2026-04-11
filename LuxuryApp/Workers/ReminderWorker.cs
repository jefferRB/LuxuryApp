using LuxuryApp.Services;
using LuxuryApp.Services.Tenant;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Workers
{
    public class ReminderWorker : BackgroundService
    {
        private readonly TenantExecutionService _tenantExecutionService;
        private readonly ILogger<ReminderWorker> _logger;
        private readonly IConfiguration _config;

        public ReminderWorker(
            TenantExecutionService tenantExecutionService,
            ILogger<ReminderWorker> logger,
            IConfiguration config)
        {
            _tenantExecutionService = tenantExecutionService;
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
                    var nowCR = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzCR);

                    var template24 = _config["TwilioTemplates:Recordatorio24h"];
                    var template3 = _config["TwilioTemplates:Recordatorio3h"];

                    await _tenantExecutionService.RunForEachActiveTenantAsync(
                        async (serviceProvider, tenantId, cancellationToken) =>
                        {
                            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
                            var whatsapp = serviceProvider.GetRequiredService<WhatsAppService>();

                            var citas = await context.Citas
                                .Include(c => c.Funcionario)
                                .Include(c => c.Servicio)
                                .Where(c =>
                                    c.ConfirmacionEnviada &&
                                    (!c.Recordatorio24hEnviado || !c.Recordatorio3hEnviado))
                                .ToListAsync(cancellationToken);

                            foreach (var cita in citas)
                            {
                                try
                                {
                                    if (string.IsNullOrWhiteSpace(cita.TelefonoCliente))
                                        continue;

                                    var diff = cita.FechaHoraCita - nowCR;

                                    if (diff.TotalMinutes <= 0)
                                        continue;

                                    var funcionario = cita.Funcionario?.Nombre ?? "-";

                                    if (!cita.Recordatorio24hEnviado &&
                                        diff.TotalHours <= 24 &&
                                        diff.TotalHours > 23)
                                    {
                                        await whatsapp.SendTemplateAsync(
                                            cita.TelefonoCliente!,
                                            template24!,
                                            new Dictionary<string, object>
                                            {
                                                { "1", cita.NombreCliente ?? string.Empty },
                                                { "2", cita.FechaHoraCita.ToString("dd/MM/yyyy") },
                                                { "3", cita.FechaHoraCita.ToString("hh:mm tt") },
                                                { "4", cita.Servicio?.Nombre ?? string.Empty },
                                                { "5", funcionario }
                                            });

                                        cita.Recordatorio24hEnviado = true;

                                        _logger.LogInformation(
                                            "Recordatorio 24h enviado a CitaId {Id} para TenantId {TenantId}",
                                            cita.Id,
                                            tenantId);
                                    }

                                    if (!cita.Recordatorio3hEnviado &&
                                        diff.TotalHours <= 3 &&
                                        diff.TotalHours > 2)
                                    {
                                        await whatsapp.SendTemplateAsync(
                                            cita.TelefonoCliente!,
                                            template3!,
                                            new Dictionary<string, object>
                                            {
                                                { "1", cita.NombreCliente ?? string.Empty },
                                                { "2", cita.FechaHoraCita.ToString("dd/MM/yyyy") },
                                                { "3", cita.FechaHoraCita.ToString("hh:mm tt") },
                                                { "4", cita.Servicio?.Nombre ?? string.Empty },
                                                { "5", funcionario }
                                            });

                                        cita.Recordatorio3hEnviado = true;

                                        _logger.LogInformation(
                                            "Recordatorio 3h enviado a CitaId {Id} para TenantId {TenantId}",
                                            cita.Id,
                                            tenantId);
                                    }
                                }
                                catch (Exception exCita)
                                {
                                    _logger.LogError(
                                        exCita,
                                        "Error enviando recordatorio para CitaId {Id} del TenantId {TenantId}",
                                        cita.Id,
                                        tenantId);
                                }
                            }

                            await context.SaveChangesAsync(cancellationToken);
                        },
                        stoppingToken);
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
