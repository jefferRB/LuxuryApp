using System.Net;
using System.Text;
using System.Text.Json;
using LuxuryApp.Models.SaaS;
using LuxuryApp.Services.Tilopay;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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

        // ── Contrato real verificado en producción ──

        // Respuesta real de getSuscriptorRepeat (id numérico, array bajo "suscriptor", status Active).
        private const string RealSuscriptorResponse =
            """
            {
              "type": "success",
              "message": "ok",
              "suscriptor": [
                {
                  "id": 374830,
                  "name": "Jefferson",
                  "lastname": "Rojas",
                  "email": "compra1usuario@gmail.com",
                  "modality": "LC_M_01",
                  "amount": "8000.00",
                  "expire": "2026-08-07",
                  "coupon": "",
                  "status": "Active",
                  "create": "2026-07-07 19:26:00"
                }
              ]
            }
            """;

        [Fact]
        public async Task GetSuscriptorRepeat_SendsExactContract_KeyAndIdAsString()
        {
            var handler = new StubHandler(RealSuscriptorResponse);
            var service = CreateService(handler);

            await service.GetSuscriptorRepeatAsync(6119);

            var request = handler.LastByPath("getSuscriptorRepeat");
            Assert.NotNull(request);
            Assert.Equal("application/json", request!.ContentType);
            Assert.Equal("Bearer", request.AuthScheme);
            Assert.Equal("fake-token", request.AuthParameter);

            using var doc = JsonDocument.Parse(request.Body!);
            var root = doc.RootElement;
            Assert.Equal("k", root.GetProperty("key").GetString());              // key presente = ApiKey
            Assert.Equal(JsonValueKind.String, root.GetProperty("id").ValueKind); // id COMO STRING
            Assert.Equal("6119", root.GetProperty("id").GetString());
            Assert.False(root.TryGetProperty("id_plan", out _));                  // NO id_plan
            Assert.False(root.TryGetProperty("plan_id", out _));                  // NO plan_id
        }

        [Fact]
        public async Task GetSuscriptorRepeat_ParsesRealSuscriptorArray()
        {
            var service = CreateService(new StubHandler(RealSuscriptorResponse));

            var subscribers = await service.GetSuscriptorRepeatAsync(6119);

            var subscriber = Assert.Single(subscribers);
            Assert.Equal("374830", subscriber.SubscriberId);           // item.id
            Assert.Equal("compra1usuario@gmail.com", subscriber.Email);
            Assert.Equal("Active", subscriber.Status);
        }

        [Fact]
        public async Task ResolveSubscriber_RealResponse_FindsEmailCaseInsensitive()
        {
            var service = CreateService(new StubHandler(RealSuscriptorResponse));

            // El correo consultado en mayúsculas distintas debe resolver igual (case-insensitive).
            var result = await service.ResolveSubscriberAsync(6119, "Compra1Usuario@GMAIL.com");

            Assert.Equal(SubscriberResolutionStatus.Found, result.Status);
            Assert.Equal("374830", result.Subscriber!.SubscriberId);
        }

        [Fact]
        public async Task GetSuscriptorRepeat_On400_LogsSanitizedBody_AndResolveReturnsError()
        {
            // TiloPay responde 400 con un body que incluye datos sensibles: no deben loguearse en claro.
            const string errorBody =
                """{ "type": "error", "message": "campo id requerido", "email": "compra1usuario@gmail.com", "key": "SECRET-KEY" }""";
            var handler = new StubHandler(defaultBody: "[]");
            handler.SetResponse("getSuscriptorRepeat", HttpStatusCode.BadRequest, errorBody);

            var logger = new CapturingLogger();
            var service = CreateService(handler, logger);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSuscriptorRepeatAsync(6119));

            var logged = string.Join("\n", logger.Messages);
            Assert.Contains("campo id requerido", logged, StringComparison.Ordinal); // mensaje útil sí
            Assert.DoesNotContain("SECRET-KEY", logged, StringComparison.Ordinal);   // key redactada
            Assert.DoesNotContain("compra1usuario@gmail.com", logged, StringComparison.Ordinal); // email redactado

            // Resolución sobre un 400 no lanza hacia el caller: devuelve Error (falla-cerrado aguas arriba).
            var resolution = await service.ResolveSubscriberAsync(6119, "compra1usuario@gmail.com");
            Assert.Equal(SubscriberResolutionStatus.Error, resolution.Status);
        }

        private static TilopayRepeatAdminService CreateService(string subscribersJson, bool enabled = true) =>
            CreateService(new StubHandler(subscribersJson), enabled: enabled);

        private static TilopayRepeatAdminService CreateService(
            StubHandler handler,
            ILogger<TilopayRepeatAdminService>? logger = null,
            bool enabled = true)
        {
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
                logger ?? NullLogger<TilopayRepeatAdminService>.Instance);
        }

        internal sealed record CapturedRequest(string? Body, string? ContentType, string? AuthScheme, string? AuthParameter);

        /// <summary>
        /// Responde login con un token; para otras rutas devuelve el body por defecto o uno
        /// configurado por path (status + body). Captura cada request para aserciones de contrato.
        /// </summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _defaultBody;
            private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _byPath = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<(string Path, CapturedRequest Request)> _captured = new();

            public StubHandler(string defaultBody) => _defaultBody = defaultBody;

            public void SetResponse(string pathContains, HttpStatusCode status, string body) =>
                _byPath[pathContains] = (status, body);

            public CapturedRequest? LastByPath(string pathContains) =>
                _captured.LastOrDefault(entry => entry.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase)).Request;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                var contentType = request.Content?.Headers.ContentType?.MediaType;

                _captured.Add((path, new CapturedRequest(
                    body,
                    contentType,
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter)));

                if (path.EndsWith("login", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(HttpStatusCode.OK, """{ "access_token": "fake-token", "expires_in": 3600 }""");
                }

                foreach (var (pathContains, configured) in _byPath)
                {
                    if (path.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
                    {
                        return Json(configured.Status, configured.Body);
                    }
                }

                return Json(HttpStatusCode.OK, _defaultBody);
            }

            private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
                new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private sealed class CapturingLogger : ILogger<TilopayRepeatAdminService>
        {
            public List<string> Messages { get; } = new();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                Messages.Add(formatter(state, exception));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
