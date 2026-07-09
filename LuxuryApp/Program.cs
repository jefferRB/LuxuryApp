using LuxuryApp.Datos;
using LuxuryApp.Emails;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Middleware;
using LuxuryApp.Services;
using LuxuryApp.Services.BusinessTime;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Comprobantes;
using LuxuryApp.Services.Contracts;
using LuxuryApp.Services.DataBase;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Informacion;
using LuxuryApp.Services.Layout;
using LuxuryApp.Services.Localization;
using LuxuryApp.Services.Notifications;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.PublicPages;
using LuxuryApp.Services.PublicImages;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.Productos;
using LuxuryApp.Services.Reservas;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
using LuxuryApp.Services.WhatsApp;
using LuxuryApp.Workers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;
using Resend;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var globalizationInvariant) && globalizationInvariant)
{
    throw new InvalidOperationException("La globalizacion de .NET no puede estar en modo invariant para Luxury.");
}

var defaultCulture = CultureInfo.GetCultureInfo("es-CR");
CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

// QuestPDF (generación de PDF del comprobante interno). Licencia Community:
// gratuita para empresas con ingresos anuales < USD 1M.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

//Builders para host en linux nginx 
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/luxury/dataprotection-keys"))
    .SetApplicationName("Luxury");
//Fin builders para host en linux nginx

builder.Services.AddScoped<TenantSessionConnectionInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("ConexionSql"));
    options.AddInterceptors(serviceProvider.GetRequiredService<TenantSessionConnectionInterceptor>());
});

builder.Services
    .AddIdentity<AppUsuario, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    .AddPasswordValidator<SuperAdminPasswordValidator>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        PlatformAuthorizationPolicies.PlatformSuperAdmin,
        policy => policy.RequireClaim(CustomClaimTypes.PlatformSuperAdmin, bool.TrueString));

    options.AddPolicy(
        AppAuthorizationPolicies.RequireTenantAdmin,
        policy => policy.RequireRole(AppRoles.Administrador));

    options.AddPolicy(
        AppAuthorizationPolicies.RequireFuncionario,
        policy => policy.RequireRole(AppRoles.Funcionario));
});

builder.Services.AddScoped<TenantSessionSecurityValidator>();
builder.Services.AddScoped<LegacyUserStateRepairService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = new PathString("/Accounts/Acceso");
    options.AccessDeniedPath = new PathString("/Accounts/Bloqueado");
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = async context =>
    {
        // El validator debe correr ANTES que el security stamp validator: con
        // ValidationInterval = Zero el stamp validator regenera el principal desde la BD
        // en cada request, y los checks de claims obsoletos (tenant desalineado,
        // platform_super_admin revocado) nunca verían la cookie original.
        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<TenantSessionSecurityValidator>();
            var isValid = await validator.ValidateAsync(context.Principal, context.HttpContext.RequestAborted);

            if (!isValid)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
    };
});

builder.Services.Configure<IdentityOptions>(options =>
{
    // Mínimo global 8; las cuentas de plataforma exigen 12 vía SuperAdminPasswordValidator.
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

builder.Services.Configure<LuxuryApp.Services.Identity.PlatformSecurityOptions>(
    builder.Configuration.GetSection(LuxuryApp.Services.Identity.PlatformSecurityOptions.SectionName));

builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());

    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));

    // Enrolamiento obligatorio de TOTP para superadmins (S1). Inerte mientras
    // Security:Mfa:SuperAdminEnforcement sea false.
    options.Filters.Add<LuxuryApp.Filters.RequireMfaEnrollmentFilter>();
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

builder.Services.AddMemoryCache();

// Rate limiting acotado al enlace público de reservas (/reservar/*). Frena floods y spam
// por IP sin tocar el resto del pipeline: solo el controlador público opta con
// [EnableRateLimiting("PublicBooking")]. Detrás de nginx, UseForwardedHeaders ya deja el
// IP real del cliente en RemoteIpAddress, así que la partición por IP es correcta.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PublicBooking", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

