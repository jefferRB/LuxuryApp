using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using LuxuryApp.Controllers;
using LuxuryApp.Controllers.DataBase;
using LuxuryApp.Controllers.Finanzas;
using LuxuryApp.Controllers.Funcionarios;
using LuxuryApp.Models.DataBase;
using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.Funcionarios;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Payments;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Services.Tilopay;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;

var repoRoot = @"C:\Users\jefferson\source\repos\LuxuryApp\LuxuryApp";
var userSecretsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Microsoft",
    "UserSecrets",
    "LuxuryApp-a2d6e79d-4b16-49d7-9d15-5c1ea4dfe9ee",
    "secrets.json");

var configuration = new ConfigurationBuilder()
    .SetBasePath(repoRoot)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddJsonFile(userSecretsPath, optional: true)
    .AddEnvironmentVariables()
    .Build();

var tilopayOptions = configuration.GetSection("Tilopay").Get<OpcionesTilopay>() ?? new OpcionesTilopay();
var tenantProvider = new StaticTenantProvider();
var services = BuildServices(configuration, tenantProvider);

if (args.Length >= 2 &&
    string.Equals(args[0], "replay-tilopay-webhook", StringComparison.OrdinalIgnoreCase))
{
    await ReplayTilopayWebhookAsync(services, tenantProvider, args[1]);
    return;
}

var validation = await EnsureValidationAsync(services, tenantProvider);

Print("Config", "PASS",
    $"ApiUser={(string.IsNullOrWhiteSpace(tilopayOptions.ApiUser) ? "<empty>" : "***set***")}, " +
    $"ApiPassword={(string.IsNullOrWhiteSpace(tilopayOptions.ApiPassword) ? "<empty>" : "***set***")}, " +
    $"ApiKey={(string.IsNullOrWhiteSpace(tilopayOptions.ApiKey) ? "<empty>" : "***set***")}, " +
    $"MerchantId={(string.IsNullOrWhiteSpace(tilopayOptions.MerchantId) ? "<empty>" : "***set***")}, " +
    $"WebhookToken={(string.IsNullOrWhiteSpace(tilopayOptions.WebhookAccessToken) ? "<empty>" : "***set***")}");

await RunSqlSessionContextPoolingAsync(configuration);
await RunCheckoutGuardAsync(services, tenantProvider, validation, tilopayOptions);
await RunValidationPlanVisibilityAsync(services, tenantProvider, validation, tilopayOptions);
var checkout = await RunRealCheckoutAsync(services, tenantProvider, validation, tilopayOptions);
await RunExitoCancelAsync(services, tenantProvider, validation, tilopayOptions, checkout.Reference);
await RunClientMatrixAsync(tilopayOptions);

Console.WriteLine($"KNOWN_UNPAID_REFERENCE={checkout.Reference}");
Console.WriteLine($"VALIDATION_TENANT_ID={validation.TenantId}");
Console.WriteLine($"VALIDATION_PLAN_ID={validation.PlanId}");

await CleanupValidationDataAsync(services, validation.TenantId);
tenantProvider.TenantId = validation.TenantId;

await RunWebhookSuccessAsync(services, tenantProvider, validation);
await RunWebhookFailureAsync(services, tenantProvider, validation);
await RunWebhookDuplicateAsync(services, tenantProvider, validation);
await RunWebhookMismatchAsync(services, tenantProvider, validation);
await RunWebhookForeignTenantIsolationAsync(services, tenantProvider, validation);
await RunExportIsolationAsync(services, tenantProvider, validation);
await RunSecondaryEndpointIsolationAsync(services, tenantProvider, validation);
await CleanupValidationDataAsync(services, validation.TenantId);
await CleanupTenantOperationalDataAsync(services, validation.TenantId);

static ServiceProvider BuildServices(IConfiguration configuration, StaticTenantProvider tenantProvider)
{
    var tilopay = configuration.GetSection("Tilopay").Get<OpcionesTilopay>() ?? new OpcionesTilopay();

    var services = new ServiceCollection();
    services.AddLogging(builder =>
    {
        builder.ClearProviders();
        builder.AddSimpleConsole(options => options.SingleLine = true);
        builder.SetMinimumLevel(LogLevel.Information);
    });
    services.AddMemoryCache();
    services.AddHttpContextAccessor();
    services.AddSingleton<ITenantProvider>(tenantProvider);
    services.AddScoped<TenantSessionConnectionInterceptor>();
    services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        options.UseSqlServer(configuration.GetConnectionString("ConexionSql"));
        options.AddInterceptors(serviceProvider.GetRequiredService<TenantSessionConnectionInterceptor>());
    });
    services.AddIdentityCore<AppUsuario>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
    services.AddScoped<SuscripcionService>();
    services.AddScoped<SaaSPaymentService>();
    services.AddScoped<PaymentProviderResolver>();
    services.AddHttpClient<PublicCallbackHealthService>(client => client.Timeout = TimeSpan.FromSeconds(10));
    services.AddSingleton<IOptions<OpcionesPago>>(Options.Create(new OpcionesPago
    {
        ProveedorPredeterminado = PaymentProviderType.Tilopay,
        PublicBaseUrl = "https://validation.example/luxuryapp/"
    }));
    services.AddSingleton<IOptions<OpcionesTilopay>>(Options.Create(tilopay));
    services.AddHttpClient<TilopayService>((_, client) =>
    {
        client.BaseAddress = new Uri(tilopay.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, tilopay.TimeoutSeconds));
    });
    services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<TilopayService>());
    return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}

