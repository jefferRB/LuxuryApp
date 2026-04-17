using System.Security.Claims;
using LuxuryApp.Controllers.Platform;
using LuxuryApp.Models.Platform;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.SaaS;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class PlatformControllerTests
    {
        [Fact]
        public void CreatePromotionalCode_ShouldBindUsingCreateFormPrefix()
        {
            var action = typeof(PlatformController).GetMethod(nameof(PlatformController.CreatePromotionalCode));

            Assert.NotNull(action);

            var formParameter = action!.GetParameters()
                .Single(parameter => parameter.ParameterType == typeof(PlatformPromotionalCodeCreateViewModel));

            var bindAttribute = formParameter
                .GetCustomAttributes(typeof(BindAttribute), inherit: false)
                .OfType<BindAttribute>()
                .SingleOrDefault();

            Assert.NotNull(bindAttribute);
            Assert.Equal(nameof(PlatformPromotionalCodesPageViewModel.CreateForm), bindAttribute!.Prefix);
        }

        [Fact]
        public async Task CreatePromotionalCode_ShouldPersistCodeAndRedirect()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var planId = Guid.NewGuid();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Full",
                PrecioMensual = 99,
                Moneda = "CRC",
                Activo = true
            });

            await context.SaveChangesAsync();

            var controller = CreateController(context, "platform-user-1");

            var result = await controller.CreatePromotionalCode(
                new PlatformPromotionalCodeCreateViewModel
                {
                    Codigo = " vip30 ",
                    PlanId = planId,
                    DiasGratis = 30,
                    MaxUsos = 1,
                    Activo = true
                },
                CancellationToken.None);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(PlatformController.PromotionalCodes), redirect.ActionName);

            var persistedCode = Assert.Single(context.PromotionalCodes);
            Assert.Equal("VIP30", persistedCode.Codigo);
            Assert.Equal(planId, persistedCode.PlanId);
            Assert.Equal("platform-user-1", persistedCode.CreadoPorUserId);
        }

        [Fact]
        public async Task CreatePromotionalCode_WhenDuplicateCodeExists_ShouldAttachFieldErrorToCreateFormPrefix()
        {
            var tenantProvider = new TestTenantProvider { TenantId = Guid.NewGuid() };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var disposableContext = context;
            using var disposableConnection = connection;

            var planId = Guid.NewGuid();
            context.Planes.Add(new Plan
            {
                Id = planId,
                Nombre = "Full",
                PrecioMensual = 99,
                Moneda = "CRC",
                Activo = true
            });

            context.PromotionalCodes.Add(new PromotionalCode
            {
                Id = Guid.NewGuid(),
                Codigo = "VIP30",
                Activo = true,
                DiasGratis = 30,
                PlanId = planId,
                FechaCreacionUtc = DateTime.UtcNow
            });

            await context.SaveChangesAsync();

            var controller = CreateController(context, "platform-user-2");

            var result = await controller.CreatePromotionalCode(
                new PlatformPromotionalCodeCreateViewModel
                {
                    Codigo = "VIP30",
                    PlanId = planId,
                    DiasGratis = 30,
                    MaxUsos = 1,
                    Activo = true
                },
                CancellationToken.None);

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal("PromotionalCodes", view.ViewName);
            Assert.False(controller.ModelState.IsValid);
            Assert.Contains($"{nameof(PlatformPromotionalCodesPageViewModel.CreateForm)}.{nameof(PlatformPromotionalCodeCreateViewModel.Codigo)}", controller.ModelState.Keys);
        }

        private static PlatformController CreateController(ProyectoIdentity.Datos.ApplicationDbContext context, string userId)
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var accessCache = new TenantCommercialAccessCache(cache);
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
                        authenticationType: "TestAuth"))
            };

            var controller = new PlatformController(
                context,
                new TenantCommercialAccessResolver(context, cache, accessCache),
                accessCache)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };

            controller.TempData = new TempDataDictionary(httpContext, new FakeTempDataProvider());
            return controller;
        }

        private sealed class FakeTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

            public void SaveTempData(HttpContext context, IDictionary<string, object> values)
            {
            }
        }
    }
}