builder.Services.Configure<OpcionesPago>(builder.Configuration.GetSection("Payments"));
builder.Services.Configure<OpcionesTilopay>(builder.Configuration.GetSection("Tilopay"));
builder.Services.Configure<TilopayRepeatOptions>(builder.Configuration.GetSection("TilopayRepeat"));
builder.Services.Configure<OpcionesOnboardingTenant>(builder.Configuration.GetSection("TenantOnboarding"));
builder.Services.Configure<BusinessDateTimeOptions>(builder.Configuration.GetSection(BusinessDateTimeOptions.SectionName));
builder.Services.Configure<MetaWhatsAppOptions>(builder.Configuration.GetSection(MetaWhatsAppOptions.SectionName));
builder.Services.Configure<LuxuryApp.Services.Account.AccountEmailOptions>(
    builder.Configuration.GetSection(LuxuryApp.Services.Account.AccountEmailOptions.SectionName));
// URL pública oficial (clave raíz "PublicBaseUrl" / env var PublicBaseUrl). Centraliza los
// enlaces absolutos de correos/procesos de fondo; nunca usa el host del request ni ngrok.
builder.Services.Configure<LuxuryApp.Services.Common.PublicSiteOptions>(builder.Configuration);
builder.Services.AddOptions<PublicImageOptions>()
    .Bind(builder.Configuration.GetSection(PublicImageOptions.SectionName))
    .Validate(options =>
        string.Equals(options.Provider, PublicImageProviders.Local, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(options.Provider, PublicImageProviders.S3Compatible, StringComparison.OrdinalIgnoreCase),
        "PublicImages:Provider debe ser Local o S3Compatible.")
    .ValidateOnStart();
builder.Services.AddOptions<S3StorageOptions>()
    .Bind(builder.Configuration.GetSection(S3StorageOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<S3StorageOptions>, S3StorageOptionsValidator>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { defaultCulture };

    options.DefaultRequestCulture = new RequestCulture(defaultCulture);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

builder.Services.AddSingleton<RecordatorioService>();
builder.Services.AddSingleton<IBusinessDateTimeProvider, BusinessDateTimeProvider>();
builder.Services.AddScoped<EmailSender>();
builder.Services.AddTransient<EmailService, EmailSender>();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = builder.Configuration["Email:SmtpPassword"] ?? string.Empty;
});
builder.Services.AddTransient<IResend, ResendClient>();
builder.Services.AddScoped<LuxuryApp.Services.Account.IAccountEmailService,
    LuxuryApp.Services.Account.AccountEmailService>();

builder.Services.AddHttpClient<IMetaWhatsAppClient, MetaWhatsAppClient>((serviceProvider, client) =>
{
    var options = MetaWhatsAppNormalizedOptions.Create(
        serviceProvider.GetRequiredService<IOptionsMonitor<MetaWhatsAppOptions>>().CurrentValue);
    if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
    {
        client.BaseAddress = baseUri;
    }

    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.RequestTimeoutSeconds));
});
builder.Services.AddHostedService<MetaWhatsAppOptionsLoggingService>();
builder.Services.AddScoped<ICalendarWhatsAppNotificationService, CalendarWhatsAppNotificationService>();
builder.Services.AddScoped<ITenantWhatsAppSettingsService, TenantWhatsAppSettingsService>();
builder.Services.AddScoped<ITenantWhatsAppFeatureService, TenantWhatsAppFeatureService>();
builder.Services.AddScoped<IWhatsAppInboxService, WhatsAppInboxService>();
builder.Services.AddScoped<ICalendarCommandService, CalendarCommandService>();
builder.Services.AddScoped<ICalendarQueryService, CalendarQueryService>();
builder.Services.AddScoped<IControlCobrosQueryService, ControlCobrosQueryService>();
builder.Services.AddScoped<ICobroService, CobroService>();
builder.Services.AddScoped<ICobroQueryService, CobroQueryService>();
// Comprobante digital interno (no fiscal)
builder.Services.AddScoped<IComprobantePdfService, ComprobantePdfService>();
builder.Services.AddSingleton<IComprobanteHtmlRenderer, ComprobanteHtmlRenderer>();
builder.Services.AddScoped<IComprobanteEmailService, ComprobanteEmailService>();
builder.Services.AddScoped<IComprobanteCobroService, ComprobanteCobroService>();
builder.Services.AddScoped<IDashboardFinancieroQueryService, DashboardFinancieroQueryService>();
// Resumen Ejecutivo Mensual (LuxuryCloud Insights)
builder.Services.Configure<LuxuryApp.Services.Reports.MonthlyReportSchedulerOptions>(
    builder.Configuration.GetSection(LuxuryApp.Services.Reports.MonthlyReportSchedulerOptions.SectionName));
