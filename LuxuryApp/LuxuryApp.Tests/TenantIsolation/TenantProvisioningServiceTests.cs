using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Legal;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Contracts;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class TenantProvisioningServiceTests
    {
        [Fact]
        public async Task RegisterAsync_ShouldRollbackTenantWhenUserCreationFails()
        {
            await using var provider = await CreateServiceProviderAsync();

            using (var scope = provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();

                var result = await service.RegisterAsync(new TenantRegistrationRequest
                {
                    Name = "Tenant fallido",
                    Email = "rollback@test.local",
                    PhoneNumber = "88888888",
                    Password = "x",
                    AcceptCurrentContract = true,
                    SubmittedContractDocumentId = ContractDocumentSeedData.InitialDocumentId,
                    ContractIpAddress = "203.0.113.10",
                    ContractUserAgent = "TenantProvisioningServiceTests/rollback"
                });

                Assert.False(result.Succeeded);
            }

            using (var assertScope = provider.CreateScope())
            {
                var dbContext = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                Assert.Equal(0, await dbContext.Tenants.CountAsync());
                Assert.Equal(0, await dbContext.Users.CountAsync());
                Assert.Equal(0, await dbContext.ContractAcceptanceRecords.CountAsync());
            }
        }

        [Fact]
        public async Task RegisterAsync_ShouldPersistContractAcceptanceEvidenceOnSuccessfulProvisioning()
        {
            await using var provider = await CreateServiceProviderAsync();

            TenantProvisioningResult result;

            using (var scope = provider.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();

                result = await service.RegisterAsync(new TenantRegistrationRequest
                {
                    Name = "Luxury Tenant",
                    Email = "owner-contract@test.local",
                    PhoneNumber = "88888888",
                    Password = "Valid1!",
                    AcceptCurrentContract = true,
                    SubmittedContractDocumentId = ContractDocumentSeedData.InitialDocumentId,
                    ContractIpAddress = "198.51.100.25",
                    ContractUserAgent = "TenantProvisioningServiceTests/success"
                });
            }

            Assert.True(result.Succeeded);
            Assert.NotNull(result.User);

            using (var assertScope = provider.CreateScope())
            {
                var dbContext = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var acceptance = await dbContext.ContractAcceptanceRecords.SingleAsync();
                var activeDocument = await dbContext.ContractDocuments.SingleAsync(document => document.IsActive);

                Assert.Equal(result.User!.Id, acceptance.UserId);
                Assert.Equal(activeDocument.Id, acceptance.ContractDocumentId);
                Assert.Equal(activeDocument.VersionNumber, acceptance.ContractVersion);
                Assert.Equal(activeDocument.ContentHash, acceptance.AcceptedContentHash);
                Assert.Equal(ContractAcceptanceSources.Register, acceptance.AcceptanceSource);
                Assert.Equal("198.51.100.25", acceptance.IpAddress);
                Assert.Equal("TenantProvisioningServiceTests/success", acceptance.UserAgent);
                Assert.NotEqual(default, acceptance.AcceptedAtUtc);
            }
        }

        private static async Task<ServiceProvider> CreateServiceProviderAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddMemoryCache();
            services.AddSingleton(connection);
            services.AddSingleton<ITenantExecutionContextAccessor, TenantExecutionContextAccessor>();
            services.AddScoped<ITenantProvider, TenantProvider>();
            services.AddSingleton<ITenantCommercialAccessCache, TenantCommercialAccessCache>();
            services.Configure<IdentityOptions>(_ => { });
            services.Configure<TilopayRepeatOptions>(_ => { });
            services.Configure<OpcionesOnboardingTenant>(options =>
            {
                options.RegistrationRole = "Administrador";
                options.AddRegisteredRole = true;
                options.RegisteredRole = "Registrado";
                options.CreateInitialSubscription = false;
            });
            services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(serviceProvider.GetRequiredService<SqliteConnection>());
            });
            services
                .AddIdentity<AppUsuario, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
            services.AddSingleton<Services.BusinessTime.IBusinessDateTimeProvider>(_ => new FixedBusinessDateTimeProvider());
            services.AddScoped<SuscripcionService>();
            services.AddScoped<ITenantCommercialAccessResolver, TenantCommercialAccessResolver>();
            services.AddScoped<IPromotionalCodeService, PromotionalCodeService>();
            services.AddScoped<IContractService, ContractService>();
            services.AddScoped<TenantProvisioningService>();

            var provider = services.BuildServiceProvider();

            using (var setupScope = provider.CreateScope())
            {
                var dbContext = setupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
            }

            return provider;
        }
    }
}
