using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Services.PublicImages
{
    public sealed class S3CompatiblePublicImageStorageService : IPublicImageStorageService
    {
        private const string CacheControlHeader = "public, max-age=31536000, immutable";

        /// <summary>
        /// Marcador unico para confirmar en logs (journalctl) que el binario desplegado
        /// contiene el modo de compatibilidad con Cloudflare R2 (AWS SDK v4).
        /// </summary>
        internal const string CompatModeMarker = "R2_UPLOAD_COMPAT_MODE_V2_ACTIVE";

        private static readonly string SdkVersion =
            typeof(AmazonS3Client).Assembly.GetName().Version?.ToString() ?? "unknown";

        private readonly S3StorageOptions _options;
        private readonly string _publicBaseUrl;
        private readonly ILogger<S3CompatiblePublicImageStorageService> _logger;

        public S3CompatiblePublicImageStorageService(
            IOptions<S3StorageOptions> options,
            ILogger<S3CompatiblePublicImageStorageService> logger)
        {
            _options = options.Value;
            _publicBaseUrl = (_options.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            _logger = logger;
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

            EnsureConfigured();

            // R2 rechaza el streaming trailer (aws-chunked). Garantizamos un stream seekable con
            // longitud conocida para forzar un PUT simple con Content-Length en vez de chunked.
            var (uploadStream, createdStream) = await EnsureSeekableAsync(content, cancellationToken);
            try
            {
                uploadStream.Position = 0;
                var length = uploadStream.Length;
                if (length <= 0)
                {
                    throw new InvalidOperationException("El contenido de la imagen esta vacio.");
                }

                var config = BuildConfig(_options);
                var request = BuildPutObjectRequest(
                    _options.BucketName!,
                    storageKey,
                    uploadStream,
                    contentType,
                    length);

                LogUploadDiagnostics(config, request, uploadStream, storageKey, contentType, length);

                using var client = new AmazonS3Client(CreateCredentials(), config);
                try
                {
                    await client.PutObjectAsync(request, cancellationToken);
                }
                catch (AmazonS3Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{Marker} R2 upload FAILED provider=S3Compatible statusCode={StatusCode} errorCode={ErrorCode} requestId={RequestId} key={StorageKey} message={Message}",
                        CompatModeMarker,
                        ex.StatusCode,
                        ex.ErrorCode,
                        ex.RequestId,
                        storageKey,
                        ex.Message);
                    throw;
                }

                return new PublicImageStoredObject(
                    storageKey,
                    BuildPublicUrl(storageKey),
                    contentType,
                    length);
            }
            finally
            {
                if (createdStream)
                {
                    await uploadStream.DisposeAsync();
                }
            }
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

            using var client = new AmazonS3Client(CreateCredentials(), BuildConfig(_options));
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
        /// Sube un objeto de diagnostico pequeno a R2 (fuera del arbol de imagenes) usando el mismo
        /// modo de compatibilidad, y devuelve la URL publica. Pensado para un healthcheck manual de
        /// SuperAdmin. No expone secretos. La clave es controlada internamente (no viene del cliente).
        /// </summary>
        public async Task<string> UploadHealthCheckObjectAsync(CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            var storageKey = $"diagnostics/r2-healthcheck-{DateTime.UtcNow:yyyyMMddHHmmssfff}.txt";
            var payload = System.Text.Encoding.UTF8.GetBytes(
                $"r2-healthcheck {DateTime.UtcNow:O} {CompatModeMarker}");

            await using var content = new MemoryStream(payload, writable: false);
            var config = BuildConfig(_options);
            var request = BuildPutObjectRequest(
                _options.BucketName!,
                storageKey,
                content,
                "text/plain; charset=utf-8",
                payload.Length);

            LogUploadDiagnostics(config, request, content, storageKey, request.ContentType, payload.Length);

            using var client = new AmazonS3Client(CreateCredentials(), config);
            try
            {
                await client.PutObjectAsync(request, cancellationToken);
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Marker} R2 healthcheck upload FAILED statusCode={StatusCode} errorCode={ErrorCode} requestId={RequestId} message={Message}",
                    CompatModeMarker,
                    ex.StatusCode,
                    ex.ErrorCode,
                    ex.RequestId,
                    ex.Message);
                throw;
            }

            // La clave de diagnostico esta fuera del patron de imagenes, por eso construimos la URL
            // publica directamente (BuildPublicUrl valida el patron de imagenes).
            return $"{_publicBaseUrl}/{storageKey}";
        }

        /// <summary>
        /// Construye el PutObjectRequest compatible con Cloudflare R2. El AWS SDK v4 agrega por
        /// defecto un checksum en streaming (STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER) que R2 no
        /// implementa. Se desactiva la firma de payload por chunks, el checksum por defecto y el
        /// chunk-encoding, y se fija Content-Length para forzar un PUT simple. No se fija
        /// ChecksumAlgorithm, ServerSideEncryption ni ACL.
        /// </summary>
        internal static PutObjectRequest BuildPutObjectRequest(
            string bucketName,
            string storageKey,
            Stream content,
            string contentType,
            long contentLength)
        {
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = storageKey,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
                AutoResetStreamPosition = false,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false
            };
            request.Headers.CacheControl = CacheControlHeader;
            request.Headers.ContentLength = contentLength;
            return request;
        }

        /// <summary>
        /// Configuracion del cliente S3 para R2. La clave del fix v4 es desactivar el calculo de
        /// checksum a nivel de config (no basta con el request): con WHEN_REQUIRED el SDK no agrega
        /// el checksum CRC que dispara el streaming trailer.
        /// </summary>
        internal static AmazonS3Config BuildConfig(S3StorageOptions options)
        {
            var config = new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                ForcePathStyle = true,
                // Cloudflare R2 usa "auto" como region de firma SigV4.
                AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "auto" : options.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };

            // Solo mapear a un RegionEndpoint real de AWS cuando NO es R2 ("auto").
            if (!string.Equals(options.Region, "auto", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(options.Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
            }

            return config;
        }

        /// <summary>
        /// Garantiza un stream seekable posicionado en 0. Si el contenido no es seekable se copia a
        /// un MemoryStream (indicando que el llamador debe liberarlo).
        /// </summary>
        internal static async Task<(Stream Stream, bool Created)> EnsureSeekableAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            if (content.CanSeek)
            {
                content.Position = 0;
                return (content, false);
            }

            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return (buffer, true);
        }

        private void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(_options.Endpoint) ||
                string.IsNullOrWhiteSpace(_options.BucketName) ||
                string.IsNullOrWhiteSpace(_options.AccessKey) ||
                string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("El storage S3-compatible no esta configurado.");
            }
        }

        private BasicAWSCredentials CreateCredentials() =>
            new(_options.AccessKey, _options.SecretKey);

        private void LogUploadDiagnostics(
            AmazonS3Config config,
            PutObjectRequest request,
            Stream stream,
            string storageKey,
            string contentType,
            long length)
        {
            // Nunca se registran AccessKey/SecretKey, URLs firmadas ni el contenido del archivo.
            _logger.LogInformation(
                "{Marker} provider=S3Compatible sdk={Sdk} endpointHost={EndpointHost} bucket={Bucket} " +
                "region={Region} publicBaseUrl={PublicBaseUrl} key={StorageKey} contentType={ContentType} " +
                "streamType={StreamType} canSeek={CanSeek} length={Length} position={Position} " +
                "forcePathStyle={ForcePathStyle} serviceUrl={ServiceUrl} authRegion={AuthRegion} " +
                "requestChecksumCalculation={ReqChecksum} responseChecksumValidation={RespChecksum} " +
                "disablePayloadSigning={DisablePayloadSigning} disableDefaultChecksumValidation={DisableDefaultChecksum} " +
                "useChunkEncoding={UseChunkEncoding} autoResetStreamPosition={AutoResetStreamPosition}",
                CompatModeMarker,
                SdkVersion,
                ResolveEndpointHost(),
                _options.BucketName,
                _options.Region,
                _publicBaseUrl,
                storageKey,
                contentType,
                stream.GetType().Name,
                stream.CanSeek,
                length,
                stream.CanSeek ? stream.Position : -1,
                config.ForcePathStyle,
                config.ServiceURL,
                config.AuthenticationRegion,
                config.RequestChecksumCalculation,
                config.ResponseChecksumValidation,
                request.DisablePayloadSigning,
                request.DisableDefaultChecksumValidation,
                request.UseChunkEncoding,
                request.AutoResetStreamPosition);
        }

        private string ResolveEndpointHost()
        {
            if (!string.IsNullOrWhiteSpace(_options.Endpoint) &&
                Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            return "(invalid-endpoint)";
        }
    }
}