builder.Services.AddSingleton<LuxuryApp.Services.Reports.IMonthlyReportEmailRenderer, LuxuryApp.Services.Reports.MonthlyReportEmailRenderer>();
builder.Services.AddScoped<LuxuryApp.Services.Reports.IMonthlyReportEmailSender, LuxuryApp.Services.Reports.MonthlyReportEmailSender>();
builder.Services.AddScoped<LuxuryApp.Services.Reports.IMonthlyReportRecipientResolver, LuxuryApp.Services.Reports.MonthlyReportRecipientResolver>();
builder.Services.AddScoped<LuxuryApp.Services.Reports.IMonthlyBusinessReportService, LuxuryApp.Services.Reports.MonthlyBusinessReportService>();
builder.Services.AddScoped<LuxuryApp.Services.Reports.IMonthlyReportScheduler, LuxuryApp.Services.Reports.MonthlyReportScheduler>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformMonthlyReportService, LuxuryApp.Services.Platform.PlatformMonthlyReportService>();
builder.Services.AddScoped<IEgresoService, EgresoService>();
builder.Services.AddScoped<IEgresoQueryService, EgresoQueryService>();
builder.Services.AddScoped<IInformacionNegocioQueryService, InformacionNegocioQueryService>();
builder.Services.AddSingleton<LuxuryApp.Services.Fiscal.ITaxCalculationService, LuxuryApp.Services.Fiscal.TaxCalculationService>();
builder.Services.AddSingleton<LuxuryApp.Services.Fiscal.ILiquidacionFuncionarioService, LuxuryApp.Services.Fiscal.LiquidacionFuncionarioService>();
builder.Services.AddScoped<LuxuryApp.Services.Fiscal.ITenantFiscalConfigService, LuxuryApp.Services.Fiscal.TenantFiscalConfigService>();
builder.Services.AddScoped<LuxuryApp.Services.Fiscal.ICobroFiscalPreviewService, LuxuryApp.Services.Fiscal.CobroFiscalPreviewService>();
builder.Services.AddScoped<ILiquidacionSemanalService, LiquidacionSemanalService>();
builder.Services.AddScoped<IFuncionarioPortalAccessService, FuncionarioPortalAccessService>();
builder.Services.AddScoped<IFuncionarioPortalQueryService, FuncionarioPortalQueryService>();
builder.Services.AddScoped<IFuncionarioPortalPermissionService, FuncionarioPortalPermissionService>();
builder.Services.AddScoped<IProductoService, ProductoService>();
builder.Services.AddScoped<IProductoQueryService, ProductoQueryService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IPrivateNavigationService, PrivateNavigationService>();
builder.Services.AddScoped<IPublicSiteContentService, PublicSiteContentService>();
builder.Services.AddScoped<ITenantPublicPageQueryService, TenantPublicPageQueryService>();
builder.Services.AddScoped<ITenantPublicPageSettingsService, TenantPublicPageSettingsService>();
builder.Services.AddScoped<ITenantPublicPageAnalyticsService, TenantPublicPageAnalyticsService>();
builder.Services.AddScoped<ITenantPublicPageRedirectService, TenantPublicPageRedirectService>();
builder.Services.AddScoped<IPublicUrlValidationService, PublicUrlValidationService>();
builder.Services.AddScoped<IPublicAssetQuotaService, PublicAssetQuotaService>();
builder.Services.AddScoped<IPublicImageUploadService, PublicImageUploadService>();
builder.Services.AddScoped<IUploadedFileSecurityScanner, NoOpUploadedFileSecurityScanner>();
builder.Services.AddScoped<LocalPublicImageStorageService>();
builder.Services.AddScoped<S3CompatiblePublicImageStorageService>();
builder.Services.AddScoped<IPublicImageStorageService>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<PublicImageOptions>>().Value;
    return string.Equals(options.Provider, PublicImageProviders.S3Compatible, StringComparison.OrdinalIgnoreCase)
        ? serviceProvider.GetRequiredService<S3CompatiblePublicImageStorageService>()
        : serviceProvider.GetRequiredService<LocalPublicImageStorageService>();
});
builder.Services.AddScoped<ITenantDisplayNameService, TenantDisplayNameService>();
// Reservas online por tenant (Fase 1)
builder.Services.AddScoped<IBookingCatalogService, BookingCatalogService>();
builder.Services.AddScoped<IBookingAvailabilityService, BookingAvailabilityService>();
builder.Services.AddScoped<IBookingSettingsService, BookingSettingsService>();
builder.Services.AddScoped<IPublicBookingService, PublicBookingService>();
builder.Services.AddScoped<IBookingRequestService, BookingRequestService>();
builder.Services.AddScoped<IFuncionarioPhotoStorageService, FuncionarioPhotoStorageService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddScoped<VisitasAutomaticasService>();
builder.Services.AddHostedService<VisitasBackgroundService>();
// Envío automático del Resumen Ejecutivo Mensual. Inerte hasta MonthlyReports:SchedulerEnabled=true.
builder.Services.AddHostedService<LuxuryApp.Workers.MonthlyReportSchedulerService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantExecutionContextAccessor, TenantExecutionContextAccessor>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddSingleton<TenantExecutionService>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUsuario>, CustomClaimsPrincipalFactory>();

