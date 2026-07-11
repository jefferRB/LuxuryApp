using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using LuxuryApp.Services.PublicImages;
using Xunit;

namespace LuxuryApp.Tests.TenantIsolation
{
    public class S3CompatiblePublicImageStorageServiceTests
    {
        [Fact]
        public void BuildPutObjectRequest_UsesCloudflareR2CompatibleFlagsAndContentLength()
        {
            using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });

            var request = S3CompatiblePublicImageStorageService.BuildPutObjectRequest(
                "mi-bucket",
                "tenants/abc/public-page/location/x.webp",
                content,
                "image/webp",
                content.Length);

            // Flags que evitan el trailer STREAMING-AWS4-HMAC-SHA256-PAYLOAD que R2 no soporta.
            Assert.True(request.DisablePayloadSigning);
            Assert.True(request.DisableDefaultChecksumValidation);
            Assert.False(request.UseChunkEncoding);
            Assert.False(request.AutoResetStreamPosition);
            Assert.False(request.AutoCloseStream);

            // Content-Length explicito para forzar un PUT simple (no chunked).
            Assert.Equal(4, request.Headers.ContentLength);

            // Se preserva el pipeline actual.
            Assert.Equal("mi-bucket", request.BucketName);
            Assert.Equal("tenants/abc/public-page/location/x.webp", request.Key);
            Assert.Equal("image/webp", request.ContentType);
            Assert.Equal("public, max-age=31536000, immutable", request.Headers.CacheControl);
            Assert.Same(content, request.InputStream);

            // No se fija checksum explicito (evita interferir con R2).
            Assert.Null(request.ChecksumAlgorithm);
        }

        [Fact]
        public void BuildConfig_UsesR2CompatibleChecksumPathStyleAndAutoRegion()
        {
            var options = new S3StorageOptions
            {
                Endpoint = "https://accountid.r2.cloudflarestorage.com",
                Region = "auto",
                BucketName = "luxurycloud-public-assets-prod"
            };

            var config = S3CompatiblePublicImageStorageService.BuildConfig(options);

            Assert.True(config.ForcePathStyle);
            // El SDK normaliza ServiceURL (puede agregar "/" final).
            Assert.StartsWith("https://accountid.r2.cloudflarestorage.com", config.ServiceURL);
            Assert.Equal("auto", config.AuthenticationRegion);
            // Fix clave v4: no calcular checksum salvo que la operacion lo requiera.
            Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, config.RequestChecksumCalculation);
            Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, config.ResponseChecksumValidation);
            Assert.Null(config.RegionEndpoint);
        }

        [Fact]
        public async Task EnsureSeekableAsync_NonSeekableStream_CopiesToSeekableMemoryStream()
        {
            var payload = Encoding.UTF8.GetBytes("contenido-no-seekable");
            await using var nonSeekable = new NonSeekableStream(payload);

            var (stream, created) = await S3CompatiblePublicImageStorageService.EnsureSeekableAsync(
                nonSeekable,
                CancellationToken.None);

            Assert.True(created);
            Assert.True(stream.CanSeek);
            Assert.Equal(0, stream.Position);
            Assert.Equal(payload.Length, stream.Length);

            using var reader = new MemoryStream();
            await stream.CopyToAsync(reader);
            Assert.Equal(payload, reader.ToArray());

            await stream.DisposeAsync();
        }

        [Fact]
        public async Task EnsureSeekableAsync_SeekableStream_ReusesAndResetsPosition()
        {
            using var seekable = new MemoryStream(new byte[] { 9, 8, 7 });
            seekable.Position = 2;

            var (stream, created) = await S3CompatiblePublicImageStorageService.EnsureSeekableAsync(
                seekable,
                CancellationToken.None);

            Assert.False(created);
            Assert.Same(seekable, stream);
            Assert.Equal(0, stream.Position);
        }

        // Stream forward-only para simular un contenido no seekable (ej. red).
        private sealed class NonSeekableStream : Stream
        {
            private readonly MemoryStream _inner;

            public NonSeekableStream(byte[] data) => _inner = new MemoryStream(data);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
