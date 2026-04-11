using LuxuryApp.Services.Tenant;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LuxuryApp.Services
{
    public class VisitasBackgroundService : BackgroundService
    {
        private readonly TenantExecutionService _tenantExecutionService;

        public VisitasBackgroundService(TenantExecutionService tenantExecutionService)
        {
            _tenantExecutionService = tenantExecutionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _tenantExecutionService.RunForEachActiveTenantAsync(
                    async (serviceProvider, _, cancellationToken) =>
                    {
                        var servicio = serviceProvider.GetRequiredService<VisitasAutomaticasService>();
                        await servicio.ProcesarCitasFinalizadas(cancellationToken);
                    },
                    stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
