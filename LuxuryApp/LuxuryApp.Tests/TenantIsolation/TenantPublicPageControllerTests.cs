using LuxuryApp.Controllers;
using LuxuryApp.Controllers.Configuracion;
using LuxuryApp.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class TenantPublicPageControllerTests
    {
        [Fact]
        public void PublicSiteController_IsAnonymousAndUsesSitioRoute()
        {
            Assert.NotEmpty(typeof(PublicSiteController)
                .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));

            var route = typeof(PublicSiteController)
                .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .OfType<RouteAttribute>()
                .SingleOrDefault();

            Assert.NotNull(route);
            Assert.Equal("sitio", route!.Template);
        }

        [Theory]
        [InlineData(nameof(PublicSiteController.GoReserve), "{slug}/go/reservar")]
        [InlineData(nameof(PublicSiteController.GoServiceReserve), "{slug}/go/servicio/{servicioId:int}/reservar")]
        [InlineData(nameof(PublicSiteController.GoWhatsApp), "{slug}/go/whatsapp")]
        [InlineData(nameof(PublicSiteController.GoMaps), "{slug}/go/maps")]
        [InlineData(nameof(PublicSiteController.GoWaze), "{slug}/go/waze")]
        public void PublicSiteController_GoEndpointsUseInternalRedirectRoutes(
            string actionName,
            string expectedTemplate)
        {
            var action = typeof(PublicSiteController).GetMethod(actionName);

            Assert.NotNull(action);
            var route = action!
                .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
                .OfType<HttpGetAttribute>()
                .SingleOrDefault();

            Assert.NotNull(route);
            Assert.Equal(expectedTemplate, route!.Template);
        }

        [Fact]
        public void PaginaPublicaController_RequiresTenantAdminRole()
        {
            var authorize = typeof(PaginaPublicaController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .OfType<AuthorizeAttribute>()
                .SingleOrDefault();

            Assert.NotNull(authorize);
            Assert.Equal(AppRoles.Administrador, authorize!.Roles);
        }

        [Fact]
        public void PaginaPublicaPost_RequiresAntiForgeryToken()
        {
            var post = typeof(PaginaPublicaController)
                .GetMethods()
                .Single(method =>
                    method.Name == nameof(PaginaPublicaController.Index) &&
                    method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Any());

            Assert.True(post
                .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true)
                .Any());
        }

        [Fact]
        public void PaginaPublicaImagePosts_RequireAntiForgeryToken()
        {
            var actionNames = new[]
            {
                nameof(PaginaPublicaController.UploadLogo),
                nameof(PaginaPublicaController.RemoveLogo),
                nameof(PaginaPublicaController.UploadCover),
                nameof(PaginaPublicaController.RemoveCover),
                nameof(PaginaPublicaController.UploadBusinessGalleryImage),
                nameof(PaginaPublicaController.RemoveBusinessGalleryImage),
                nameof(PaginaPublicaController.UploadServiceMainImage),
                nameof(PaginaPublicaController.RemoveServiceMainImage),
                nameof(PaginaPublicaController.UploadLocationImage),
                nameof(PaginaPublicaController.RemoveLocationImage)
            };

            foreach (var actionName in actionNames)
            {
                var post = typeof(PaginaPublicaController)
                    .GetMethods()
                    .Single(method =>
                        method.Name == actionName &&
                        method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Any());

                Assert.True(post
                    .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true)
                    .Any());
            }
        }
    }
}
