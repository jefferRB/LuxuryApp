using System.Security.Claims;
using LuxuryApp.Models.Identity;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Identity;
using LuxuryApp.Services.Tenant;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class IdentitySessionSecurityTests
    {
        [Fact]
        public async Task TenantSessionSecurityValidator_ShouldRejectSuspendedTenant()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid().ToString("N");

            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Nombre = "Tenant suspendido",
                Activo = false
            });

            context.Users.Add(new AppUsuario
            {
                Id = userId,
                UserName = "owner@test.local",
                NormalizedUserName = "OWNER@TEST.LOCAL",
                Email = "owner@test.local",
                NormalizedEmail = "OWNER@TEST.LOCAL",
                TenantId = tenantId,
                State = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });

            await context.SaveChangesAsync();

            var principal = BuildPrincipal(userId, tenantId);
            var validator = new TenantSessionSecurityValidator(
                context,
                NullLogger<TenantSessionSecurityValidator>.Instance);

            var isValid = await validator.ValidateAsync(principal);

            Assert.False(isValid);
        }

        [Fact]
        public async Task TenantSessionSecurityValidator_ShouldRejectTenantClaimMismatch()
        {
            var tenantProvider = new TestTenantProvider();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var actualTenantId = Guid.NewGuid();
            var staleTenantId = Guid.NewGuid();
            var userId = Guid.NewGuid().ToString("N");

            context.Tenants.Add(new Tenant
            {
                Id = actualTenantId,
                Nombre = "Tenant actual",
                Activo = true
            });

            context.Users.Add(new AppUsuario
            {
                Id = userId,
                UserName = "owner@test.local",
                NormalizedUserName = "OWNER@TEST.LOCAL",
                Email = "owner@test.local",
                NormalizedEmail = "OWNER@TEST.LOCAL",
                TenantId = actualTenantId,
                State = true,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });

            await context.SaveChangesAsync();

            var principal = BuildPrincipal(userId, staleTenantId);
            var validator = new TenantSessionSecurityValidator(
                context,
                NullLogger<TenantSessionSecurityValidator>.Instance);

            var isValid = await validator.ValidateAsync(principal);

            Assert.False(isValid);
        }

        [Fact]
        public async Task SuscripcionMiddleware_ShouldRejectSuspendedTenantWithActiveCookie()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var httpContextAccessor = new HttpContextAccessor();
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            context.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Nombre = "Tenant suspendido",
                Activo = false
            });

            var planId = Guid.NewGuid();

            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Base",
                PrecioMensual = 10,
                Moneda = "CRC",
                Activo = true
            });

            context.Suscripciones.Add(new Suscripcion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Estado = EstadoSuscripcion.Activa,
                FechaInicio = DateTime.UtcNow
            });

            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/Productos";
            httpContext.User = BuildPrincipal(userId: "user-1", tenantId);
            httpContext.RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService, FakeAuthenticationService>()
                .BuildServiceProvider();
            httpContextAccessor.HttpContext = httpContext;

            var middleware = new SuscripcionMiddleware(
                _ => Task.CompletedTask,
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<SuscripcionMiddleware>.Instance);

            await middleware.Invoke(httpContext, context);

            Assert.Equal("/Accounts/Acceso", httpContext.Response.Headers.Location.ToString());
            Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        }

        private static ClaimsPrincipal BuildPrincipal(string userId, Guid tenantId) =>
            new(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(CustomClaimTypes.UserId, userId),
                    new Claim(CustomClaimTypes.TenantId, tenantId.ToString())
                },
                authenticationType: "TestAuth"));

        private sealed class FakeAuthenticationService : IAuthenticationService
        {
            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
                Task.FromResult(AuthenticateResult.NoResult());

            public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
                Task.CompletedTask;

            public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
                Task.CompletedTask;
        }
    }
}