builder.Services.AddScoped<SuscripcionService>();
builder.Services.AddScoped<LuxuryApp.Services.SaaS.ISubscriptionSummaryService, LuxuryApp.Services.SaaS.SubscriptionSummaryService>();
builder.Services.AddSingleton<LuxuryApp.Services.SaaS.ISubscriptionPricingCatalog, LuxuryApp.Services.SaaS.SubscriptionPricingCatalog>();
builder.Services.AddScoped<LuxuryApp.Services.SaaS.IPlanChangeService, LuxuryApp.Services.SaaS.PlanChangeService>();
builder.Services.AddSingleton<ITenantCommercialAccessCache, TenantCommercialAccessCache>();
builder.Services.AddScoped<ITenantCommercialAccessResolver, TenantCommercialAccessResolver>();
builder.Services.AddScoped<IPromotionalCodeService, PromotionalCodeService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformAuditService, LuxuryApp.Services.Platform.PlatformAuditService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformUserAdminService, LuxuryApp.Services.Platform.PlatformUserAdminService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformMetricsService, LuxuryApp.Services.Platform.PlatformMetricsService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformHealthService, LuxuryApp.Services.Platform.PlatformHealthService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformWhatsAppStatusService, LuxuryApp.Services.Platform.PlatformWhatsAppStatusService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformTenantProfileService, LuxuryApp.Services.Platform.PlatformTenantProfileService>();
// Mission Control: heartbeat singleton (crea su propio scope EF) + snapshot cacheado de señales/colas.
builder.Services.AddSingleton<LuxuryApp.Services.Platform.IWorkerHeartbeatService, LuxuryApp.Services.Platform.WorkerHeartbeatService>();
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformMissionControlService, LuxuryApp.Services.Platform.PlatformMissionControlService>();
// Snapshot comercial mensual (AD-4): historia de MRR/churn/trials que no se puede
// reconstruir retroactivamente. El worker queda inerte con Platform:CommercialSnapshot:Enabled=false.
builder.Services.Configure<LuxuryApp.Models.Platform.PlatformCommercialSnapshotOptions>(
    builder.Configuration.GetSection(LuxuryApp.Models.Platform.PlatformCommercialSnapshotOptions.SectionName));