static async Task RunSqlSessionContextPoolingAsync(IConfiguration configuration)
{
    var baseConnectionString = configuration.GetConnectionString("ConexionSql")
        ?? throw new InvalidOperationException("No existe ConnectionStrings:ConexionSql para validar SESSION_CONTEXT.");

    var sequentialConnectionString = new SqlConnectionStringBuilder(baseConnectionString)
    {
        Pooling = true,
        MinPoolSize = 1,
        MaxPoolSize = 1
    }.ConnectionString;

    var concurrentConnectionString = new SqlConnectionStringBuilder(baseConnectionString)
    {
        Pooling = true,
        MinPoolSize = 1,
        MaxPoolSize = 4
    }.ConnectionString;

    var tenantA = Guid.NewGuid();
    var tenantB = Guid.NewGuid();
    var tenantC = Guid.NewGuid();

    using var sequentialServices = BuildSessionContextServices(sequentialConnectionString);
    var sequentialFirst = await ReadSessionContextTenantAsync(sequentialServices, tenantA);
    var sequentialCleared = await ReadSessionContextTenantAsync(sequentialServices, null);
    var sequentialSecond = await ReadSessionContextTenantAsync(sequentialServices, tenantB);
    var sequentialPass = sequentialFirst == tenantA && sequentialCleared is null && sequentialSecond == tenantB;

    using var concurrentServices = BuildSessionContextServices(concurrentConnectionString);
    var expectedTenants = new[] { tenantA, tenantB, tenantC };

    var concurrentResults = await Task.WhenAll(
        Enumerable.Range(0, 24).Select(async index =>
        {
            var expectedTenant = expectedTenants[index % expectedTenants.Length];

            for (var iteration = 0; iteration < 3; iteration++)
            {
                var observedTenant = await ReadSessionContextTenantAsync(concurrentServices, expectedTenant);
                if (observedTenant != expectedTenant)
                {
                    return false;
                }
            }

            return true;
        }));

    var concurrentCleared = await ReadSessionContextTenantAsync(concurrentServices, null);
    var concurrentPass = concurrentResults.All(result => result) && concurrentCleared is null;

    Print(
        "SqlPoolingSessionContext",
        sequentialPass && concurrentPass ? "PASS" : "FAIL",
        $"SequentialA={sequentialFirst}, SequentialClear={sequentialCleared?.ToString() ?? "<null>"}, SequentialB={sequentialSecond}, ConcurrentOk={concurrentResults.Count(result => result)}/{concurrentResults.Length}, ConcurrentClear={concurrentCleared?.ToString() ?? "<null>"}");
}

static ServiceProvider BuildSessionContextServices(string connectionString)
{
    var services = new ServiceCollection();
    services.AddLogging(builder =>
    {
        builder.ClearProviders();
        builder.AddSimpleConsole(options => options.SingleLine = true);
        builder.SetMinimumLevel(LogLevel.Warning);
    });
    services.AddHttpContextAccessor();
    services.AddSingleton<ITenantExecutionContextAccessor, TenantExecutionContextAccessor>();
    services.AddScoped<ITenantProvider, TenantProvider>();
    services.AddScoped<TenantSessionConnectionInterceptor>();
    services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        options.UseSqlServer(connectionString);
        options.AddInterceptors(serviceProvider.GetRequiredService<TenantSessionConnectionInterceptor>());
    });
    return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
}

static async Task<Guid?> ReadSessionContextTenantAsync(IServiceProvider services, Guid? tenantId)
{
    using var scope = services.CreateScope();
    var tenantExecutionAccessor = scope.ServiceProvider.GetRequiredService<ITenantExecutionContextAccessor>();

    using var tenantScope = tenantId.HasValue
        ? tenantExecutionAccessor.BeginScope(tenantId.Value)
        : tenantExecutionAccessor.ClearScope();

    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await db.Database.OpenConnectionAsync();

    try
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)";

        var value = await command.ExecuteScalarAsync();
        return value is null || value == DBNull.Value
            ? null
            : (Guid)value;
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}

static async Task<ValidationContext> EnsureValidationAsync(IServiceProvider services, StaticTenantProvider tenantProvider)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = Guid.Empty;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    const string tenantName = "Tilopay Validation";
    const string email = "tilopay.validation@luxuryapp.local";

    var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Nombre == tenantName);
    if (tenant is null)
    {
        tenant = new Tenant { Id = Guid.NewGuid(), Nombre = tenantName, Activo = true, FechaCreacion = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user is null)
    {
        user = new AppUsuario
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Name = "Tilopay Validation",
            TenantId = tenant.Id,
            State = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    var plan = await db.Planes.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.PrecioMensual).FirstAsync();
    await CleanupValidationDataAsync(services, tenant.Id);
    tenantProvider.TenantId = tenant.Id;
    Print("Seed", "PASS", $"Tenant={tenant.Id}, User={user.Id}, Plan={plan.Id}");
    return new ValidationContext(tenant.Id, user.Id, plan.Id, email);
}

static async Task CleanupValidationDataAsync(IServiceProvider services, Guid tenantId)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Facturas WHERE TenantId = {tenantId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM EventosPago WHERE TenantId = {tenantId} OR ReferenciaExterna LIKE {"VAL-%"}");
    await db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM HistorialSuscripciones WHERE SuscripcionId IN (SELECT Id FROM Suscripciones WHERE TenantId = {tenantId})");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PagosSuscripcion WHERE TenantId = {tenantId}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Suscripciones WHERE TenantId = {tenantId}");
}

static async Task RunCheckoutGuardAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation, OpcionesTilopay tilopayOptions)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var before = await db.PagosSuscripcion.CountAsync(p => p.TenantId == validation.TenantId);
    var controller = CreateController(scope.ServiceProvider, validation, tilopayOptions, string.Empty);
    var result = await controller.Checkout(validation.PlanId, CancellationToken.None);
    var after = await db.PagosSuscripcion.CountAsync(p => p.TenantId == validation.TenantId);
    var redirect = result as RedirectToActionResult;
    Print("CheckoutGuardLocalhost", redirect?.ActionName == "Planes" && controller.TempData.ContainsKey("BillingError") && before == after ? "PASS" : "FAIL",
        $"Action={redirect?.ActionName}, Before={before}, After={after}");
}

static async Task RunValidationPlanVisibilityAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation, OpcionesTilopay tilopayOptions)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;

    var hiddenController = CreateController(scope.ServiceProvider, validation, tilopayOptions, "https://validation.example/luxuryapp/", enableValidationPlans: false);
    var visibleController = CreateController(scope.ServiceProvider, validation, tilopayOptions, "https://validation.example/luxuryapp/", enableValidationPlans: true);

    var hiddenResult = await hiddenController.Planes() as ViewResult;
    var visibleResult = await visibleController.Planes() as ViewResult;

    var hiddenPlans = hiddenResult?.Model as List<Plan> ?? new List<Plan>();
    var visiblePlans = visibleResult?.Model as List<Plan> ?? new List<Plan>();

    var hiddenValidationPlan = hiddenPlans.Any(plan => plan.EsPlanValidacion);
    var visibleValidationPlan = visiblePlans.Any(plan => plan.EsPlanValidacion && plan.Nombre == "Prueba Tilopay" && plan.PrecioMensual == 1000m);

    Print("ValidationPlanVisibility", !hiddenValidationPlan && visibleValidationPlan ? "PASS" : "FAIL",
        $"HiddenCount={hiddenPlans.Count}, VisibleCount={visiblePlans.Count}, VisibleValidationPlan={visibleValidationPlan}");
}

