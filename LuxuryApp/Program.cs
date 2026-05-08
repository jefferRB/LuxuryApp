using LuxuryApp.Datos;
using LuxuryApp.Emails;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Middleware;
using LuxuryApp.Services;
using LuxuryApp.Services.Calendar;
using LuxuryApp.Services.Contracts;
using LuxuryApp.Services.DataBase;
using LuxuryApp.Services.Finanzas;
using LuxuryApp.Services.Funcionarios;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Informacion;
using LuxuryApp.Services.Layout;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.PublicSite;
using LuxuryApp.Services.Productos;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
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

var builder = WebApplication.CreateBuilder(args);

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
});

builder.Services.AddScoped<TenantSessionSecurityValidator>();
builder.Services.AddScoped<LegacyUserStateRepairService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = new PathString("/Accounts/Acceso");
    options.AccessDeniedPath = new PathString("/Accounts/Bloqueado");
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
builder.Services.Configure<OpcionesOnboardingTenant>(builder.Configuration.GetSection("TenantOnboarding"));

builder.Services.AddSingleton<RecordatorioService>();
builder.Services.AddScoped<EmailSender>();
builder.Services.AddTransient<EmailService, EmailSender>();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = builder.Configuration["Email:resendAPIKey"] ?? string.Empty;
});
builder.Services.AddTransient<IResend, ResendClient>();

builder.Services.AddScoped<WhatsAppService>();
builder.Services.AddScoped<ICalendarNotificationService, CalendarNotificationService>();
builder.Services.AddScoped<ICalendarCommandService, CalendarCommandService>();
builder.Services.AddScoped<ICalendarQueryService, CalendarQueryService>();
builder.Services.AddScoped<ICobroService, CobroService>();
builder.Services.AddScoped<ICobroQueryService, CobroQueryService>();
builder.Services.AddScoped<IDashboardFinancieroQueryService, DashboardFinancieroQueryService>();
builder.Services.AddScoped<IEgresoService, EgresoService>();
builder.Services.AddScoped<IEgresoQueryService, EgresoQueryService>();
builder.Services.AddScoped<IInformacionNegocioQueryService, InformacionNegocioQueryService>();
builder.Services.AddScoped<ILiquidacionSemanalService, LiquidacionSemanalService>();
builder.Services.AddScoped<IProductoService, ProductoService>();
builder.Services.AddScoped<IProductoQueryService, ProductoQueryService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IPrivateNavigationService, PrivateNavigationService>();
builder.Services.AddScoped<IPublicSiteContentService, PublicSiteContentService>();
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
}
//linux nginx
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseStaticFiles();

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
