using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Services.PublicImages
{
    public sealed class S3CompatiblePublicImageStorageService : IPublicImageStorageService
    {
        private const string CacheControlHeader = "public, max-age=31536000, immutable";

        private readonly S3StorageOptions _options;
        private readonly string _publicBaseUrl;

        public S3CompatiblePublicImageStorageService(IOptions<S3StorageOptions> options)
        {
            _options = options.Value;
            _publicBaseUrl = (_options.PublicBaseUrl ?? string.Empty).TrimEnd('/');
        }

        public async Task<PublicImageStoredObject> UploadAsync(
            string storageKey,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (!IsValidStorageKey(storageKey))
            {
                throw new InvalidOperationException("La ruta de almacenamiento de imagen no es valida.");
            }

            if (string.IsNullOrWhiteSpace(_options.Endpoint) ||
                string.IsNullOrWhiteSpace(_options.BucketName) ||
                string.IsNullOrWhiteSpace(_options.AccessKey) ||
                string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("El storage S3-compatible no esta configurado.");
            }

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            using var client = CreateClient();
            var request = BuildPutObjectRequest(_options.BucketName, storageKey, content, contentType);

            await client.PutObjectAsync(request, cancellationToken);

            return new PublicImageStoredObject(
                storageKey,
                BuildPublicUrl(storageKey),
                contentType,
                content.CanSeek ? content.Length : 0);
        }

        public async Task<bool> TryDeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            if (!IsValidStorageKey(storageKey) ||
                string.IsNullOrWhiteSpace(_options.BucketName))
            {
                return false;
            }

            using var client = CreateClient();
            await client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = storageKey
                },
                cancellationToken);

            return true;
        }

        public string BuildPublicUrl(string storageKey)
        {
            if (!IsValidStorageKey(storageKey))
            {
                throw new InvalidOperationException("La ruta de almacenamiento de imagen no es valida.");
            }

            if (string.IsNullOrWhiteSpace(_publicBaseUrl))
            {
                throw new InvalidOperationException("S3Storage:PublicBaseUrl es requerido.");
            }

            return $"{_publicBaseUrl}/{storageKey}";
        }

        public bool IsValidStorageKey(string storageKey) =>
            PublicImageStorageKeyBuilder.IsValidStorageKey(storageKey);

        /// <summary>
        /// Construye el PutObjectRequest compatible con Cloudflare R2. El AWS SDK v4 agrega por
        /// defecto un checksum en streaming (STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER) que R2
        /// no implementa. Desactivamos firma de payload por chunks, el checksum por defecto y el
        /// chunk-encoding para usar un PUT simple (Content-Length + UNSIGNED-PAYLOAD sobre HTTPS).
        /// No se fija ChecksumAlgorithm, ServerSideEncryption ni ACL publica.
        /// </summary>
        internal static PutObjectRequest BuildPutObjectRequest(
            string bucketName,
            string storageKey,
            Stream content,
            string contentType)
        {
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = storageKey,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false
            };
            request.Headers.CacheControl = CacheControlHeader;
            return request;
        }

        private AmazonS3Client CreateClient()
        {
            var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
            var config = new AmazonS3Config
            {
                ServiceURL = _options.Endpoint,
                ForcePathStyle = true,
                // Cloudflare R2 usa "auto" como region de firma SigV4.
                AuthenticationRegion = string.IsNullOrWhiteSpace(_options.Region)
                    ? "auto"
                    : _options.Region
            };

            // Solo mapear a un RegionEndpoint real de AWS cuando NO es R2 ("auto").
            if (!string.Equals(_options.Region, "auto", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_options.Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region);
            }

            return new AmazonS3Client(credentials, config);
        }
    }
}
