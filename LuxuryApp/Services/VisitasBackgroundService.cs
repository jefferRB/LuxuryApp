using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LuxuryApp.Services
{
    public class VisitasBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public VisitasBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var servicio = scope.ServiceProvider
                        .GetRequiredService<VisitasAutomaticasService>();

                    await servicio.ProcesarCitasFinalizadas();
                }

                // 🔥 Corre cada 5 minutos
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}