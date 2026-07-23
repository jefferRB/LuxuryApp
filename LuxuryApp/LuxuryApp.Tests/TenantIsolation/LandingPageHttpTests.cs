using System.Net;
using System.Text.RegularExpressions;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.Layout;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Layout;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProyectoIdentity.Datos;
using System.Security.Claims;

namespace LuxuryApp.Tests.TenantIsolation
{
    /// <summary>
    /// Renderiza la landing pública REAL (HomeController.Index + vista Home/Index.cshtml +
    /// PublicSiteContentService real) por HTTP. Verifica que la página responde 200, tiene un
    /// único H1, deriva el precio inicial de los planes públicos (no hardcodeado), degrada bien
    /// sin planes, usa rutas reales y no renderiza un reproductor de video roto.
    /// </summary>
    public sealed class LandingPageHttpTests
    {
        [Fact]
        public async Task Get_Landing_WithPlans_Returns200_HasSingleH1_AndDynamicStartingPrice()
        {
            await using var harness = await LandingHarness.CreateAsync(seedPlans: true);

            var response = await harness.Client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(1, CountOccurrences(html, "<h1"));
            Assert.Contains("bajo control", html); // título del hero (H1)
            Assert.Contains("Desde", html);
            Assert.Contains("8.000", html); // precio del plan más económico sembrado (₡8.000)
        }

        [Fact]
        public async Task Get_Landing_WithoutPlans_Returns200_ShowsNeutralMessage_AndNoHardcodedPrice()
        {
            await using var harness = await LandingHarness.CreateAsync(seedPlans: false);

            var response = await harness.Client.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(1, CountOccurrences(html, "<h1"));
            Assert.Contains("temporalmente no disponibles", html);
            // Sin planes no debe aparecer un "Desde ₡8.000" hardcodeado.
            Assert.DoesNotContain("8.000", html);
        }

        [Fact]
        public async Task Get_Landing_UsesRealRoutesForKeyCtas()
        {
            await using var harness = await LandingHarness.CreateAsync(seedPlans: true);

            var html = await (await harness.Client.GetAsync("/")).Content.ReadAsStringAsync();

            Assert.Contains("/Accounts/Registro", html);
            Assert.Contains("/Accounts/Acceso", html);
            Assert.Contains("/Billing/Planes", html);
        }

        [Fact]
        public async Task Get_Landing_DoesNotRenderVideoPlayer_WhenNoVideoConfigured()
        {
            await using var harness = await LandingHarness.CreateAsync(seedPlans: true);

            var html = await (await harness.Client.GetAsync("/")).Content.ReadAsStringAsync();

            Assert.DoesNotContain("<video", html);
            Assert.DoesNotContain("<iframe", html);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private sealed class LandingHarness : IAsyncDisposable
        {
            private readonly IHost _host;
            private readonly SqliteConnection _connection;

            private LandingHarness(IHost host, SqliteConnection connection, HttpClient client)
            {
                _host = host;
                _connection = connection;
                Client = client;
            }

            public HttpClient Client { get; }

            public static async Task<LandingHarness> CreateAsync(bool seedPlans)
            {
                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();

                var host = await new HostBuilder()
                    .ConfigureWebHost(webHost =>
                    {
                        webHost.UseTestServer();
                        webHost.UseSetting(WebHostDefaults.ApplicationKey, "LuxuryApp");
                        webHost.ConfigureServices(services => ConfigureServices(services, connection));
                        webHost.Configure(Configure);
                    })
                    .StartAsync();

                using (var scope = host.Services.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await context.Database.EnsureCreatedAsync();

                    if (seedPlans)
                    {
                        context.Planes.AddRange(
                            NewPlan(PlanCodes.Basic, "Básico", 8000m, maxFuncionarios: 1),
                            NewPlan(PlanCodes.Pro, "Pro", 20000m, maxFuncionarios: 3),
                            NewPlan(PlanCodes.Business, "Empresarial", 35000m, maxFuncionarios: 7));
                        await context.SaveChangesAsync();
                    }
                }

                var server = host.GetTestServer();
                var client = server.CreateClient();
                client.BaseAddress = new Uri("http://localhost/");

                return new LandingHarness(host, connection, client);
            }

            private static Plan NewPlan(string code, string name, decimal price, int maxFuncionarios) =>
                new()
                {
                    Id = Guid.NewGuid(),
                    Codigo = code,
                    Nombre = name,
                    PrecioMensual = price,
                    Moneda = "CRC",
                    MaxFuncionarios = maxFuncionarios,
                    Activo = true
                };

            private static void ConfigureServices(IServiceCollection services, SqliteConnection connection)
            {
                services.AddSingleton(connection);
                services.AddSingleton<ITenantProvider>(new TestTenantProvider());
                services.AddDbContext<ApplicationDbContext>((sp, options) =>
                    options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));

                services.AddHttpContextAccessor();
                services.AddMemoryCache();
                services.Configure<OpcionesPago>(options => options.EnableValidationPlans = false);
                services.Configure<OpcionesTilopay>(_ => { });
                services.Configure<TilopayRepeatOptions>(_ => { });

                services
                    .AddIdentity<AppUsuario, IdentityRole>()
                    .AddEntityFrameworkStores<ApplicationDbContext>()
                    .AddDefaultTokenProviders();

                services.AddScoped<IPublicSiteContentService, PublicSiteContentService>();
                services.AddScoped<IPrivateNavigationService, EmptyPrivateNavigationService>();

                var mvc = services.AddControllersWithViews(options =>
                {
                    var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                    options.Filters.Add(new AuthorizeFilter(policy));
                });

                var appAssembly = typeof(LuxuryApp.Controllers.HomeController).Assembly;
                mvc.ConfigureApplicationPartManager(apm =>
                {
                    if (apm.ApplicationParts.All(p => p.Name != appAssembly.GetName().Name))
                    {
                        apm.ApplicationParts.Add(new AssemblyPart(appAssembly));
                    }

                    apm.ApplicationParts.Add(new CompiledRazorAssemblyPart(appAssembly));
                });
            }

            private static void Configure(IApplicationBuilder app)
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                });
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await _host.StopAsync();
                _host.Dispose();
                await _connection.DisposeAsync();
            }
        }

        private sealed class EmptyPrivateNavigationService : IPrivateNavigationService
        {
            public Task<PrivateNavigationViewModel> BuildAsync(ClaimsPrincipal principal, CancellationToken ct = default) =>
                Task.FromResult(new PrivateNavigationViewModel());
        }
    }
}