static async Task<RealCheckoutContext> RunRealCheckoutAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation, OpcionesTilopay tilopayOptions)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var controller = CreateController(
        scope.ServiceProvider,
        validation,
        tilopayOptions,
        "https://validation.example/luxuryapp/",
        enableValidationPlans: true);
    var result = await controller.Checkout(validation.PlanId, CancellationToken.None);
    var redirect = result as RedirectResult ?? throw new InvalidOperationException("Checkout no devolvió RedirectResult.");
    var pago = await db.PagosSuscripcion.IgnoreQueryFilters().OrderByDescending(p => p.FechaCreacionUtc).FirstAsync(p => p.TenantId == validation.TenantId);
    var suscripcion = await db.Suscripciones.IgnoreQueryFilters().FirstAsync(s => s.TenantId == validation.TenantId);
    var webhookAuditIsRedacted = string.IsNullOrWhiteSpace(tilopayOptions.WebhookAccessToken) ||
                                 pago.UltimoPayloadProveedor?.Contains(tilopayOptions.WebhookAccessToken, StringComparison.Ordinal) != true;
    var passed = redirect.Url?.Contains("tp.cr/", StringComparison.OrdinalIgnoreCase) == true &&
                 !string.IsNullOrWhiteSpace(pago.ProviderCheckoutId) &&
                 pago.Estado == EstadoPagoProveedor.Pendiente &&
                 suscripcion.Estado == EstadoSuscripcion.Pendiente &&
                 webhookAuditIsRedacted;
    Print("CheckoutReal", passed ? "PASS" : "FAIL",
        $"Reference={pago.ReferenciaInterna}, CheckoutId={pago.ProviderCheckoutId}, Pago={pago.Estado}, Suscripcion={suscripcion.Estado}, AuditRedacted={webhookAuditIsRedacted}");
    return new RealCheckoutContext(pago.ReferenciaInterna);
}

static async Task RunExitoCancelAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation, OpcionesTilopay tilopayOptions, string reference)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var controller = CreateController(scope.ServiceProvider, validation, tilopayOptions, "https://validation.example/luxuryapp/");
    var beforeState = (await db.Suscripciones.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.TenantId == validation.TenantId)).Estado;
    var beforeFacturas = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId);
    var exito = await controller.Exito(reference: reference, code: "1", description: "simulado") as ViewResult;
    _ = controller.Cancelado("cancelado") as ViewResult;
    var afterState = (await db.Suscripciones.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.TenantId == validation.TenantId)).Estado;
    var afterFacturas = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId);
    var model = exito?.Model as ResultadoCheckoutViewModel;
    Print("SuccessCancelNoActivation", model?.Referencia == reference && beforeState == afterState && beforeFacturas == afterFacturas ? "PASS" : "FAIL",
        $"Reference={reference}, Before={beforeState}, After={afterState}, Facturas={afterFacturas}");
}

static async Task RunClientMatrixAsync(OpcionesTilopay realOptions)
{
    var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
    var cache = new MemoryCache(new MemoryCacheOptions());
    try
    {
        var missing = new TilopayService(new HttpClient(new ScriptedHandler((_, _) => throw new InvalidOperationException("No red"))), cache, Options.Create(new OpcionesTilopay()), loggerFactory.CreateLogger<TilopayService>());
        await missing.CreateCheckoutAsync(new PaymentCheckoutRequest { Reference = "VAL-MISSING", Amount = 1, Currency = "CRC", CustomerEmail = "x@y.com", CustomerName = "x", Description = "x", SuccessUrl = "https://a", CancelUrl = "https://b", WebhookUrl = "https://c" });
        Print("ClientMissingCredentials", "FAIL", "No lanzó excepción.");
    }
    catch (PaymentProviderConfigurationException ex)
    {
        Print("ClientMissingCredentials", "PASS", ex.Message);
    }

    await RunClientScenarioAsync("ClientLoginWithoutToken", loggerFactory, cache, realOptions, (request, _) =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/login", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"expires_in\":3600}", Encoding.UTF8, "application/json") });
        }
        throw new InvalidOperationException("No debería llegar a createLinkPayment.");
    }, typeof(InvalidOperationException), "access_token");

    await RunClientScenarioAsync("ClientCreateLink500", loggerFactory, cache, realOptions, (request, _) =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/login", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"token\",\"expires_in\":3600}", Encoding.UTF8, "application/json") });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"type\":\"500\"}", Encoding.UTF8, "application/json") });
    }, typeof(InvalidOperationException), "Tilopay no pudo generar el checkout.");

    await RunClientScenarioAsync("ClientCreateLinkWithoutUrl", loggerFactory, cache, realOptions, (request, _) =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/login", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"token\",\"expires_in\":3600}", Encoding.UTF8, "application/json") });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"type\":\"200\",\"id\":123}", Encoding.UTF8, "application/json") });
    }, typeof(InvalidOperationException), "checkout válido");
}

static async Task RunClientScenarioAsync(string name, ILoggerFactory loggerFactory, IMemoryCache cache, OpcionesTilopay realOptions, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler, Type expectedType, string expectedMessage)
{
    var options = new OpcionesTilopay { BaseUrl = "https://validation.example/", ApiUser = realOptions.ApiUser, ApiPassword = realOptions.ApiPassword, ApiKey = realOptions.ApiKey, TimeoutSeconds = 5 };
    var client = new HttpClient(new ScriptedHandler(handler)) { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(5) };
    var service = new TilopayService(client, cache, Options.Create(options), loggerFactory.CreateLogger<TilopayService>());
    try
    {
        await service.CreateCheckoutAsync(new PaymentCheckoutRequest { Reference = $"VAL-{name}", Amount = 1, Currency = "CRC", CustomerEmail = "x@y.com", CustomerName = "x", Description = "x", SuccessUrl = "https://a", CancelUrl = "https://b", WebhookUrl = "https://c" });
        Print(name, "FAIL", "No lanzó excepción.");
    }
    catch (Exception ex) when (expectedType.IsAssignableFrom(ex.GetType()) && ex.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase))
    {
        Print(name, "PASS", ex.Message);
    }
}

