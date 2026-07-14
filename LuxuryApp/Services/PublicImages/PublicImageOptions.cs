using Microsoft.Extensions.Options;

namespace LuxuryApp.Services.PublicImages
{
    public static class PublicImageProviders
    {
        public const string Local = "Local";
        public const string S3Compatible = "S3Compatible";
    }

    public sealed class PublicImageOptions
    {
        public const string SectionName = "PublicImages";

        public string Provider { get; set; } = PublicImageProviders.Local;

        public string? CdnBaseUrl { get; set; }

        public long MaxTenantPublicImageBytes { get; set; } = 25L * 1024 * 1024;

        public long MaxLogoBytes { get; set; } = 4L * 1024 * 1024;

        public long MaxCoverBytes { get; set; } = 8L * 1024 * 1024;

        public long MaxGalleryImageBytes { get; set; } = 6L * 1024 * 1024;

        public long MaxServiceImageBytes { get; set; } = 6L * 1024 * 1024;

        public int MaxBusinessGalleryImages { get; set; } = 12;

        public int MaxServiceGalleryImages { get; set; } = 6;

        public string[] AllowedContentTypes { get; set; } =
        {
            "image/jpeg",
            "image/jpg",
            "image/png",
            "image/webp"
        };

        public string OutputContentType { get; set; } = "image/webp";

        public int LogoMaxWidth { get; set; } = 512;

        public int LogoMaxHeight { get; set; } = 512;

        public int CoverMaxWidth { get; set; } = 1920;

        public int CoverMaxHeight { get; set; } = 1080;

        public int GalleryMaxWidth { get; set; } = 1200;

        public int GalleryMaxHeight { get; set; } = 1500;

        // Caja mas alta para admitir fotos verticales de celular sin encogerlas.
        public int ServiceImageMaxWidth { get; set; } = 1080;

        public int ServiceImageMaxHeight { get; set; } = 1350;

        // Ubicacion admite vertical u horizontal: caja cuadrada amplia.
        public int LocationMaxWidth { get; set; } = 1400;

        public int LocationMaxHeight { get; set; } = 1400;

        public long MaxDecodedPixels { get; set; } = 24_000_000;
    }

    public sealed class S3StorageOptions
    {
        public const string SectionName = "S3Storage";

        public string? Endpoint { get; set; }

        public string Region { get; set; } = "auto";

        public string? BucketName { get; set; }

        public string? AccessKey { get; set; }

        public string? SecretKey { get; set; }

        public string? PublicBaseUrl { get; set; }
    }

    public sealed class S3StorageOptionsValidator : IValidateOptions<S3StorageOptions>
    {
        private readonly IOptions<PublicImageOptions> _publicImageOptions;

        public S3StorageOptionsValidator(IOptions<PublicImageOptions> publicImageOptions)
        {
            _publicImageOptions = publicImageOptions;
        }

        public ValidateOptionsResult Validate(string? name, S3StorageOptions options)
        {
            if (!string.Equals(
                    _publicImageOptions.Value.Provider,
                    PublicImageProviders.S3Compatible,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ValidateOptionsResult.Success;
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Endpoint)) missing.Add(nameof(options.Endpoint));
            if (string.IsNullOrWhiteSpace(options.Region)) missing.Add(nameof(options.Region));
            if (string.IsNullOrWhiteSpace(options.BucketName)) missing.Add(nameof(options.BucketName));
            if (string.IsNullOrWhiteSpace(options.AccessKey)) missing.Add(nameof(options.AccessKey));
            if (string.IsNullOrWhiteSpace(options.SecretKey)) missing.Add(nameof(options.SecretKey));
            if (string.IsNullOrWhiteSpace(options.PublicBaseUrl)) missing.Add(nameof(options.PublicBaseUrl));

            return missing.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"S3Storage requiere configuracion completa para PublicImages:Provider=S3Compatible. Campos faltantes: {string.Join(", ", missing)}.");
        }
    }
}
