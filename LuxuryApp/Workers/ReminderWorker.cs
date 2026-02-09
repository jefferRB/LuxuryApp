using LuxuryApp.Services;
using Microsoft.EntityFrameworkCore;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Workers
{
    public class ReminderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReminderWorker> _logger;

        public ReminderWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tzCR = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var whatsapp = scope.ServiceProvider.GetRequiredService<WhatsAppService>();

                    var nowCR = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzCR);

                    // Solo citas entre ahora y 24h + 10 min (ventana de búsqueda)
                    var limite = nowCR.AddHours(24).AddMinutes(10);

                    var citas = await context.Citas
                        .Include(c => c.CitaBarberos)
                            .ThenInclude(cb => cb.Barbero)
                        .Where(c => c.FechaHoraCita >= nowCR && c.FechaHoraCita <= limite)
                        .ToListAsync(stoppingToken);

                    foreach (var cita in citas)
                    {
                        var diff = cita.FechaHoraCita - nowCR;
                        if (diff.TotalMinutes <= 0)
                            continue;

                        var barberos = string.Join(", ",
                            cita.CitaBarberos.Select(b => b.Barbero.Nombre));

                        // ---- 24 HORAS (ventana 23h–24h para evitar duplicados) ----
                        if (!cita.Recordatorio24hEnviado &&
                        diff.TotalHours <= 24)
                        {
                            await whatsapp.SendMessageAsync(
                             cita.TelefonoCliente!,
                     GenerarMensaje(cita, barberos, "24 horas"));

                            cita.Recordatorio24hEnviado = true;

                            _logger.LogInformation("24h enviado a CitaId {Id}", cita.Id);
                        }
                        {
                            await whatsapp.SendMessageAsync(
                            cita.TelefonoCliente!,
                            GenerarMensaje(cita, barberos, "3 horas"));
                            cita.Recordatorio24hEnviado = true;

                            _logger.LogInformation("24h enviado a CitaId {Id}", cita.Id);
                        }

                        // ---- 3 HORAS (ventana 2h–3h) ----
                        if (!cita.Recordatorio3hEnviado &&
                        diff.TotalHours <= 3)
                        {
                            var mensaje = GenerarMensaje(cita, barberos, "3 horas");
                            await whatsapp.SendMessageAsync(cita.TelefonoCliente!, mensaje);

                            cita.Recordatorio3hEnviado = true;

                            _logger.LogInformation("3h enviado a CitaId {Id}", cita.Id);
                        }
                        {
                            var mensaje = GenerarMensaje(cita, barberos, "3 horas");
                            await whatsapp.SendMessageAsync(cita.TelefonoCliente!, mensaje);
                            cita.Recordatorio3hEnviado = true;

                            _logger.LogInformation("3h enviado a CitaId {Id}", cita.Id);
                        }
                    }

                    await context.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en ReminderWorker");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private static string GenerarMensaje(Models.Calendar.Cita cita, string barberos, string etiqueta)
        {
            return
$@"💎 Luxury Barbería

Hola {cita.NombreCliente} 👋

Recordatorio ({etiqueta}) de tu cita:

📅 {cita.FechaHoraCita:dd/MM/yyyy}
⏰ {cita.FechaHoraCita:hh:mm tt}
✂ Servicio: {cita.Servicio}
💈 Barbero(s): {barberos}

¡Te esperamos!";
        }
    }
}