static async Task RunWebhookSuccessAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    const string reference = "LXA-AA1101-0000000001";
    await SeedAttemptAsync(db, validation, reference, EstadoSuscripcion.Pendiente);
    var service = CreateFakeWebhookService(scope.ServiceProvider, new FakeProvider(
        payload => ParsePayload(payload, "900001"),
        (_, _) => Task.FromResult(new PaymentVerificationResult { ProviderType = PaymentProviderType.Tilopay, Exists = true, IsSuccess = true, Reference = reference, StatusCode = "1", StatusDescription = "APPROVED", ProviderTransactionId = "900001", Amount = 8000m, Currency = "CRC", RawResponse = "{\"type\":\"200\"}" })));
    await service.ProcessTilopayWebhookAsync($"{{\"orderNumber\":\"{reference}\",\"tilopayOrderId\":900001}}", "corr-1");
    var pago = await db.PagosSuscripcion.IgnoreQueryFilters().FirstAsync(p => p.ReferenciaInterna == reference);
    var suscripcion = await db.Suscripciones.IgnoreQueryFilters().FirstAsync(s => s.TenantId == validation.TenantId);
    var facturas = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId);
    Print("WebhookSuccessSimulation", pago.Estado == EstadoPagoProveedor.Confirmado && suscripcion.Estado == EstadoSuscripcion.Activa && facturas == 1 ? "PASS" : "FAIL",
        $"Pago={pago.Estado}, Suscripcion={suscripcion.Estado}, Facturas={facturas}");
}

static async Task RunWebhookFailureAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    const string reference = "LXA-AA1101-0000000002";
    await SeedAttemptAsync(db, validation, reference, EstadoSuscripcion.Activa);
    var service = CreateFakeWebhookService(scope.ServiceProvider, new FakeProvider(
        payload => ParsePayload(payload, "900002"),
        (_, _) => Task.FromResult(new PaymentVerificationResult { ProviderType = PaymentProviderType.Tilopay, Exists = true, IsSuccess = false, Reference = reference, StatusCode = "2", StatusDescription = "DECLINED", ProviderTransactionId = "900002", Amount = 8000m, Currency = "CRC", RawResponse = "{\"type\":\"200\"}" })));
    await service.ProcessTilopayWebhookAsync($"{{\"orderNumber\":\"{reference}\",\"tilopayOrderId\":900002}}", "corr-2");
    var pago = await db.PagosSuscripcion.IgnoreQueryFilters().FirstAsync(p => p.ReferenciaInterna == reference);
    var suscripcion = await db.Suscripciones.IgnoreQueryFilters().FirstAsync(s => s.TenantId == validation.TenantId);
    var facturas = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId && f.Estado == "Fallido");
    Print("WebhookFailureSimulation", pago.Estado == EstadoPagoProveedor.Fallido && suscripcion.Estado == EstadoSuscripcion.Morosa && facturas >= 1 ? "PASS" : "FAIL",
        $"Pago={pago.Estado}, Suscripcion={suscripcion.Estado}, FacturasFallidas={facturas}");
}

static async Task RunWebhookDuplicateAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    const string reference = "LXA-AA1101-0000000003";
    await SeedAttemptAsync(db, validation, reference, EstadoSuscripcion.Pendiente);
    var service = CreateFakeWebhookService(scope.ServiceProvider, new FakeProvider(
        payload => ParsePayload(payload, "900003"),
        (_, _) => Task.FromResult(new PaymentVerificationResult { ProviderType = PaymentProviderType.Tilopay, Exists = true, IsSuccess = true, Reference = reference, StatusCode = "1", StatusDescription = "APPROVED", ProviderTransactionId = "900003", Amount = 8000m, Currency = "CRC", RawResponse = "{\"type\":\"200\"}" })));
    await service.ProcessTilopayWebhookAsync($"{{\"orderNumber\":\"{reference}\",\"tilopayOrderId\":900003}}", "corr-3");
    var eventsBefore = await db.EventosPago.IgnoreQueryFilters().CountAsync(e => e.ReferenciaExterna == reference);
    var facturasBefore = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId && f.ProviderReference == reference);
    var result = await service.ProcessTilopayWebhookAsync($"{{\"orderNumber\":\"{reference}\",\"tilopayOrderId\":900003}}", "corr-4");
    var eventsAfter = await db.EventosPago.IgnoreQueryFilters().CountAsync(e => e.ReferenciaExterna == reference);
    var facturasAfter = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId && f.ProviderReference == reference);
    Print("WebhookDuplicateSimulation", result.IsDuplicate && eventsBefore == eventsAfter && facturasBefore == facturasAfter ? "PASS" : "FAIL",
        $"EventosAntes={eventsBefore}, EventosDespues={eventsAfter}, FacturasAntes={facturasBefore}, FacturasDespues={facturasAfter}");
}