builder.Services.AddScoped<LuxuryApp.Services.Platform.IPlatformCommercialSnapshotService, LuxuryApp.Services.Platform.PlatformCommercialSnapshotService>();
builder.Services.AddHostedService<LuxuryApp.Workers.CommercialSnapshotWorker>();

builder.Services.AddScoped<SaaSPaymentService>();
builder.Services.AddScoped<PaymentProviderResolver>();

// Reconciliación automática y diagnóstico del módulo Billing (red de seguridad diaria).
// El worker queda inerte con BillingReconciliation:Enabled=false.
builder.Services.Configure<LuxuryApp.Services.Billing.BillingReconciliationOptions>(
    builder.Configuration.GetSection("BillingReconciliation"));
builder.Services.AddScoped<LuxuryApp.Services.Billing.IBillingReconciliationService, LuxuryApp.Services.Billing.BillingReconciliationService>();
builder.Services.AddScoped<LuxuryApp.Services.Billing.IBillingHealthService, LuxuryApp.Services.Billing.BillingHealthService>();
builder.Services.AddHostedService<LuxuryApp.Workers.BillingReconciliationWorker>();

// Cliente admin de TiloPay Repeat: resuelve id_suscriptor y gestiona el suscriptor del proveedor.
// Deshabilitado por defecto (TilopayRepeatAdmin:Enabled=false): el flujo de compra actual no cambia.
builder.Services.Configure<OpcionesTilopayRepeatAdmin>(builder.Configuration.GetSection("TilopayRepeatAdmin"));
builder.Services.AddHttpClient<LuxuryApp.Services.Tilopay.ITilopayRepeatAdminService, LuxuryApp.Services.Tilopay.TilopayRepeatAdminService>(
    (serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<OpcionesTilopay>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));
    });
builder.Services.AddScoped<LuxuryApp.Services.Billing.ISubscriberResolutionService, LuxuryApp.Services.Billing.SubscriberResolutionService>();
builder.Services.AddScoped<LuxuryApp.Services.Billing.IProviderSubscriptionManager, LuxuryApp.Services.Billing.ProviderSubscriptionManager>();
builder.Services.AddHttpClient<PublicCallbackHealthService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient<TilopayService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpcionesTilopay>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));
});
builder.Services.AddScoped<IPaymentProvider>(serviceProvider => serviceProvider.GetRequiredService<TilopayService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });

        await next();
    });
}
// Debe ir lo más arriba posible para envolver autenticación, routing, controladores y EF Core.
// Traduce las cancelaciones del cliente (cambio rápido de módulo, cerrar pestaña, refrescar,
// doble click) en un 499 silencioso y evita que lleguen al UseExceptionHandler como error 500.
app.UseMiddleware<ClientDisconnectMiddleware>();

//linux nginx
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<ContractAcceptanceMiddleware>();
app.UseMiddleware<SuscripcionMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await IdentitySeeder.SeedRolesAsync(services);
    await IdentitySeeder.SeedPlatformAccessAsync(services);
    await services.GetRequiredService<LegacyUserStateRepairService>().RepairAsync();
}

app.Run();
