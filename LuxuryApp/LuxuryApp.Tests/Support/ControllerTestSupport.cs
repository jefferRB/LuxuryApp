using System.Security.Claims;
using LuxuryApp.Models.Comprobantes;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Services;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Comprobantes;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Informacion;
using LuxuryApp.Services.Productos;
using LuxuryApp.Services.Tenant;
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

        public static ILiquidacionSemanalService CreateLiquidacionSemanalService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            ITenantProvider tenantProvider) =>
            new LiquidacionSemanalService(
                context,
                BusinessDateTimeProvider,
                new LuxuryApp.Services.Fiscal.TaxCalculationService(),
                new LuxuryApp.Services.Fiscal.LiquidacionFuncionarioService(),
                new LuxuryApp.Services.Fiscal.TenantFiscalConfigService(context, tenantProvider),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LiquidacionSemanalService>.Instance);

        public static ICobroService CreateCobroService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new CobroService(
                context,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CobroService>.Instance);

        public static ICobroQueryService CreateCobroQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new CobroQueryService(context, BusinessDateTimeProvider);

        public static LuxuryApp.Services.Fiscal.ICobroFiscalPreviewService CreateCobroFiscalPreviewService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            ITenantProvider tenantProvider) =>
            new LuxuryApp.Services.Fiscal.CobroFiscalPreviewService(
                context,
                new LuxuryApp.Services.Fiscal.TenantFiscalConfigService(context, tenantProvider),
                new LuxuryApp.Services.Fiscal.TaxCalculationService());

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
            ICalendarWhatsAppNotificationService? notificationService = null) =>
            new CalendarCommandService(
                context,
                notificationService ?? new NoOpCalendarWhatsAppNotificationService(),
                new VisitasAutomaticasService(context, BusinessDateTimeProvider),
                CreateAvailabilityService(context),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CalendarCommandService>.Instance);

        /// <summary>
        /// Fuente única de disponibilidad (citas + bloqueos recurrentes). La usan el calendario y
        /// las reservas públicas: en tests se construye igual que en producción.
        /// </summary>
        public static LuxuryApp.Services.Horarios.IFuncionarioAvailabilityService CreateAvailabilityService(
            ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new LuxuryApp.Services.Horarios.FuncionarioAvailabilityService(context);

        public static LuxuryApp.Services.Reservas.IBookingAvailabilityService CreateBookingAvailabilityService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            LuxuryApp.Services.BusinessTime.IBusinessDateTimeProvider? clock = null,
            LuxuryApp.Services.Reservas.IBookingCatalogService? catalog = null) =>
            new LuxuryApp.Services.Reservas.BookingAvailabilityService(
                context,
                clock ?? BusinessDateTimeProvider,
                catalog ?? new LuxuryApp.Services.Reservas.BookingCatalogService(context),
                CreateAvailabilityService(context));

        public static ICalendarQueryService CreateCalendarQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new CalendarQueryService(context, BusinessDateTimeProvider);

        public static IProductoService CreateProductoService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new ProductoService(
                context,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ProductoService>.Instance);

        public static IProductoQueryService CreateProductoQueryService(ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new ProductoQueryService(context);

        public static ITenantDisplayNameService CreateTenantDisplayNameService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            ITenantProvider tenantProvider) =>
            new TenantDisplayNameService(context, tenantProvider, new HttpContextAccessor());

        // No-arg overload para tests que no necesitan nombre de tenant real.
        public static ITenantDisplayNameService CreateTenantDisplayNameService() =>
            new NoOpTenantDisplayNameService();

        // Almacenamiento de fotos que no toca disco (los tests no ejercitan fotos).
        public static IFuncionarioPhotoStorageService CreateFuncionarioPhotoStorageService() =>
            new NoOpFuncionarioPhotoStorageService();

        // Overload con nombre fijo para tests que verifican que el nombre del negocio
        // aparece en encabezados/nombres de archivo de los reportes.
        public static ITenantDisplayNameService CreateTenantDisplayNameService(string displayName) =>
            new FixedTenantDisplayNameService(displayName);

        public static IControlCobrosQueryService CreateControlCobrosQueryService(
            ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new ControlCobrosQueryService(context, BusinessDateTimeProvider);

        public static IComprobanteCobroService CreateComprobanteCobroService() =>
            new NoOpComprobanteCobroService();

        public static IFuncionarioPortalAccessService CreateFuncionarioPortalAccessService() =>
            new NoOpFuncionarioPortalAccessService();

        public static IFuncionarioPortalPermissionService CreateFuncionarioPortalPermissionService() =>
            new NoOpFuncionarioPortalPermissionService();

        public static LuxuryApp.Services.Account.IAccountEmailService CreateAccountEmailService() =>
            new NoOpAccountEmailService();

        public static LuxuryApp.Services.Reports.IMonthlyReportRecipientResolver CreateMonthlyReportRecipientResolver(
            ProyectoIdentity.Datos.ApplicationDbContext context) =>
            new LuxuryApp.Services.Reports.MonthlyReportRecipientResolver(context);

        public static LuxuryApp.Services.Reports.IMonthlyBusinessReportService CreateMonthlyBusinessReportService(
            ProyectoIdentity.Datos.ApplicationDbContext context,
            ITenantProvider tenantProvider,
            LuxuryApp.Services.Reports.IMonthlyReportEmailSender emailSender,
            string businessName = "Negocio de Prueba",
            string? baseUrl = "https://app.luxurycloud.test") =>
            new LuxuryApp.Services.Reports.MonthlyBusinessReportService(
                context,
                tenantProvider,
                CreateDashboardFinancieroQueryService(context),
                CreateInformacionNegocioQueryService(context),
                CreateTenantDisplayNameService(businessName),
                CreateMonthlyReportRecipientResolver(context),
                new LuxuryApp.Services.Reports.MonthlyReportEmailRenderer(),
                emailSender,
                BusinessDateTimeProvider,
                Microsoft.Extensions.Options.Options.Create(new LuxuryApp.Services.Common.PublicSiteOptions
                {
                    PublicBaseUrl = baseUrl ?? string.Empty
                }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LuxuryApp.Services.Reports.MonthlyBusinessReportService>.Instance);
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

    internal sealed class NoOpTenantDisplayNameService : ITenantDisplayNameService
    {
        public Task<string> GetCurrentTenantDisplayNameAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string> GetTenantDisplayNameAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task<string?> GetPublicTenantDisplayNameBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public string NormalizeDisplayName(string? value) => value ?? string.Empty;

        public bool ContainsInvalidDisplayNameCharacters(string? value) => false;
    }

    internal sealed class FixedTenantDisplayNameService : ITenantDisplayNameService
    {
        private readonly string _displayName;

        public FixedTenantDisplayNameService(string displayName) => _displayName = displayName;

        public Task<string> GetCurrentTenantDisplayNameAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_displayName);

        public Task<string> GetTenantDisplayNameAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_displayName);

        public Task<string?> GetPublicTenantDisplayNameBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_displayName);

        public string NormalizeDisplayName(string? value) => value ?? string.Empty;

        public bool ContainsInvalidDisplayNameCharacters(string? value) => false;
    }

    internal sealed class NoOpComprobanteCobroService : IComprobanteCobroService
    {
        public Task<ComprobanteCobro?> CrearYEnviarDesdeCobroAsync(int cobroId, string emailDestino, bool guardarEmailEnCliente, string? createdByUserId, int? funcionarioScopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComprobanteCobro?>(null);

        public Task<ComprobanteCobro?> ReenviarAsync(int comprobanteId, int? funcionarioScopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComprobanteCobro?>(null);

        public Task<ComprobanteCobro?> ObtenerParaAppAsync(int comprobanteId, int? funcionarioScopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComprobanteCobro?>(null);

        public Task<ComprobanteCobro?> ObtenerPorTokenPublicoAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComprobanteCobro?>(null);

        public byte[] GenerarPdf(ComprobanteCobro comprobante) => Array.Empty<byte>();
    }

    internal sealed class NoOpFuncionarioPortalAccessService : IFuncionarioPortalAccessService
    {
        public Task<FuncionarioAccesoViewModel> ObtenerEstadoAsync(int funcionarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FuncionarioAccesoViewModel { FuncionarioId = funcionarioId });

        public Task<FuncionarioAccesoResultado> ActivarAccesoAsync(int funcionarioId, string email, FuncionarioAccesoCredencialModo modo, string? contrasenaTemporal, CancellationToken cancellationToken = default) =>
            Task.FromResult(FuncionarioAccesoResultado.Falla("NoOp"));

        public Task<FuncionarioAccesoResultado> DesactivarAccesoAsync(int funcionarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FuncionarioAccesoResultado.Falla("NoOp"));

        public Task<FuncionarioAccesoResultado> ReactivarAccesoAsync(int funcionarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FuncionarioAccesoResultado.Falla("NoOp"));

        public Task<FuncionarioAccesoResultado> GenerarEnlaceInvitacionAsync(int funcionarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FuncionarioAccesoResultado.Falla("NoOp"));

        public Task<FuncionarioAccesoResultado> CambiarCorreoAsync(int funcionarioId, string nuevoEmail, CancellationToken cancellationToken = default) =>
            Task.FromResult(FuncionarioAccesoResultado.Falla("NoOp"));
    }

    internal sealed class NoOpFuncionarioPortalPermissionService : IFuncionarioPortalPermissionService
    {
        public Task<FuncionarioPortalPermisosSet> ObtenerAsync(int funcionarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FuncionarioPortalPermisosSet(new Dictionary<string, bool>()));

        public Task<bool> TienePermisoAsync(int funcionarioId, string permiso, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task CrearDefaultsAsync(int funcionarioId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> GuardarAsync(int funcionarioId, IReadOnlyDictionary<string, bool> valores, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    internal sealed class NoOpAccountEmailService : LuxuryApp.Services.Account.IAccountEmailService
    {
        public Task SendPasswordResetEmailAsync(string toEmail, string displayName, string resetLink, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendEmailConfirmationEmailAsync(string toEmail, string displayName, string confirmationLink, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendFuncionarioInvitationEmailAsync(string toEmail, string displayName, string setPasswordLink, string businessName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class NoOpFuncionarioPhotoStorageService : IFuncionarioPhotoStorageService
    {
        public Task<FuncionarioPhotoSaveResult> SaveAsync(
            Guid tenantId,
            Microsoft.AspNetCore.Http.IFormFile file,
            string? previousStoragePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FuncionarioPhotoSaveResult.Ok("/uploads/test.jpg", "uploads/test.jpg"));

        public void Delete(string? storagePath) { }
    }

    internal sealed class NoOpCalendarWhatsAppNotificationService : ICalendarWhatsAppNotificationService
    {
        public Task SendAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LuxuryApp.Services.Calendar.WhatsAppConfirmationSendResult> SendConfirmationNowAsync(int citaId, string source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LuxuryApp.Services.Calendar.WhatsAppConfirmationSendResult(
                LuxuryApp.Services.Calendar.WhatsAppConfirmationOutcome.Sent, "Confirmación de WhatsApp enviada."));

        public Task QueueAppointmentConfirmationAsync(int citaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task QueueAppointmentReminderAsync(int citaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task QueueImmediateReminderOnCreateAsync(int citaId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ProcessInboundReplyAsync(System.Text.Json.JsonElement payload, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ProcessStatusUpdateAsync(System.Text.Json.JsonElement payload, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ScheduleDueRemindersAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task GenerateDailyBatchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RescheduleConfirmationIfPendingAsync(int citaId, DateTime newFechaHoraCita, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CancelPendingNotificationsAsync(int citaId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