static async Task RunWebhookMismatchAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = validation.TenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    const string checkoutMismatchReference = "LXA-AA1101-0000000004";
    const string amountMismatchReference = "LXA-AA1101-0000000005";

    await CleanupWebhookReferenceAsync(db, validation.TenantId, checkoutMismatchReference);
    await CleanupWebhookReferenceAsync(db, validation.TenantId, amountMismatchReference);

    await SeedAttemptAsync(db, validation, checkoutMismatchReference, EstadoSuscripcion.Pendiente);
    await SeedAttemptAsync(db, validation, amountMismatchReference, EstadoSuscripcion.Pendiente);

    var checkoutAttempt = await db.PagosSuscripcion.IgnoreQueryFilters().FirstAsync(p => p.ReferenciaInterna == checkoutMismatchReference);
    checkoutAttempt.ProviderCheckoutId = "expected-checkout";

    var amountAttempt = await db.PagosSuscripcion.IgnoreQueryFilters().FirstAsync(p => p.ReferenciaInterna == amountMismatchReference);
    amountAttempt.ProviderCheckoutId = "expected-amount-checkout";

    await db.SaveChangesAsync();

    var checkoutMismatchService = CreateFakeWebhookService(scope.ServiceProvider, new FakeProvider(
        _ => new PaymentProviderWebhookData
        {
            ProviderType = PaymentProviderType.Tilopay,
            EventId = "tilopay-link-900004",
            EventType = "tilopay.link.completed",
            Reference = checkoutMismatchReference,
            ProviderOrderNumber = checkoutMismatchReference,
            ProviderCheckoutId = "spoofed-checkout",
            ProviderTransactionId = "900004",
            RawPayload = $"{{\"orderNumber\":\"{checkoutMismatchReference}\",\"tilopayOrderId\":900004,\"tilopayLinkId\":\"spoofed-checkout\"}}"
        },
        (_, _) => Task.FromResult(new PaymentVerificationResult
        {
            ProviderType = PaymentProviderType.Tilopay,
            Exists = true,
            IsSuccess = true,
            Reference = checkoutMismatchReference,
            StatusCode = "1",
            StatusDescription = "APPROVED",
            ProviderTransactionId = "900004",
            Amount = 8000m,
            Currency = "CRC",
            RawResponse = "{\"type\":\"200\"}"
        })));

    var amountMismatchService = CreateFakeWebhookService(scope.ServiceProvider, new FakeProvider(
        _ => new PaymentProviderWebhookData
        {
            ProviderType = PaymentProviderType.Tilopay,
            EventId = "tilopay-link-900005",
            EventType = "tilopay.link.completed",
            Reference = amountMismatchReference,
            ProviderOrderNumber = amountMismatchReference,
            ProviderCheckoutId = "expected-amount-checkout",
            ProviderTransactionId = "900005",
            RawPayload = $"{{\"orderNumber\":\"{amountMismatchReference}\",\"tilopayOrderId\":900005,\"tilopayLinkId\":\"expected-amount-checkout\"}}"
        },
        (_, _) => Task.FromResult(new PaymentVerificationResult
        {
            ProviderType = PaymentProviderType.Tilopay,
            Exists = true,
            IsSuccess = true,
            Reference = amountMismatchReference,
            StatusCode = "1",
            StatusDescription = "APPROVED",
            ProviderTransactionId = "900005",
            Amount = 9999m,
            Currency = "CRC",
            RawResponse = "{\"type\":\"200\"}"
        })));

    var checkoutRejected = false;
    var amountRejected = false;

    try
    {
        await checkoutMismatchService.ProcessTilopayWebhookAsync(
            $"{{\"orderNumber\":\"{checkoutMismatchReference}\",\"tilopayOrderId\":900004,\"tilopayLinkId\":\"spoofed-checkout\"}}",
            "corr-mismatch-checkout");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("checkout emitido", StringComparison.OrdinalIgnoreCase))
    {
        checkoutRejected = true;
    }

    try
    {
        await amountMismatchService.ProcessTilopayWebhookAsync(
            $"{{\"orderNumber\":\"{amountMismatchReference}\",\"tilopayOrderId\":900005,\"tilopayLinkId\":\"expected-amount-checkout\"}}",
            "corr-mismatch-amount");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("monto verificado", StringComparison.OrdinalIgnoreCase))
    {
        amountRejected = true;
    }

    var checkoutState = await db.PagosSuscripcion.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.ReferenciaInterna == checkoutMismatchReference);
    var amountState = await db.PagosSuscripcion.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.ReferenciaInterna == amountMismatchReference);
    var errorEvents = await db.EventosPago.IgnoreQueryFilters()
        .CountAsync(evento =>
            (evento.ReferenciaExterna == checkoutMismatchReference || evento.ReferenciaExterna == amountMismatchReference) &&
            evento.EstadoProcesamiento == "Error");
    var generatedInvoices = await db.Facturas.IgnoreQueryFilters()
        .CountAsync(factura => factura.TenantId == validation.TenantId &&
                               (factura.ProviderReference == checkoutMismatchReference || factura.ProviderReference == amountMismatchReference));

    var passed = checkoutRejected &&
                 amountRejected &&
                 checkoutState.Estado == EstadoPagoProveedor.Pendiente &&
                 amountState.Estado == EstadoPagoProveedor.Pendiente &&
                 generatedInvoices == 0 &&
                 errorEvents == 2;

    Print(
        "WebhookMismatchSimulation",
        passed ? "PASS" : "FAIL",
        $"CheckoutRejected={checkoutRejected}, AmountRejected={amountRejected}, ErrorEvents={errorEvents}, Invoices={generatedInvoices}, CheckoutState={checkoutState.Estado}, AmountState={amountState.Estado}");
}

static async Task RunWebhookForeignTenantIsolationAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    var foreignTenantId = await EnsureValidationTenantAsync(services, "Tilopay Validation Foreign");

    try
    {
        await CleanupValidationDataAsync(services, validation.TenantId);
        await CleanupValidationDataAsync(services, foreignTenantId);

        tenantProvider.TenantId = Guid.Empty;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        const string currentReference = "LXA-AA1101-0000000006";
        const string foreignReference = "LXA-BB2202-0000000007";

        await SeedAttemptAsync(db, validation, currentReference, EstadoSuscripcion.Pendiente);
        await SeedAttemptAsync(db, new ValidationContext(foreignTenantId, validation.UserId, validation.PlanId, "foreign.validation@luxuryapp.local"), foreignReference, EstadoSuscripcion.Pendiente);

        var service = CreateFakeWebhookService(scope.ServiceProvider, new FakeProvider(
            payload => ParsePayload(payload, "900006"),
            (_, _) => Task.FromResult(new PaymentVerificationResult
            {
                ProviderType = PaymentProviderType.Tilopay,
                Exists = true,
                IsSuccess = true,
                Reference = foreignReference,
                StatusCode = "1",
                StatusDescription = "APPROVED",
                ProviderTransactionId = "900006",
                Amount = 8000m,
                Currency = "CRC",
                RawResponse = "{\"type\":\"200\"}"
            })));

        var result = await service.ProcessTilopayWebhookAsync(
            $"{{\"orderNumber\":\"{foreignReference}\",\"tilopayOrderId\":900006}}",
            "corr-foreign-isolation");

        var currentPayment = await db.PagosSuscripcion.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.ReferenciaInterna == currentReference);
        var foreignPayment = await db.PagosSuscripcion.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.ReferenciaInterna == foreignReference);
        var currentSubscription = await db.Suscripciones.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.TenantId == validation.TenantId);
        var foreignSubscription = await db.Suscripciones.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.TenantId == foreignTenantId);
        var foreignEvent = await db.EventosPago.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.ReferenciaExterna == foreignReference);
        var currentInvoices = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == validation.TenantId);
        var foreignInvoices = await db.Facturas.IgnoreQueryFilters().CountAsync(f => f.TenantId == foreignTenantId);

        var passed = result.IsProcessed &&
                     foreignPayment.Estado == EstadoPagoProveedor.Confirmado &&
                     foreignSubscription.Estado == EstadoSuscripcion.Activa &&
                     currentPayment.Estado == EstadoPagoProveedor.Pendiente &&
                     currentSubscription.Estado == EstadoSuscripcion.Pendiente &&
                     foreignEvent.TenantId == foreignTenantId &&
                     currentInvoices == 0 &&
                     foreignInvoices == 1;

        Print(
            "WebhookForeignTenantIsolation",
            passed ? "PASS" : "FAIL",
            $"CurrentState={currentPayment.Estado}/{currentSubscription.Estado}, ForeignState={foreignPayment.Estado}/{foreignSubscription.Estado}, ForeignEventTenant={foreignEvent.TenantId}, CurrentInvoices={currentInvoices}, ForeignInvoices={foreignInvoices}");
    }
    finally
    {
        await CleanupValidationDataAsync(services, validation.TenantId);
        await CleanupValidationDataAsync(services, foreignTenantId);
        tenantProvider.TenantId = validation.TenantId;
    }
}

