using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Tenant;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class TenantProvisioningServiceTests
    {
        [Fact]
        public async Task RegisterAsync_ShouldRollbackTenantWhenUserCreationFails()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddSingleton<ITenantExecutionContextAccessor, TenantExecutionContextAccessor>();
            services.AddScoped<ITenantProvider, TenantProvider>();
            services.Configure<IdentityOptions>(_ => { });
            services.Configure<OpcionesOnboardingTenant>(options =>
            {
                options.RegistrationRole = "Administrador";
                options.AddRegisteredRole = true;
                options.RegisteredRole = "Registrado";
                options.CreateInitialSubscription = false;
            });
            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(connection);
            });
            services
                .AddIdentity<AppUsuario, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.AddScoped<TenantProvisioningService>();

            await using var provider = services.BuildServiceProvider();

            using (var setupScope = provider.CreateScope())
            {
                var dbContext = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
            }

            using (var scope = provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();

                var result = await service.RegisterAsync(new TenantRegistrationRequest
                {
                    Name = "Tenant fallido",
                    Email = "rollback@test.local",
                    PhoneNumber = "88888888",
                    Password = "x"
                });

                Assert.False(result.Succeeded);
            }

            using (var assertScope = provider.CreateScope())
            {
                var dbContext = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                Assert.Equal(0, await dbContext.Tenants.CountAsync());
                Assert.Equal(0, await dbContext.Users.CountAsync());
            }

            await connection.DisposeAsync();
        }
    }
}
