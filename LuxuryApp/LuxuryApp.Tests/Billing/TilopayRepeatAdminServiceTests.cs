using System.Net;
using System.Text;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Tilopay;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LuxuryApp.Tests.Billing
{
    /// <summary>
    /// Prueba el cliente admin REAL contra respuestas JSON simuladas: parseo tolerante de
    /// id/id_suscriptor y la lógica de resolución 0/1/N por email.
    /// </summary>
    public class TilopayRepeatAdminServiceTests
    {
        [Fact]
        public async Task GetSuscriptorRepeat_ParsesIdSuscriptorField()
        {
            var service = CreateService(
                """[ { "id_suscriptor": "374830", "email": "compra1usuario@gmail.com", "status": "1" } ]""");

            var subscribers = await service.GetSuscriptorRepeatAsync(6119);

            var subscriber = Assert.Single(subscribers);
            Assert.Equal("374830", subscriber.SubscriberId);
            Assert.Equal("compra1usuario@gmail.com", subscriber.Email);
        }

        [Fact]
        public async Task GetSuscriptorRepeat_ParsesPlainIdField()
        {
            var service = CreateService(
                """{ "suscriptores": [ { "id": 991122, "correo": "owner@test.local" } ] }""");

            var subscribers = await service.GetSuscriptorRepeatAsync(6119);

            var subscriber = Assert.Single(subscribers);
            Assert.Equal("991122", subscriber.SubscriberId);
            Assert.Equal("owner@test.local", subscriber.Email);
        }

        [Fact]
        public async Task ResolveSubscriber_SingleMatchByEmail_ReturnsFound()
        {
            var service = CreateService(
                """
                [
                  { "id_suscriptor": "111", "email": "other@test.local" },
                  { "id_suscriptor": "374830", "email": "Compra1Usuario@gmail.com" }
                ]
                """);

            var result = await service.ResolveSubscriberAsync(6119, "compra1usuario@gmail.com");

            Assert.Equal(SubscriberResolutionStatus.Found, result.Status);
            Assert.Equal("374830", result.Subscriber!.SubscriberId);
        }

        [Fact]
        public async Task ResolveSubscriber_NoMatch_ReturnsNotFound()
        {
            var service = CreateService("""[ { "id_suscriptor": "111", "email": "other@test.local" } ]""");

            var result = await service.ResolveSubscriberAsync(6119, "compra1usuario@gmail.com");

            Assert.Equal(SubscriberResolutionStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task ResolveSubscriber_MultipleMatches_ReturnsAmbiguous()
        {
            var service = CreateService(
                """
                [
                  { "id_suscriptor": "111", "email": "dup@test.local" },
                  { "id_suscriptor": "222", "email": "dup@test.local" }
                ]
                """);

            var result = await service.ResolveSubscriberAsync(6119, "dup@test.local");

            Assert.Equal(SubscriberResolutionStatus.Ambiguous, result.Status);
            Assert.Equal(2, result.MatchCount);
        }

        [Fact]
        public async Task ResolveSubscriber_MultipleButSingleActive_ResolvesToActive()
        {
            var service = CreateService(
                """
                [
                  { "id_suscriptor": "111", "email": "dup@test.local", "status": "4" },
                  { "id_suscriptor": "222", "email": "dup@test.local", "status": "1" }
                ]
                """);

            var result = await service.ResolveSubscriberAsync(6119, "dup@test.local");

            // El estado 4 (eliminado) se descarta; queda un único activo.
            Assert.Equal(SubscriberResolutionStatus.Found, result.Status);
            Assert.Equal("222", result.Subscriber!.SubscriberId);
        }

        [Fact]
        public async Task GetRecurrentUrl_ReturnsUrl()
        {
            var service = CreateService("""{ "url": "https://tp.cr/l/recurrent-link" }""");

            var result = await service.GetRecurrentUrlAsync(6119, "owner@test.local");

            Assert.True(result.Succeeded);
            Assert.Equal("https://tp.cr/l/recurrent-link", result.Url);
        }

        [Fact]
        public async Task Disabled_Throws()
        {
            var service = CreateService("[]", enabled: false);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSuscriptorRepeatAsync(6119));
        }

        private static TilopayRepeatAdminService CreateService(string subscribersJson, bool enabled = true)
        {
            var handler = new StubHandler(subscribersJson);
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://app.tilopay.com/") };
            var cache = new MemoryCache(new MemoryCacheOptions());

            return new TilopayRepeatAdminService(
                httpClient,
                cache,
                Options.Create(new OpcionesTilopay
                {
                    ApiUser = "u",
                    ApiPassword = "p",
                    ApiKey = "k",
                    BaseUrl = "https://app.tilopay.com/"
                }),
                Options.Create(new OpcionesTilopayRepeatAdmin
                {
                    Enabled = enabled,
                    ResolveRetryCount = 0
                }),
                NullLogger<TilopayRepeatAdminService>.Instance);
        }

        /// <summary>Responde login con un token y cualquier otra ruta con el JSON de suscriptores.</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _payload;

            public StubHandler(string payload) => _payload = payload;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var body = path.EndsWith("login", StringComparison.OrdinalIgnoreCase)
                    ? """{ "access_token": "fake-token", "expires_in": 3600 }"""
                    : _payload;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
