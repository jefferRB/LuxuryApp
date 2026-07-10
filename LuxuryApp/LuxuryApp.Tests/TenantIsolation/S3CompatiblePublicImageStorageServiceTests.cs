using System.IO;
using LuxuryApp.Services.PublicImages;
using Xunit;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class S3CompatiblePublicImageStorageServiceTests
    {
        [Fact]
        public void BuildPutObjectRequest_UsesCloudflareR2CompatibleFlags()
        {
            using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });

            var request = S3CompatiblePublicImageStorageService.BuildPutObjectRequest(
                "mi-bucket",
                "tenants/abc/public-page/location/x.webp",
                content,
                "image/webp");

            // Flags que evitan el trailer STREAMING-AWS4-HMAC-SHA256-PAYLOAD que R2 no soporta.
            Assert.True(request.DisablePayloadSigning);
            Assert.True(request.DisableDefaultChecksumValidation);
            Assert.False(request.UseChunkEncoding);

            // Se preserva el pipeline actual.
            Assert.Equal("mi-bucket", request.BucketName);
            Assert.Equal("tenants/abc/public-page/location/x.webp", request.Key);
            Assert.Equal("image/webp", request.ContentType);
            Assert.Equal("public, max-age=31536000, immutable", request.Headers.CacheControl);
            Assert.Same(content, request.InputStream);
            Assert.False(request.AutoCloseStream);

            // No se fija checksum explicito (evita interferir con R2).
            Assert.Null(request.ChecksumAlgorithm);
        }
    }
}
