using System.Security.Claims;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Services.DataBase;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace LuxuryApp.Tests.Support
{
    internal static class ControllerTestSupport
    {
        public static DefaultHttpContext AttachHttpContext(Controller controller, ClaimsPrincipal? user = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.User = user ?? new ClaimsPrincipal(new ClaimsIdentity());

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            controller.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
            return httpContext;
        }

        public static ClaimsPrincipal BuildTenantPrincipal(
            string userId,
            Guid tenantId,
            bool isPlatformSuperAdmin = false)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new(CustomClaimTypes.UserId, userId),
                new(CustomClaimTypes.TenantId, tenantId.ToString())
            };

            if (isPlatformSuperAdmin)
            {
                claims.Add(new Claim(CustomClaimTypes.PlatformSuperAdmin, bool.TrueString));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
        }

        public static RecordatorioService CreateRecordatorioService()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ConexionSql"] = "Server=(localdb)\\MSSQLLocalDB;Database=LuxuryAppTests;Trusted_Connection=True;"
                })
                .Build();

            return new RecordatorioService(configuration);
        }

        public static ILiquidacionSemanalService CreateLiquidacionSemanalService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new LiquidacionSemanalService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiquidacionSemanalService>.Instance);
    }

    internal sealed class TestTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _values = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(_values);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _values = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    internal sealed class FakeEmailService : EmailService
    {
        public Task SendBulkEmailsAsync(List<ClientesModel> users, string subject, string template) =>
            Task.CompletedTask;
    }

    internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "LuxuryApp.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
