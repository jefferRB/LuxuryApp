using LuxuryApp.Models.Finanzas;
using LuxuryApp.Models.PublicPages;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.PublicImages;
using LuxuryApp.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class PublicImageUploadServiceTests
    {
        [Fact]
        public async Task UploadPublicPageAsset_ValidLogo_ReencodesWebpAndUsesOpaqueKey()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Upload");

            var storage = new FakePublicImageStorageService();
            var service = CreateUploadService(context, tenantProvider, storage);

            var asset = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Logo,
                CreateImageFile("mi-logo.png"),
                "user");

            Assert.Equal(TenantPublicAssetType.Logo, asset.AssetType);
            Assert.EndsWith(".webp", asset.StorageKey);
            Assert.DoesNotContain("mi-logo", asset.StorageKey, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("image/webp", asset.ContentType);
            Assert.Equal($"https://media.test/{asset.StorageKey}", asset.PublicUrl);
            Assert.True(storage.UploadedBytes[asset.StorageKey].Length > 0);
            Assert.Equal((byte)'R', storage.UploadedBytes[asset.StorageKey][0]);
        }

        [Fact]
        public async Task UploadPublicPageAsset_RejectsDangerousDoubleExtension()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Upload");

            var service = CreateUploadService(context, tenantProvider, new FakePublicImageStorageService());

            await Assert.ThrowsAsync<PublicImageUploadException>(() =>
                service.UploadPublicPageAssetAsync(
                    TenantPublicAssetType.Logo,
                    CreateImageFile("logo.svg.jpg"),
                    "user"));
        }

        [Fact]
        public async Task UploadPublicPageAsset_ReplacingLogo_InactivatesOldAssetAndDeletesOldStorage()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Replace");

            var storage = new FakePublicImageStorageService();
            var service = CreateUploadService(context, tenantProvider, storage);

            var first = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Logo,
                CreateImageFile("logo-one.png"),
                "user");
            var second = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Logo,
                CreateImageFile("logo-two.png"),
                "user");

            var assets = await context.TenantPublicAssets
                .AsNoTracking()
                .OrderBy(asset => asset.CreatedAtUtc)
                .ToListAsync();

            Assert.Equal(2, assets.Count);
            Assert.False(assets.Single(asset => asset.Id == first.Id).IsActive);
            Assert.True(assets.Single(asset => asset.Id == second.Id).IsActive);
            Assert.Contains(first.StorageKey, storage.DeletedKeys);
        }

        [Fact]
        public async Task UploadPublicPageAsset_ValidCrop_AppliesCropBeforeSaving()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Crop");

            var storage = new FakePublicImageStorageService();
            var service = CreateUploadService(context, tenantProvider, storage);

            var asset = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Logo,
                CreateSplitColorImageFile("crop.png"),
                "user",
                crop: new PublicImageCropRequest
                {
                    CropX = 40,
                    CropY = 0,
                    CropWidth = 40,
                    CropHeight = 40
                });

            using var image = Image.Load<Rgba32>(storage.UploadedBytes[asset.StorageKey]);
            var center = image[image.Width / 2, image.Height / 2];

            Assert.True(center.B > center.R, $"Expected blue-dominant crop, got R:{center.R} B:{center.B}.");
            Assert.Equal(40, asset.Width);
            Assert.Equal(40, asset.Height);
        }

        [Fact]
        public async Task UploadPublicPageAsset_InvalidCrop_NormalizesSafely()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Invalid Crop");

            var storage = new FakePublicImageStorageService();
            var service = CreateUploadService(context, tenantProvider, storage);

            var asset = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.BusinessGallery,
                CreateImageFile("gallery.png"),
                "user",
                crop: new PublicImageCropRequest
                {
                    CropX = 999,
                    CropY = 999,
                    CropWidth = 100,
                    CropHeight = 100
                });

            Assert.True(asset.Width > 0);
            Assert.True(asset.Height > 0);
            Assert.True(storage.UploadedBytes[asset.StorageKey].Length > 0);
        }

        [Fact]
        public async Task UploadServiceAsset_RejectsServiceFromAnotherTenant()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantB };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;

            await SeedTenantAsync(context, tenantB, "Tenant B");
            var serviceFromTenantB = await SeedServiceAsync(context, "Servicio B");

            tenantProvider.TenantId = tenantA;
            await SeedTenantAsync(context, tenantA, "Tenant A");
            var upload = CreateUploadService(context, tenantProvider, new FakePublicImageStorageService());

            await Assert.ThrowsAsync<PublicImageUploadException>(() =>
                upload.UploadServiceAssetAsync(
                    TenantPublicAssetType.ServiceMain,
                    serviceFromTenantB.Id,
                    CreateImageFile("servicio.png"),
                    "user"));
        }

        [Fact]
        public async Task UploadServiceAsset_ReplacingMainImage_InactivatesOldAsset()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Service Main");
            var servicio = await SeedServiceAsync(context, "Servicio");

            var storage = new FakePublicImageStorageService();
            var upload = CreateUploadService(context, tenantProvider, storage);

            var first = await upload.UploadServiceAssetAsync(
                TenantPublicAssetType.ServiceMain,
                servicio.Id,
                CreateImageFile("main-one.png"),
                "user");
            var second = await upload.UploadServiceAssetAsync(
                TenantPublicAssetType.ServiceMain,
                servicio.Id,
                CreateImageFile("main-two.png"),
                "user");

            var assets = await context.TenantPublicAssets
                .AsNoTracking()
                .Where(asset => asset.ServicioId == servicio.Id)
                .OrderBy(asset => asset.CreatedAtUtc)
                .ToListAsync();

            Assert.False(assets.Single(asset => asset.Id == first.Id).IsActive);
            Assert.True(assets.Single(asset => asset.Id == second.Id).IsActive);
            Assert.Contains(first.StorageKey, storage.DeletedKeys);
        }

        [Fact]
        public async Task UploadPublicPageAsset_RejectsWhenQuotaExceeded()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Quota");

            var upload = CreateUploadService(
                context,
                tenantProvider,
                new FakePublicImageStorageService(),
                new PublicImageOptions { MaxTenantPublicImageBytes = 1 });

            await Assert.ThrowsAsync<PublicImageUploadException>(() =>
                upload.UploadPublicPageAssetAsync(
                    TenantPublicAssetType.BusinessGallery,
                    CreateImageFile("gallery.png"),
                    "user"));
        }

        [Fact]
        public async Task UploadPublicPageAsset_ValidLocation_UsesLocationKeyAndIsSingleton()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Location");

            var storage = new FakePublicImageStorageService();
            var service = CreateUploadService(context, tenantProvider, storage);

            var first = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Location,
                CreateImageFile("fachada.png"),
                "user");

            Assert.Equal(TenantPublicAssetType.Location, first.AssetType);
            Assert.Contains("/public-page/location/", first.StorageKey);
            Assert.EndsWith(".webp", first.StorageKey);
            Assert.True(storage.UploadedBytes[first.StorageKey].Length > 0);

            // Reemplazo: la anterior queda inactiva (singleton) y se borra su storage.
            var second = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Location,
                CreateImageFile("interior.png"),
                "user");

            var assets = await context.TenantPublicAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(asset => asset.AssetType == TenantPublicAssetType.Location)
                .ToListAsync();

            Assert.Equal(2, assets.Count);
            Assert.False(assets.Single(asset => asset.Id == first.Id).IsActive);
            Assert.True(assets.Single(asset => asset.Id == second.Id).IsActive);
            Assert.Contains(first.StorageKey, storage.DeletedKeys);
        }

        [Fact]
        public async Task RemovePublicPageSingleton_Location_InactivatesAndDeletesStorage()
        {
            var tenantId = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantId };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;
            await SeedTenantAsync(context, tenantId, "Tenant Location Remove");

            var storage = new FakePublicImageStorageService();
            var service = CreateUploadService(context, tenantProvider, storage);

            var asset = await service.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Location,
                CreateImageFile("fachada.png"),
                "user");

            await service.RemovePublicPageSingletonAsync(TenantPublicAssetType.Location, "user");

            var stored = await context.TenantPublicAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(item => item.Id == asset.Id);

            Assert.False(stored.IsActive);
            Assert.NotNull(stored.DeletedAtUtc);
            Assert.Contains(asset.StorageKey, storage.DeletedKeys);
        }

        [Fact]
        public async Task RemovePublicPageSingleton_Location_DoesNotAffectOtherTenant()
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var tenantProvider = new TestTenantProvider { TenantId = tenantB };
            var (context, connection) = TestDbContextFactory.CreateSqliteContext(tenantProvider);
            using var _ = context;
            using var __ = connection;

            await SeedTenantAsync(context, tenantB, "Tenant B");
            var storage = new FakePublicImageStorageService();
            var uploadB = CreateUploadService(context, tenantProvider, storage);
            var assetB = await uploadB.UploadPublicPageAssetAsync(
                TenantPublicAssetType.Location,
                CreateImageFile("fachada-b.png"),
                "user");

            // Otro tenant intenta remover: solo afecta su propio singleton (ninguno).
            tenantProvider.TenantId = tenantA;
            await SeedTenantAsync(context, tenantA, "Tenant A");
            var uploadA = CreateUploadService(context, tenantProvider, storage);
            await uploadA.RemovePublicPageSingletonAsync(TenantPublicAssetType.Location, "user");

            var stored = await context.TenantPublicAssets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(item => item.Id == assetB.Id);

            Assert.True(stored.IsActive);
            Assert.DoesNotContain(assetB.StorageKey, storage.DeletedKeys);
        }

        private static PublicImageUploadService CreateUploadService(
            ApplicationDbContext context,
            TestTenantProvider tenantProvider,
            FakePublicImageStorageService storage,
            PublicImageOptions? options = null)
        {
            options ??= new PublicImageOptions();
            return new PublicImageUploadService(
                context,
                tenantProvider,
                storage,
                new PublicAssetQuotaService(context, Options.Create(options)),
                new NoOpUploadedFileSecurityScanner(),
                Options.Create(options),
                NullLogger<PublicImageUploadService>.Instance);
        }

        private static IFormFile CreateImageFile(string fileName)
        {
            var stream = new MemoryStream();
            using (var image = new Image<Rgba32>(32, 32))
            {
                image.Mutate(context => context.BackgroundColor(Color.Red));
                image.SaveAsPng(stream);
            }

            stream.Position = 0;
            return new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
        }

        private static IFormFile CreateSplitColorImageFile(string fileName)
        {
            var stream = new MemoryStream();
            using (var image = new Image<Rgba32>(80, 40))
            {
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        image[x, y] = x < 40 ? Color.Red : Color.Blue;
                    }
                }

                image.SaveAsPng(stream);
            }

            stream.Position = 0;
            return new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
        }

        private static async Task SeedTenantAsync(
            ApplicationDbContext context,
            Guid tenantId,
            string name)
        {
            context.Tenants.Add(new Tenant { Id = tenantId, Nombre = name, Activo = true });
            await context.SaveChangesAsync();
        }

        private static async Task<Servicio> SeedServiceAsync(
            ApplicationDbContext context,
            string name)
        {
            var service = new Servicio
            {
                Nombre = name,
                Precio = 5000m,
                DuracionMinutos = 30,
                Activo = true
            };
            context.Servicios.Add(service);
            await context.SaveChangesAsync();
            return service;
        }

        private sealed class FakePublicImageStorageService : IPublicImageStorageService
        {
            public Dictionary<string, byte[]> UploadedBytes { get; } = new(StringComparer.Ordinal);
            public List<string> DeletedKeys { get; } = new();

            public async Task<PublicImageStoredObject> UploadAsync(
                string storageKey,
                Stream content,
                string contentType,
                CancellationToken cancellationToken = default)
            {
                if (!IsValidStorageKey(storageKey))
                {
                    throw new InvalidOperationException("Invalid key.");
                }

                if (content.CanSeek)
                {
                    content.Position = 0;
                }

                await using var copy = new MemoryStream();
                await content.CopyToAsync(copy, cancellationToken);
                UploadedBytes[storageKey] = copy.ToArray();

                return new PublicImageStoredObject(
                    storageKey,
                    BuildPublicUrl(storageKey),
                    contentType,
                    copy.Length);
            }

            public Task<bool> TryDeleteAsync(
                string storageKey,
                CancellationToken cancellationToken = default)
            {
                DeletedKeys.Add(storageKey);
                return Task.FromResult(true);
            }

            public string BuildPublicUrl(string storageKey) =>
                $"https://media.test/{storageKey}";

            public bool IsValidStorageKey(string storageKey) =>
                PublicImageStorageKeyBuilder.IsValidStorageKey(storageKey);
        }
    }
}