static async Task RunExportIsolationAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    var foreignTenantId = await EnsureValidationTenantAsync(services, "Reporting Validation Foreign");

    try
    {
        await CleanupTenantOperationalDataAsync(services, validation.TenantId, foreignTenantId);

        var currentFixture = await SeedOperationalFixtureAsync(services, tenantProvider, validation.TenantId, "CURRENT");
        var foreignFixture = await SeedOperationalFixtureAsync(services, tenantProvider, foreignTenantId, "FOREIGN");

        using var scope = services.CreateScope();
        tenantProvider.TenantId = validation.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cobrosController = new CobrosController(db);
        var egresosController = new EgresosController(db);

        var cobroResult = await cobrosController.ExportarExcel(new CobroFiltroViewModel { VistaTiempo = "todo" });
        var egresoResult = await egresosController.ExportarExcel(new EgresoFiltroViewModel { VistaTiempo = "dia" });

        var cobroFile = cobroResult as FileContentResult;
        var egresoFile = egresoResult as FileContentResult;
        var cobroText = cobroFile is null ? string.Empty : ReadWorkbookText(cobroFile.FileContents);
        var egresoText = egresoFile is null ? string.Empty : ReadWorkbookText(egresoFile.FileContents);

        var passed = cobroFile is not null &&
                     egresoFile is not null &&
                     cobroText.Contains(currentFixture.ClientName, StringComparison.Ordinal) &&
                     !cobroText.Contains(foreignFixture.ClientName, StringComparison.Ordinal) &&
                     egresoText.Contains(currentFixture.ExpenseDetail, StringComparison.Ordinal) &&
                     !egresoText.Contains(foreignFixture.ExpenseDetail, StringComparison.Ordinal);

        Print(
            "ExportIsolation",
            passed ? "PASS" : "FAIL",
            $"CobrosCurrent={cobroText.Contains(currentFixture.ClientName, StringComparison.Ordinal)}, CobrosForeign={cobroText.Contains(foreignFixture.ClientName, StringComparison.Ordinal)}, EgresosCurrent={egresoText.Contains(currentFixture.ExpenseDetail, StringComparison.Ordinal)}, EgresosForeign={egresoText.Contains(foreignFixture.ExpenseDetail, StringComparison.Ordinal)}");
    }
    finally
    {
        await CleanupTenantOperationalDataAsync(services, validation.TenantId, foreignTenantId);
        tenantProvider.TenantId = validation.TenantId;
    }
}

static async Task RunSecondaryEndpointIsolationAsync(IServiceProvider services, StaticTenantProvider tenantProvider, ValidationContext validation)
{
    var foreignTenantId = await EnsureValidationTenantAsync(services, "Secondary Validation Foreign");

    try
    {
        await CleanupTenantOperationalDataAsync(services, validation.TenantId, foreignTenantId);

        var currentFixture = await SeedOperationalFixtureAsync(services, tenantProvider, validation.TenantId, "SECONDARY-CURRENT");
        var foreignFixture = await SeedOperationalFixtureAsync(services, tenantProvider, foreignTenantId, "SECONDARY-FOREIGN");

        using var scope = services.CreateScope();
        tenantProvider.TenantId = validation.TenantId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clientesController = new ClientesController(db, null!, null!, null!);
        var funcionariosController = new FuncionariosController(db);
        var serviciosController = new ServiciosController(db);

        var clientesResult = await clientesController.Autocompletado("VAL-CLIENT");
        var funcionariosResult = await funcionariosController.GetActivos();
        var servicioResult = await serviciosController.ObtenerPrecio(foreignFixture.ServiceId);

        var okClientes = clientesResult as OkObjectResult;
        var jsonFuncionarios = funcionariosResult as JsonResult;
        var jsonServicio = servicioResult as JsonResult;

        var clientesPayload = JsonSerializer.Serialize(okClientes?.Value);
        var funcionariosPayload = JsonSerializer.Serialize(jsonFuncionarios?.Value);
        var servicioPayload = JsonSerializer.Serialize(jsonServicio?.Value);

        var passed = okClientes is not null &&
                     jsonFuncionarios is not null &&
                     jsonServicio is not null &&
                     clientesPayload.Contains(currentFixture.ClientName, StringComparison.Ordinal) &&
                     !clientesPayload.Contains(foreignFixture.ClientName, StringComparison.Ordinal) &&
                     funcionariosPayload.Contains(currentFixture.EmployeeName, StringComparison.Ordinal) &&
                     !funcionariosPayload.Contains(foreignFixture.EmployeeName, StringComparison.Ordinal) &&
                     string.Equals(servicioPayload, "null", StringComparison.OrdinalIgnoreCase);

        Print(
            "SecondaryEndpointIsolation",
            passed ? "PASS" : "FAIL",
            $"ClientesCurrent={clientesPayload.Contains(currentFixture.ClientName, StringComparison.Ordinal)}, ClientesForeign={clientesPayload.Contains(foreignFixture.ClientName, StringComparison.Ordinal)}, FuncionariosCurrent={funcionariosPayload.Contains(currentFixture.EmployeeName, StringComparison.Ordinal)}, FuncionariosForeign={funcionariosPayload.Contains(foreignFixture.EmployeeName, StringComparison.Ordinal)}, ForeignServicePayload={servicioPayload}");
    }
    finally
    {
        await CleanupTenantOperationalDataAsync(services, validation.TenantId, foreignTenantId);
        tenantProvider.TenantId = validation.TenantId;
    }
}

static async Task ReplayTilopayWebhookAsync(IServiceProvider services, StaticTenantProvider tenantProvider, string payloadPath)
{
    tenantProvider.TenantId = Guid.Empty;

    if (!File.Exists(payloadPath))
    {
        throw new FileNotFoundException("No se encontro el archivo de payload indicado.", payloadPath);
    }

    var payload = await File.ReadAllTextAsync(payloadPath);

    using var scope = services.CreateScope();
    var paymentService = scope.ServiceProvider.GetRequiredService<SaaSPaymentService>();
    var result = await paymentService.ProcessTilopayWebhookAsync(
        payload,
        $"manual-replay-{Guid.NewGuid():N}");

    Print(
        "ReplayTilopayWebhook",
        result.IsProcessed ? "PASS" : "FAIL",
        $"EventId={result.EventId}, Reference={result.Reference}, Duplicate={result.IsDuplicate}, Message={result.Message}, EstadoPago={result.EstadoPago}");
}

