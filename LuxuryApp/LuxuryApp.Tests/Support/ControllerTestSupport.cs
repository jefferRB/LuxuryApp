using System.Security.Claims;
using LuxuryApp.Services;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Informacion;
using LuxuryApp.Services.Productos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LuxuryApp.Tests.Support
{
    internal static class ControllerTestSupport
    {
        public static IBusinessDateTimeProvider BusinessDateTimeProvider { get; } =
            new FixedBusinessDateTimeProvider();

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

        public static ILiquidacionSemanalService CreateLiquidacionSemanalService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new LiquidacionSemanalService(
                context,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LiquidacionSemanalService>.Instance);

        public static ICobroService CreateCobroService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new CobroService(
                context,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CobroService>.Instance);

        public static ICobroQueryService CreateCobroQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new CobroQueryService(context, BusinessDateTimeProvider);

        public static IDashboardFinancieroQueryService CreateDashboardFinancieroQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new DashboardFinancieroQueryService(context, BusinessDateTimeProvider);

        public static IEgresoService CreateEgresoService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new EgresoService(
                context,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<EgresoService>.Instance);

        public static IEgresoQueryService CreateEgresoQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new EgresoQueryService(context, BusinessDateTimeProvider);

        public static IInformacionNegocioQueryService CreateInformacionNegocioQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new InformacionNegocioQueryService(context, BusinessDateTimeProvider);

        public static ICalendarCommandService CreateCalendarCommandService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            ICalendarNotificationService? notificationService = null) =>
            new CalendarCommandService(
                context,
                notificationService ?? new NoOpCalendarNotificationService(),
                new VisitasAutomaticasService(context, BusinessDateTimeProvider),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CalendarCommandService>.Instance);

        public static ICalendarQueryService CreateCalendarQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new CalendarQueryService(context, BusinessDateTimeProvider);

        public static IProductoService CreateProductoService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new ProductoService(
                context,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ProductoService>.Instance);

        public static IProductoQueryService CreateProductoQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new ProductoQueryService(context);
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

    internal sealed class NoOpCalendarNotificationService : ICalendarNotificationService
    {
        public Task<bool> TrySendConfirmationAsync(
            string telefonoCliente,
            string nombreCliente,
            DateTime fechaHoraCita,
            string servicioNombre,
            string funcionarioNombre,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
