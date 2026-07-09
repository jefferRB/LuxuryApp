namespace LuxuryApp.Tests.TenantIsolation
{
    public class TenantPublicPageViewSafetyTests
    {
        [Fact]
        public void PublicLandingView_UsesSafeResponsiveGalleryAndServiceCardClasses()
        {
            var view = File.ReadAllText(ProjectPath("Views", "PublicSite", "Index.cshtml"));
            var css = File.ReadAllText(ProjectPath("wwwroot", "css", "tenant-public-page.css"));

            Assert.DoesNotContain("Html.Raw", view, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script", view, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("tpp-site-nav", view);
            Assert.Contains("tpp-nav-toggle", view);
            Assert.Contains("tpp-floating-whatsapp", view);
            Assert.Contains("Model.HeroEyebrow", view);
            Assert.Contains("Model.BusinessHours", view);
            Assert.Contains("Model.WazeActionUrl", view);
            Assert.Contains("var hasGallery = Model.BusinessGallery.Count > 0", view);
            Assert.Contains("public-gallery-grid", view);
            Assert.Contains("data-count=\"@Model.BusinessGallery.Count\"", view);
            Assert.Contains("tpp-gallery-item", view);
            Assert.Contains("loading=\"lazy\"", view);

            Assert.Contains("public-services-grid", view);
            Assert.Contains("public-service-card", view);
            Assert.Contains("public-service-image", view);
            Assert.Contains("public-service-body", view);
            Assert.Contains("public-service-cta", view);
            Assert.Contains("service.ReserveActionUrl ?? service.BookingUrl", view);

            Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr))", css);
            Assert.Contains("aspect-ratio: 4 / 5", css);
            Assert.Contains("aspect-ratio: 4 / 3", css);
            Assert.Contains("max-height: 420px", css);
            Assert.Contains("object-fit: cover", css);
            Assert.Contains("object-position: center", css);
            Assert.Contains(".public-service-image img", css);
            Assert.Contains("max-height: 250px", css);
            Assert.Contains(".tpp-site-nav", css);
            Assert.Contains(".tpp-floating-whatsapp", css);
            Assert.Contains("scroll-behavior: smooth", css);
        }

        [Fact]
        public void AdminPublicPage_UsesLocalCropperScriptAndNoHtmlRaw()
        {
            var view = File.ReadAllText(ProjectPath("Views", "PaginaPublica", "Index.cshtml"));
            var script = File.ReadAllText(ProjectPath("wwwroot", "js", "tenant-public-image-uploader.js"));

            Assert.DoesNotContain("Html.Raw", view, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("~/js/tenant-public-image-uploader.js", view);
            Assert.Contains("data-public-image-upload", view);
            Assert.Contains("data-crop-aspect", view);
            Assert.Contains("asp-for=\"HeroEyebrow\"", view);
            Assert.Contains("asp-for=\"BusinessHours\"", view);
            Assert.Contains("asp-for=\"WazeUrl\"", view);
            Assert.Contains("CropX", script);
            Assert.Contains("fetch(", script);
            Assert.DoesNotContain("https://", script, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PublicLandingCsp_DoesNotAllowScriptsOrFrames()
        {
            var controller = File.ReadAllText(ProjectPath("Controllers", "PublicSiteController.cs"));

            Assert.Contains("\"script-src 'none'\"", controller);
            Assert.Contains("\"frame-ancestors 'none'\"", controller);
            Assert.DoesNotContain("iframe", controller, StringComparison.OrdinalIgnoreCase);
        }

        private static string ProjectPath(params string[] parts)
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}