static BillingController CreateController(IServiceProvider services, ValidationContext validation, OpcionesTilopay tilopayOptions, string publicBaseUrl, bool enableValidationPlans = false)
{
    var controller = new BillingController(
        services.GetRequiredService<ILogger<BillingController>>(),
        services.GetRequiredService<ApplicationDbContext>(),
        services.GetRequiredService<SaaSPaymentService>(),
        services.GetRequiredService<PublicCallbackHealthService>(),
        services.GetRequiredService<UserManager<AppUsuario>>(),
        Options.Create(tilopayOptions),
        Options.Create(new OpcionesPago
        {
            ProveedorPredeterminado = PaymentProviderType.Tilopay,
            PublicBaseUrl = publicBaseUrl,
            EnableValidationPlans = enableValidationPlans,
            ValidatePublicCallbackReachability = false
        }));
    var httpContext = new DefaultHttpContext { RequestServices = services, User = BuildPrincipal(validation) };
    httpContext.Request.Scheme = "https";
    httpContext.Request.Host = new HostString("localhost", 5057);
    controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    controller.Url = new SimpleUrlHelper();
    controller.TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider());
    return controller;
}

static SaaSPaymentService CreateFakeWebhookService(IServiceProvider services, FakeProvider fakeProvider)
{
    var resolver = new PaymentProviderResolver(new[] { fakeProvider });
    return new SaaSPaymentService(
        services.GetRequiredService<ApplicationDbContext>(),
        resolver,
        services.GetRequiredService<SuscripcionService>(),
        Options.Create(new OpcionesPago { ProveedorPredeterminado = PaymentProviderType.Tilopay, PublicBaseUrl = "https://validation.example/luxuryapp/" }),
        services.GetRequiredService<IOptions<OpcionesTilopay>>(),
        services.GetRequiredService<ILogger<SaaSPaymentService>>());
}

static PaymentProviderWebhookData ParsePayload(string payload, string transactionId)
{
    var json = JsonDocument.Parse(payload);
    var reference = json.RootElement.TryGetProperty("reference", out var referenceElement)
        ? referenceElement.GetString() ?? string.Empty
        : json.RootElement.GetProperty("orderNumber").GetString() ?? string.Empty;
    return new PaymentProviderWebhookData
    {
        ProviderType = PaymentProviderType.Tilopay,
        EventId = $"tilopay-link-{transactionId}",
        EventType = "tilopay.link.completed",
        Reference = reference,
        ProviderOrderNumber = json.RootElement.TryGetProperty("orderNumber", out var orderElement)
            ? orderElement.GetString()
            : null,
        ProviderCheckoutId = $"link-{transactionId}",
        ProviderTransactionId = transactionId,
        RawPayload = payload
    };
}

static async Task SeedAttemptAsync(ApplicationDbContext db, ValidationContext validation, string reference, EstadoSuscripcion estado)
{
    if (!await db.PagosSuscripcion.IgnoreQueryFilters().AnyAsync(p => p.ReferenciaInterna == reference))
    {
        db.PagosSuscripcion.Add(new PagoSuscripcion
        {
            Id = Guid.NewGuid(),
            TenantId = validation.TenantId,
            PlanId = validation.PlanId,
            Proveedor = PaymentProviderType.Tilopay,
            Estado = EstadoPagoProveedor.Pendiente,
            ReferenciaInterna = reference,
            ProviderReference = reference,
            ClienteNombre = "Validation",
            ClienteEmail = validation.Email,
            Descripcion = reference,
            Monto = 8000m,
            Moneda = "CRC",
            FechaCreacionUtc = DateTime.UtcNow,
            FechaActualizacionUtc = DateTime.UtcNow
        });
    }

    var suscripcion = await db.Suscripciones.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == validation.TenantId);
    if (suscripcion is null)
    {
        db.Suscripciones.Add(new Suscripcion
        {
            Id = Guid.NewGuid(),
            TenantId = validation.TenantId,
            PlanId = validation.PlanId,
            Proveedor = PaymentProviderType.Tilopay,
            ProviderReference = reference,
            Estado = estado,
            FechaInicio = DateTime.UtcNow,
            FechaUltimaActualizacionUtc = DateTime.UtcNow,
            MotivoEstado = $"Estado inicial {estado}"
        });
    }
    else
    {
        suscripcion.PlanId = validation.PlanId;
        suscripcion.ProviderReference = reference;
        suscripcion.Estado = estado;
        suscripcion.ProviderTransactionId = null;
        suscripcion.ProviderPaymentLinkId = null;
        suscripcion.FechaUltimoPagoUtc = null;
        suscripcion.FechaUltimaActualizacionUtc = DateTime.UtcNow;
        suscripcion.MotivoEstado = $"Estado inicial {estado}";
    }

    await db.SaveChangesAsync();
}

static async Task<Guid> EnsureValidationTenantAsync(IServiceProvider services, string tenantName)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Nombre == tenantName);

    if (tenant is null)
    {
        tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Nombre = tenantName,
            Activo = true,
            FechaCreacion = DateTime.UtcNow
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
    }
    else if (!tenant.Activo)
    {
        tenant.Activo = true;
        await db.SaveChangesAsync();
    }

    return tenant.Id;
}

static async Task CleanupWebhookReferenceAsync(ApplicationDbContext db, Guid tenantId, string reference)
{
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Facturas WHERE TenantId = {tenantId} AND ProviderReference = {reference}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM EventosPago WHERE TenantId = {tenantId} AND ReferenciaExterna = {reference}");
    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PagosSuscripcion WHERE TenantId = {tenantId} AND ReferenciaInterna = {reference}");
}

static async Task CleanupTenantOperationalDataAsync(IServiceProvider services, params Guid[] tenantIds)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    foreach (var tenantId in tenantIds.Where(id => id != Guid.Empty).Distinct())
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE d
               FROM DetalleCobroProductos d
               INNER JOIN Cobros c ON c.IdCobro = d.CobroId
               WHERE c.TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Cobros WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Egresos WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PagosFuncionarios WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Funcionarios WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Puestos WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Servicios WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Categorias WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ClienteImagenes WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ClienteVisitas WHERE TenantId = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Clientes WHERE TenantId = {tenantId}");
    }
}

