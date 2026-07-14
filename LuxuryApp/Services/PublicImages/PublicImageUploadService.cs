using LuxuryApp.Models.PublicPages;
using LuxuryApp.Services.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProyectoIdentity.Datos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuxuryApp.Services.PublicImages
{
    public sealed class PublicImageUploadService : IPublicImageUploadService
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private static readonly string[] DangerousExtensions =
        {
            ".svg",
            ".gif",
            ".pdf",
            ".html",
            ".htm",
            ".js",
            ".zip",
            ".exe",
            ".cmd",
            ".bat",
            ".ps1"
        };

        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly IPublicImageStorageService _storage;
        private readonly IPublicAssetQuotaService _quotaService;
        private readonly IUploadedFileSecurityScanner _securityScanner;
        private readonly PublicImageOptions _options;
        private readonly ILogger<PublicImageUploadService> _logger;

        public PublicImageUploadService(
            ApplicationDbContext context,
            ITenantProvider tenantProvider,
            IPublicImageStorageService storage,
            IPublicAssetQuotaService quotaService,
            IUploadedFileSecurityScanner securityScanner,
            IOptions<PublicImageOptions> options,
            ILogger<PublicImageUploadService> logger)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _storage = storage;
            _quotaService = quotaService;
            _securityScanner = securityScanner;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<TenantPublicAsset> UploadPublicPageAssetAsync(
            TenantPublicAssetType assetType,
            IFormFile? file,
            string? userId,
            CancellationToken cancellationToken = default,
            PublicImageCropRequest? crop = null)
        {
            EnsurePublicPageAssetType(assetType);
            var tenantId = ResolveTenantId();
            var page = await GetOrCreatePageAsync(cancellationToken);
            var processed = await ProcessImageAsync(file, assetType, crop, cancellationToken);
            var replacingAsset = IsSingleton(assetType)
                ? await FindActiveSingletonAsync(assetType, null, cancellationToken)
                : null;

            if (assetType == TenantPublicAssetType.BusinessGallery)
            {
                await EnsureGalleryLimitAsync(
                    tenantId,
                    assetType,
                    null,
                    _options.MaxBusinessGalleryImages,
                    cancellationToken);
            }

            try
            {
                return await SaveProcessedAssetAsync(
                    tenantId,
                    page.Id,
                    null,
                    assetType,
                    processed,
                    replacingAsset,
                    userId,
                    cancellationToken);
            }
            finally
            {
                processed.Content.Dispose();
            }
        }

        public async Task<TenantPublicAsset> UploadServiceAssetAsync(
            TenantPublicAssetType assetType,
            int serviceId,
            IFormFile? file,
            string? userId,
            CancellationToken cancellationToken = default,
            PublicImageCropRequest? crop = null)
        {
            // Solo se admite una imagen principal por servicio. La galeria/trabajos por servicio
            // fue retirada del producto.
            if (assetType is not TenantPublicAssetType.ServiceMain)
            {
                throw new PublicImageUploadException("Tipo de imagen de servicio invalido.");
            }

            var tenantId = ResolveTenantId();
            await EnsureServiceBelongsToCurrentTenantAsync(serviceId, cancellationToken);
            var processed = await ProcessImageAsync(file, assetType, crop, cancellationToken);
            var replacingAsset = await FindActiveSingletonAsync(assetType, serviceId, cancellationToken);

            try
            {
                return await SaveProcessedAssetAsync(
                    tenantId,
                    null,
                    serviceId,
                    assetType,
                    processed,
                    replacingAsset,
                    userId,
                    cancellationToken);
            }
            finally
            {
                processed.Content.Dispose();
            }
        }

        public async Task RemovePublicPageSingletonAsync(
            TenantPublicAssetType assetType,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            if (assetType is not TenantPublicAssetType.Logo
                and not TenantPublicAssetType.Cover
                and not TenantPublicAssetType.Location)
            {
                throw new PublicImageUploadException("Tipo de imagen invalido.");
            }

            var asset = await FindActiveSingletonAsync(assetType, null, cancellationToken);
            if (asset is not null)
            {
                await SoftDeleteAndRemoveStorageAsync(asset, userId, cancellationToken);
            }
        }

        public async Task RemoveServiceMainImageAsync(
            int serviceId,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            await EnsureServiceBelongsToCurrentTenantAsync(serviceId, cancellationToken);
            var asset = await FindActiveSingletonAsync(
                TenantPublicAssetType.ServiceMain,
                serviceId,
                cancellationToken);

            if (asset is not null)
            {
                await SoftDeleteAndRemoveStorageAsync(asset, userId, cancellationToken);
            }
        }

        public async Task RemoveAssetAsync(
            Guid assetId,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            if (assetId == Guid.Empty)
            {
                throw new PublicImageUploadException("Imagen invalida.");
            }

            var asset = await _context.TenantPublicAssets
                .FirstOrDefaultAsync(
                    item => item.Id == assetId &&
                            item.IsActive &&
                            item.DeletedAtUtc == null,
                    cancellationToken);

            if (asset is null)
            {
                return;
            }

            await SoftDeleteAndRemoveStorageAsync(asset, userId, cancellationToken);
        }

        private async Task<TenantPublicAsset> SaveProcessedAssetAsync(
            Guid tenantId,
            Guid? tenantPublicPageId,
            int? serviceId,
            TenantPublicAssetType assetType,
            ProcessedPublicImage processed,
            TenantPublicAsset? replacingAsset,
            string? userId,
            CancellationToken cancellationToken)
        {
            var storageKey = PublicImageStorageKeyBuilder.Build(tenantId, assetType, serviceId);
            var uploaded = false;

            await _quotaService.EnsureCanUploadAsync(
                tenantId,
                processed.SizeBytes,
                replacingAsset?.Id,
                cancellationToken);

            try
            {
                await _storage.UploadAsync(
                    storageKey,
                    processed.Content,
                    _options.OutputContentType,
                    cancellationToken);
                uploaded = true;

                var now = DateTime.UtcNow;
                if (replacingAsset is not null)
                {
                    replacingAsset.IsActive = false;
                    replacingAsset.DeletedAtUtc = now;
                    replacingAsset.UpdatedAtUtc = now;
                }

                var asset = new TenantPublicAsset
                {
                    TenantPublicPageId = tenantPublicPageId,
                    ServicioId = serviceId,
                    AssetType = assetType,
                    StorageKey = storageKey,
                    PublicUrl = _storage.BuildPublicUrl(storageKey),
                    ContentType = _options.OutputContentType,
                    SizeBytes = processed.SizeBytes,
                    Width = processed.Width,
                    Height = processed.Height,
                    OriginalFileName = processed.SafeOriginalFileName,
                    SortOrder = await ResolveNextSortOrderAsync(
                        assetType,
                        serviceId,
                        replacingAsset,
                        cancellationToken),
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                _context.TenantPublicAssets.Add(asset);
                await _context.SaveChangesAsync(cancellationToken);

                if (replacingAsset is not null)
                {
                    await TryDeleteStorageAsync(replacingAsset.StorageKey, cancellationToken);
                }

                return asset;
            }
            catch
            {
                if (uploaded)
                {
                    await TryDeleteStorageAsync(storageKey, cancellationToken);
                }

                throw;
            }
        }

        private async Task SoftDeleteAndRemoveStorageAsync(
            TenantPublicAsset asset,
            string? userId,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            asset.IsActive = false;
            asset.DeletedAtUtc = now;
            asset.UpdatedAtUtc = now;

            await _context.SaveChangesAsync(cancellationToken);
            await TryDeleteStorageAsync(asset.StorageKey, cancellationToken);
        }

        private async Task<ProcessedPublicImage> ProcessImageAsync(
            IFormFile? file,
            TenantPublicAssetType assetType,
            PublicImageCropRequest? crop,
            CancellationToken cancellationToken)
        {
            ValidateFileBasics(file, assetType);

            await using var raw = new MemoryStream();
            await using (var uploadStream = file!.OpenReadStream())
            {
                await uploadStream.CopyToAsync(raw, cancellationToken);
            }

            if (raw.Length == 0)
            {
                throw new PublicImageUploadException("Selecciona una imagen valida.");
            }

            if (!HasAllowedMagicBytes(raw))
            {
                throw new PublicImageUploadException("El archivo no es una imagen JPG, PNG o WEBP valida.");
            }

            raw.Position = 0;
            await _securityScanner.ScanAsync(
                raw,
                file.FileName,
                file.ContentType,
                cancellationToken);

            raw.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(raw, cancellationToken);
            var decodedPixels = (long)image.Width * image.Height;
            if (decodedPixels <= 0 || decodedPixels > _options.MaxDecodedPixels)
            {
                throw new PublicImageUploadException("La imagen tiene dimensiones demasiado grandes.");
            }

            var (maxWidth, maxHeight) = ResolveDimensions(assetType);
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            var fitMode = crop?.ResolveFitMode() ?? PublicImageFitMode.Cover;
            var targetAspect = ResolveTargetAspect(assetType, crop);

            MemoryStream output;
            int outputWidth;
            int outputHeight;

            switch (fitMode)
            {
                case PublicImageFitMode.Original:
                    ResizeToMax(image, maxWidth, maxHeight);
                    (output, outputWidth, outputHeight) = await EncodeWebpAsync(image, assetType, cancellationToken);
                    break;

                case PublicImageFitMode.Contain:
                    // Fondo neutro/transparente (util para logos: no se recorta ni se difumina).
                    (output, outputWidth, outputHeight) = await ComposePaddedWebpAsync(
                        image, targetAspect, maxWidth, maxHeight, assetType, blurBackground: false, cancellationToken);
                    break;

                case PublicImageFitMode.Padded:
                    // Fondo blur de la misma foto (portada/fotos verticales).
                    (output, outputWidth, outputHeight) = await ComposePaddedWebpAsync(
                        image, targetAspect, maxWidth, maxHeight, assetType, blurBackground: true, cancellationToken);
                    break;

                default: // Cover (compatibilidad con el comportamiento historico)
                    var cropRectangle = ResolveCropRectangle(image.Width, image.Height, crop, targetAspect);
                    image.Mutate(context => context.Crop(cropRectangle));
                    ResizeToMax(image, maxWidth, maxHeight);
                    (output, outputWidth, outputHeight) = await EncodeWebpAsync(image, assetType, cancellationToken);
                    break;
            }

            return new ProcessedPublicImage(
                output,
                output.Length,
                outputWidth,
                outputHeight,
                SanitizeOriginalFileName(file.FileName));
        }

        private static void ResizeToMax(Image image, int maxWidth, int maxHeight)
        {
            if (image.Width > maxWidth || image.Height > maxHeight)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidth, maxHeight),
                    Mode = ResizeMode.Max
                }));
            }
        }

        private async Task<(MemoryStream Output, int Width, int Height)> EncodeWebpAsync(
            Image image,
            TenantPublicAssetType assetType,
            CancellationToken cancellationToken)
        {
            var output = new MemoryStream();
            await image.SaveAsWebpAsync(
                output,
                new WebpEncoder { Quality = ResolveQuality(assetType) },
                cancellationToken);
            output.Position = 0;
            return (output, image.Width, image.Height);
        }

        /// <summary>
        /// Compone la imagen COMPLETA (sin recortar) centrada sobre un canvas del aspecto objetivo,
        /// rellenando los margenes con una copia ampliada y desenfocada de la misma imagen (blur).
        /// </summary>
        private async Task<(MemoryStream Output, int Width, int Height)> ComposePaddedWebpAsync(
            Image<Rgba32> image,
            double targetAspect,
            int maxWidth,
            int maxHeight,
            TenantPublicAssetType assetType,
            bool blurBackground,
            CancellationToken cancellationToken)
        {
            var (canvasWidth, canvasHeight) = ResolveCanvasSize(targetAspect, maxWidth, maxHeight);

            // Primer plano: imagen completa contenida dentro del canvas (sin recorte).
            using var foreground = image.Clone(context => context
                .Resize(new ResizeOptions
                {
                    Size = new Size(canvasWidth, canvasHeight),
                    Mode = ResizeMode.Max
                }));

            var offsetX = Math.Max(0, (canvasWidth - foreground.Width) / 2);
            var offsetY = Math.Max(0, (canvasHeight - foreground.Height) / 2);

            // Canvas transparente por defecto (Contain: bueno para logos, sin recorte ni blur).
            using var canvas = new Image<Rgba32>(canvasWidth, canvasHeight);

            if (blurBackground)
            {
                // Fondo blur: copia que cubre todo el canvas (recorta) + desenfoque + leve oscurecido.
                using var background = image.Clone(context => context
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(canvasWidth, canvasHeight),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    })
                    .GaussianBlur(Math.Max(8f, canvasWidth / 40f))
                    .Brightness(0.9f));

                canvas.Mutate(context => context
                    .DrawImage(background, new Point(0, 0), 1f)
                    .DrawImage(foreground, new Point(offsetX, offsetY), 1f));
            }
            else
            {
                canvas.Mutate(context => context
                    .DrawImage(foreground, new Point(offsetX, offsetY), 1f));
            }

            return await EncodeWebpAsync(canvas, assetType, cancellationToken);
        }

        /// <summary>Canvas del aspecto objetivo, maximizado dentro de la caja (maxWidth x maxHeight).</summary>
        private static (int Width, int Height) ResolveCanvasSize(double targetAspect, int maxWidth, int maxHeight)
        {
            var boxAspect = (double)maxWidth / maxHeight;
            if (targetAspect >= boxAspect)
            {
                var height = Math.Max(1, (int)Math.Round(maxWidth / targetAspect));
                return (maxWidth, Math.Min(height, maxHeight));
            }

            var width = Math.Max(1, (int)Math.Round(maxHeight * targetAspect));
            return (Math.Min(width, maxWidth), maxHeight);
        }

        /// <summary>
        /// Resuelve el aspecto objetivo. Usa el del request si es sano; si viene un valor absurdo lo
        /// rechaza; si no viene, cae al default del tipo.
        /// </summary>
        private static double ResolveTargetAspect(TenantPublicAssetType assetType, PublicImageCropRequest? crop)
        {
            if (crop?.TargetAspectRatio is double requested)
            {
                if (!IsSaneAspect(requested))
                {
                    throw new PublicImageUploadException("El formato de imagen solicitado no es valido.");
                }

                return requested;
            }

            return ResolveTargetAspectRatio(assetType);
        }

        private static bool IsSaneAspect(double aspect) =>
            !double.IsNaN(aspect) && !double.IsInfinity(aspect) && aspect >= 0.4 && aspect <= 3.0;

        private static Rectangle ResolveCropRectangle(
            int imageWidth,
            int imageHeight,
            PublicImageCropRequest? crop,
            double targetAspect)
        {
            if (crop is not null && IsValidCrop(imageWidth, imageHeight, crop))
            {
                return new Rectangle(
                    crop.CropX!.Value,
                    crop.CropY!.Value,
                    crop.CropWidth!.Value,
                    crop.CropHeight!.Value);
            }

            return BuildCenteredCrop(imageWidth, imageHeight, targetAspect);
        }

        private static bool IsValidCrop(
            int imageWidth,
            int imageHeight,
            PublicImageCropRequest crop)
        {
            if (!crop.HasCrop ||
                crop.CropX!.Value < 0 ||
                crop.CropY!.Value < 0 ||
                crop.CropWidth!.Value <= 0 ||
                crop.CropHeight!.Value <= 0)
            {
                return false;
            }

            var right = (long)crop.CropX.Value + crop.CropWidth.Value;
            var bottom = (long)crop.CropY.Value + crop.CropHeight.Value;
            return right <= imageWidth && bottom <= imageHeight;
        }

        private static Rectangle BuildCenteredCrop(
            int imageWidth,
            int imageHeight,
            double targetAspectRatio)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return Rectangle.Empty;
            }

            var currentAspectRatio = (double)imageWidth / imageHeight;
            var cropWidth = imageWidth;
            var cropHeight = imageHeight;

            if (currentAspectRatio > targetAspectRatio)
            {
                cropWidth = Math.Max(1, (int)Math.Round(imageHeight * targetAspectRatio));
            }
            else if (currentAspectRatio < targetAspectRatio)
            {
                cropHeight = Math.Max(1, (int)Math.Round(imageWidth / targetAspectRatio));
            }

            var cropX = Math.Max(0, (imageWidth - cropWidth) / 2);
            var cropY = Math.Max(0, (imageHeight - cropHeight) / 2);
            return new Rectangle(cropX, cropY, cropWidth, cropHeight);
        }

        private void ValidateFileBasics(IFormFile? file, TenantPublicAssetType assetType)
        {
            if (file is null || file.Length <= 0)
            {
                throw new PublicImageUploadException("Selecciona una imagen valida.");
            }

            var maxBytes = ResolveMaxBytes(assetType);
            if (file.Length > maxBytes)
            {
                throw new PublicImageUploadException(
                    $"La imagen supera el tamano maximo permitido ({FormatBytes(maxBytes)}).");
            }

            if (!_options.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new PublicImageUploadException("Formato no permitido. Usa JPG, PNG o WEBP.");
            }

            var safeName = Path.GetFileName(file.FileName ?? string.Empty);
            var extension = Path.GetExtension(safeName);
            var lowerName = safeName.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension) ||
                !AllowedExtensions.Contains(extension) ||
                DangerousExtensions.Any(lowerName.Contains))
            {
                throw new PublicImageUploadException("Extension no permitida. Usa JPG, PNG o WEBP.");
            }
        }

        private async Task<TenantPublicPage> GetOrCreatePageAsync(CancellationToken cancellationToken)
        {
            var page = await _context.TenantPublicPages
                .FirstOrDefaultAsync(cancellationToken);

            if (page is not null)
            {
                return page;
            }

            page = new TenantPublicPage
            {
                ShowServices = true,
                ShowPrices = true,
                ShowLocation = true,
                ShowWhatsAppButton = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.TenantPublicPages.Add(page);
            return page;
        }

        private async Task<TenantPublicAsset?> FindActiveSingletonAsync(
            TenantPublicAssetType assetType,
            int? serviceId,
            CancellationToken cancellationToken)
        {
            return await _context.TenantPublicAssets
                .FirstOrDefaultAsync(
                    asset => asset.AssetType == assetType &&
                             asset.ServicioId == serviceId &&
                             asset.IsActive &&
                             asset.DeletedAtUtc == null,
                    cancellationToken);
        }

        private async Task EnsureGalleryLimitAsync(
            Guid tenantId,
            TenantPublicAssetType assetType,
            int? serviceId,
            int maxImages,
            CancellationToken cancellationToken)
        {
            var count = await _context.TenantPublicAssets
                .AsNoTracking()
                .CountAsync(
                    asset => asset.TenantId == tenantId &&
                             asset.AssetType == assetType &&
                             asset.ServicioId == serviceId &&
                             asset.IsActive &&
                             asset.DeletedAtUtc == null,
                    cancellationToken);

            if (count >= maxImages)
            {
                throw new PublicImageUploadException(
                    $"Ya alcanzaste el maximo de {maxImages} imagenes en esta galeria.");
            }
        }

        private async Task<int> ResolveNextSortOrderAsync(
            TenantPublicAssetType assetType,
            int? serviceId,
            TenantPublicAsset? replacingAsset,
            CancellationToken cancellationToken)
        {
            if (replacingAsset is not null)
            {
                return replacingAsset.SortOrder;
            }

            var max = await _context.TenantPublicAssets
                .AsNoTracking()
                .Where(asset => asset.AssetType == assetType &&
                                asset.ServicioId == serviceId)
                .MaxAsync(asset => (int?)asset.SortOrder, cancellationToken);

            return (max ?? 0) + 1;
        }

        private async Task EnsureServiceBelongsToCurrentTenantAsync(
            int serviceId,
            CancellationToken cancellationToken)
        {
            if (serviceId <= 0 ||
                !await _context.Servicios
                    .AsNoTracking()
                    .AnyAsync(service => service.Id == serviceId && service.Activo, cancellationToken))
            {
                throw new PublicImageUploadException("El servicio seleccionado no existe o no pertenece al negocio actual.");
            }
        }

        private Guid ResolveTenantId()
        {
            if (!_tenantProvider.HasTenant())
            {
                throw new PublicImageUploadException("No se pudo determinar el negocio actual.");
            }

            var tenantId = _tenantProvider.GetTenantId();
            if (tenantId == Guid.Empty)
            {
                throw new PublicImageUploadException("No se pudo determinar el negocio actual.");
            }

            return tenantId;
        }

        private static void EnsurePublicPageAssetType(TenantPublicAssetType assetType)
        {
            if (assetType is not TenantPublicAssetType.Logo
                and not TenantPublicAssetType.Cover
                and not TenantPublicAssetType.BusinessGallery
                and not TenantPublicAssetType.Location)
            {
                throw new PublicImageUploadException("Tipo de imagen de pagina publica invalido.");
            }
        }

        private static bool IsSingleton(TenantPublicAssetType assetType) =>
            assetType is TenantPublicAssetType.Logo
                or TenantPublicAssetType.Cover
                or TenantPublicAssetType.Location
                or TenantPublicAssetType.ServiceMain;

        private long ResolveMaxBytes(TenantPublicAssetType assetType) =>
            assetType switch
            {
                TenantPublicAssetType.Logo => _options.MaxLogoBytes,
                TenantPublicAssetType.Cover => _options.MaxCoverBytes,
                TenantPublicAssetType.Location => _options.MaxCoverBytes,
                TenantPublicAssetType.BusinessGallery => _options.MaxGalleryImageBytes,
                TenantPublicAssetType.ServiceMain => _options.MaxServiceImageBytes,
                TenantPublicAssetType.ServiceGallery => _options.MaxServiceImageBytes,
                _ => _options.MaxGalleryImageBytes
            };

        private (int Width, int Height) ResolveDimensions(TenantPublicAssetType assetType) =>
            assetType switch
            {
                TenantPublicAssetType.Logo => (_options.LogoMaxWidth, _options.LogoMaxHeight),
                TenantPublicAssetType.Cover => (_options.CoverMaxWidth, _options.CoverMaxHeight),
                TenantPublicAssetType.Location => (_options.LocationMaxWidth, _options.LocationMaxHeight),
                TenantPublicAssetType.ServiceMain => (_options.ServiceImageMaxWidth, _options.ServiceImageMaxHeight),
                TenantPublicAssetType.ServiceGallery => (_options.ServiceImageMaxWidth, _options.ServiceImageMaxHeight),
                _ => (_options.GalleryMaxWidth, _options.GalleryMaxHeight)
            };

        private static double ResolveTargetAspectRatio(TenantPublicAssetType assetType) =>
            assetType switch
            {
                TenantPublicAssetType.Logo => 1d,
                TenantPublicAssetType.Cover => 16d / 9d,
                TenantPublicAssetType.Location => 4d / 3d,
                TenantPublicAssetType.ServiceMain => 4d / 3d,
                TenantPublicAssetType.BusinessGallery => 4d / 5d,
                TenantPublicAssetType.ServiceGallery => 4d / 5d,
                _ => 4d / 3d
            };

        private static int ResolveQuality(TenantPublicAssetType assetType) =>
            assetType == TenantPublicAssetType.Logo ? 88 : 82;

        private static bool HasAllowedMagicBytes(Stream stream)
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[12];
            var read = stream.Read(header);
            stream.Position = 0;

            if (read >= 3 &&
                header[0] == 0xFF &&
                header[1] == 0xD8 &&
                header[2] == 0xFF)
            {
                return true;
            }

            if (read >= 8 &&
                header[0] == 0x89 &&
                header[1] == 0x50 &&
                header[2] == 0x4E &&
                header[3] == 0x47 &&
                header[4] == 0x0D &&
                header[5] == 0x0A &&
                header[6] == 0x1A &&
                header[7] == 0x0A)
            {
                return true;
            }

            return read >= 12 &&
                   header[0] == 0x52 &&
                   header[1] == 0x49 &&
                   header[2] == 0x46 &&
                   header[3] == 0x46 &&
                   header[8] == 0x57 &&
                   header[9] == 0x45 &&
                   header[10] == 0x42 &&
                   header[11] == 0x50;
        }

        private static string? SanitizeOriginalFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            var safe = Path.GetFileName(fileName)
                .Replace('<', '_')
                .Replace('>', '_')
                .Replace('"', '_')
                .Replace('\'', '_')
                .Replace('`', '_')
                .Trim();

            return safe.Length == 0 ? null : safe[..Math.Min(180, safe.Length)];
        }

        private async Task TryDeleteStorageAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            try
            {
                await _storage.TryDeleteAsync(storageKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo borrar el asset publico {StorageKey}.", storageKey);
            }
        }

        private static string FormatBytes(long bytes)
        {
            var mb = bytes / 1024m / 1024m;
            return $"{mb:0.#} MB";
        }

        private sealed record ProcessedPublicImage(
            MemoryStream Content,
            long SizeBytes,
            int Width,
            int Height,
            string? SafeOriginalFileName);
    }
}
