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
using LuxuryApp.Services.Payments;
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
    .AddDefaultTokenProviders();

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
        await SecurityStampValidator.ValidatePrincipalAsync(context);

        if (context.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<TenantSessionSecurityValidator>();
        var isValid = await validator.ValidateAsync(context.Principal, context.HttpContext.RequestAborted);

        if (!isValid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequiredLength = 5;
    options.Password.RequireUppercase = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);
    options.Lockout.MaxFailedAccessAttempts = 3;
});

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());

    var policy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(policy));
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

builder.Services.AddMemoryCache();

builder.Services.Configure<OpcionesPago>(builder.Configuration.GetSection("Payments"));
builder.Services.Configure<OpcionesTilopay>(builder.Configuration.GetSection("Tilopay"));
builder.Services.Configure<TilopayRepeatOptions>(builder.Configuration.GetSection("TilopayRepeat"));
builder.Services.Configure<OpcionesOnboardingTenant>(builder.Configuration.GetSection("TenantOnboarding"));
builder.Services.Configure<BusinessDateTimeOptions>(builder.Configuration.GetSection(BusinessDateTimeOptions.SectionName));
builder.Services.Configure<MetaWhatsAppOptions>(builder.Configuration.GetSection(MetaWhatsAppOptions.SectionName));
builder.Services.Configure<LuxuryApp.Services.Account.AccountEmailOptions>(
    builder.Configuration.GetSection(LuxuryApp.Services.Account.AccountEmailOptions.SectionName));
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
builder.Services.AddScoped<IEgresoService, EgresoService>();
builder.Services.AddScoped<IEgresoQueryService, EgresoQueryService>();
builder.Services.AddScoped<IInformacionNegocioQueryService, InformacionNegocioQueryService>();
builder.Services.AddScoped<ILiquidacionSemanalService, LiquidacionSemanalService>();
builder.Services.AddScoped<IFuncionarioPortalAccessService, FuncionarioPortalAccessService>();
builder.Services.AddScoped<IFuncionarioPortalQueryService, FuncionarioPortalQueryService>();
builder.Services.AddScoped<IFuncionarioPortalPermissionService, FuncionarioPortalPermissionService>();
builder.Services.AddScoped<IProductoService, ProductoService>();
builder.Services.AddScoped<IProductoQueryService, ProductoQueryService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IPrivateNavigationService, PrivateNavigationService>();
builder.Services.AddScoped<IPublicSiteContentService, PublicSiteContentService>();
// Reservas online por tenant (Fase 1)
builder.Services.AddScoped<IBookingAvailabilityService, BookingAvailabilityService>();
builder.Services.AddScoped<IBookingSettingsService, BookingSettingsService>();
builder.Services.AddScoped<IPublicBookingService, PublicBookingService>();
builder.Services.AddScoped<IBookingRequestService, BookingRequestService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddScoped<VisitasAutomaticasService>();
builder.Services.AddHostedService<VisitasBackgroundService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantExecutionContextAccessor, TenantExecutionContextAccessor>();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddSingleton<TenantExecutionService>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUsuario>, CustomClaimsPrincipalFactory>();

builder.Services.AddScoped<SuscripcionService>();
builder.Services.AddSingleton<ITenantCommercialAccessCache, TenantCommercialAccessCache>();
builder.Services.AddScoped<ITenantCommercialAccessResolver, TenantCommercialAccessResolver>();
builder.Services.AddScoped<IPromotionalCodeService, PromotionalCodeService>();
builder.Services.AddScoped<SaaSPaymentService>();
builder.Services.AddScoped<PaymentProviderResolver>();
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
//linux nginx
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

app.UseRouting();

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