static async Task<OperationalFixture> SeedOperationalFixtureAsync(IServiceProvider services, StaticTenantProvider tenantProvider, Guid tenantId, string tag)
{
    using var scope = services.CreateScope();
    tenantProvider.TenantId = tenantId;
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var clientName = $"VAL-CLIENT-{tag}";
    var phone = await GenerateUniquePhoneKeyAsync(db);
    var employeeName = $"VAL-FUNC-{tag}";
    var expenseDetail = $"VAL-EGRESO-{tag}";

    var puesto = new Puesto
    {
        NombrePuesto = $"VAL-PUESTO-{tag}",
        Detalle = $"Fixture {tag}",
        Activo = true
    };

    db.Puestos.Add(puesto);
    await db.SaveChangesAsync();

    var funcionario = new Funcionario
    {
        Nombre = employeeName,
        IdPuesto = puesto.IdPuesto,
        ColorCalendario = "#123456",
        PorcentajeGanancia = 10,
        PorcentajeProducto = 10,
        FechaIngreso = DateTime.UtcNow,
        Activo = true
    };

    var categoria = new Categoria
    {
        Nombre = $"VAL-CAT-{tag}",
        Detalle = $"VAL-CAT-DETAIL-{tag}",
        Activo = true
    };

    var servicio = new Servicio
    {
        Nombre = $"VAL-SERV-{tag}",
        Precio = tag.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase) ? 1700m : 1500m,
        DuracionMinutos = 60,
        Activo = true
    };

    var cliente = new ClientesModel
    {
        Nombre = clientName,
        NumeroTelefono = phone,
        CorreoElectronico = $"{tag.ToLowerInvariant()}@validation.local",
        FechaUltimaVisita = DateTime.UtcNow,
        FrecuenciaVisita = 30
    };

    db.AddRange(funcionario, categoria, servicio, cliente);
    await db.SaveChangesAsync();

    db.Cobros.Add(new Cobro
    {
        NombreCliente = clientName,
        FuncionarioId = funcionario.IdFuncionario,
        ServicioId = servicio.Id,
        FechaCobro = DateTime.Today.AddHours(9),
        Monto = servicio.Precio,
        MetodoPago = "SINPE"
    });

    db.Egresos.Add(new Egreso
    {
        CategoriaId = categoria.Id,
        FechaEgreso = DateTime.Today.AddHours(8),
        Detalle = expenseDetail,
        Monto = tag.Contains("FOREIGN", StringComparison.OrdinalIgnoreCase) ? 500m : 750m,
        MetodoPago = "EFECTIVO"
    });

    await db.SaveChangesAsync();

    return new OperationalFixture(clientName, employeeName, expenseDetail, servicio.Id);
}

static string ReadWorkbookText(byte[] fileContents)
{
    using var workbook = new XLWorkbook(new MemoryStream(fileContents));
    return string.Join(
        "|",
        workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed().Select(cell => cell.GetString())));
}

static async Task<string> GenerateUniquePhoneKeyAsync(ApplicationDbContext db)
{
    var buffer = new byte[8];

    while (true)
    {
        RandomNumberGenerator.Fill(buffer);
        var numeric = BitConverter.ToUInt64(buffer) % 1_000_000_000_000_000UL;
        var phone = numeric.ToString("D15");

        if (!await db.Clientes.IgnoreQueryFilters().AnyAsync(cliente => cliente.NumeroTelefono == phone))
        {
            return phone;
        }
    }
}

static ClaimsPrincipal BuildPrincipal(ValidationContext validation)
{
    var identity = new ClaimsIdentity("Validation");
    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, validation.UserId));
    identity.AddClaim(new Claim(ClaimTypes.Name, validation.Email));
    identity.AddClaim(new Claim(CustomClaimTypes.TenantId, validation.TenantId.ToString()));
    return new ClaimsPrincipal(identity);
}

static void Print(string name, string status, string detail) =>
    Console.WriteLine($"RESULT|{name}|{status}|{detail}");

internal sealed record ValidationContext(Guid TenantId, string UserId, Guid PlanId, string Email);
internal sealed record RealCheckoutContext(string Reference);
internal sealed record OperationalFixture(string ClientName, string EmployeeName, string ExpenseDetail, int ServiceId);

internal sealed class StaticTenantProvider : ITenantProvider
{
    public Guid TenantId { get; set; }
    public Guid GetTenantId() => TenantId == Guid.Empty ? throw new InvalidOperationException("Tenant no configurado.") : TenantId;
    public bool HasTenant() => TenantId != Guid.Empty;
}

internal sealed class SimpleUrlHelper : IUrlHelper
{
    public ActionContext ActionContext { get; } = new(new DefaultHttpContext(), new Microsoft.AspNetCore.Routing.RouteData(), new ActionDescriptor());
    public string? Action(UrlActionContext actionContext) => $"/{actionContext.Controller}/{actionContext.Action}";
    public string? Content(string? contentPath) => contentPath;
    public bool IsLocalUrl(string? url) => true;
    public string? Link(string? routeName, object? values) => $"https://validation.example/{routeName}";
    public string? RouteUrl(UrlRouteContext routeContext) => $"/{routeContext.RouteName}";
}

internal sealed class InMemoryTempDataProvider : ITempDataProvider
{
    private readonly Dictionary<string, object?> _data = new();
    public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>(_data);
    public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
    {
        _data.Clear();
        foreach (var value in values)
        {
            _data[value.Key] = value.Value;
        }
    }
}

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
    public ScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _handler(request, cancellationToken);
}

internal sealed class FakeProvider : IPaymentProvider
{
    private readonly Func<string, PaymentProviderWebhookData> _parse;
    private readonly Func<PaymentVerificationRequest, CancellationToken, Task<PaymentVerificationResult>> _verify;

    public FakeProvider(
        Func<string, PaymentProviderWebhookData> parse,
        Func<PaymentVerificationRequest, CancellationToken, Task<PaymentVerificationResult>> verify)
    {
        _parse = parse;
        _verify = verify;
    }

    public PaymentProviderType ProviderType => PaymentProviderType.Tilopay;
    public Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public PaymentProviderWebhookData ParseWebhook(string payload) => _parse(payload);
    public Task<PaymentVerificationResult> VerifyPaymentAsync(PaymentVerificationRequest request, CancellationToken cancellationToken = default) => _verify(request, cancellationToken);
}
