using System.Net;
using System.Text;
using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class AvitoApiTests
{
    [Fact]
    public void SettingsStore_ProtectsCredentialsAndCanDeleteThem()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "avito-api-settings.dat");
        var store = new AvitoApiSettingsStore(path, new XorProtector());
        var settings = AvitoApiSettings.Create("client-test-id", "client-test-secret");

        store.Save(settings);
        var restored = store.Load();

        Assert.Equal(settings, restored);
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain(settings.ClientId, raw);
        Assert.DoesNotContain(settings.ClientSecret, raw);

        store.Delete();
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task RatingsService_UsesOfficialEndpointsAndImportsAllPages()
    {
        var firstTimestamp = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var secondTimestamp = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"access_token":"temporary-token","expires_in":86400,"token_type":"Bearer"}"""),
            Json(HttpStatusCode.OK, """{"isEnabled":true,"rating":{"score":4.9,"reviewsCount":2,"reviewsWithScoreCount":2}}"""),
            Json(HttpStatusCode.OK, $$$"""
                {"total":2,"reviews":[{"id":101,"score":5,"createdAt":{{{firstTimestamp}}},"text":"Очень внимательный мастер.","sender":{"name":"Анна"}}]}
                """),
            Json(HttpStatusCode.OK, $$$"""
                {"total":2,"reviews":[{"id":102,"score":4,"createdAt":{{{secondTimestamp}}},"text":"Всё хорошо.","sender":null}]}
                """));
        using var client = new HttpClient(handler);
        var service = new AvitoRatingsService(client, new Uri("https://official.test/"));

        var result = await service.GetReviewsAsync(AvitoApiSettings.Create("client-test-id", "client-test-secret"));

        Assert.Equal(2, result.TotalAvailable);
        Assert.Equal(4.9, result.AverageRating);
        Assert.Collection(result.Reviews,
            first =>
            {
                Assert.Equal("Анна", first.Author);
                Assert.Equal(5, first.Rating);
                Assert.Equal(new DateOnly(2026, 7, 20), first.PublishedOn);
                Assert.Equal("Очень внимательный мастер.", first.Text);
            },
            second =>
            {
                Assert.Equal("Клиент Avito", second.Author);
                Assert.Equal(4, second.Rating);
                Assert.Equal(new DateOnly(2026, 7, 21), second.PublishedOn);
            });

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("https://official.test/token", handler.Requests[0].Uri);
        Assert.Contains("grant_type=client_credentials", handler.Requests[0].Body);
        Assert.Contains("client_id=client-test-id", handler.Requests[0].Body);
        Assert.Contains("client_secret=client-test-secret", handler.Requests[0].Body);
        Assert.Equal("https://official.test/ratings/v1/info", handler.Requests[1].Uri);
        Assert.Equal("Bearer temporary-token", handler.Requests[1].Authorization);
        Assert.Equal("https://official.test/ratings/v1/reviews?offset=0&limit=100", handler.Requests[2].Uri);
        Assert.Equal("https://official.test/ratings/v1/reviews?offset=1&limit=100", handler.Requests[3].Uri);
    }

    [Fact]
    public async Task RatingsService_ExplainsMissingReviewAccess()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"access_token":"temporary-token"}"""),
            Json(HttpStatusCode.Forbidden, """{"error":"forbidden private detail"}"""));
        using var client = new HttpClient(handler);
        var service = new AvitoRatingsService(client, new Uri("https://official.test/"));

        var error = await Assert.ThrowsAsync<AvitoApiException>(() => service.GetReviewsAsync(
            AvitoApiSettings.Create("client-test-id", "client-test-secret")));

        Assert.Contains("нет доступа к отзывам", error.Message);
        Assert.DoesNotContain("private detail", error.Message);
    }

    [Fact]
    public async Task RatingsService_ExplainsInvalidCredentialsWithoutReturningServerBody()
    {
        var handler = new QueueHandler(Json(
            HttpStatusCode.Unauthorized,
            """{"error":"client-test-secret rejected"}"""));
        using var client = new HttpClient(handler);
        var service = new AvitoRatingsService(client, new Uri("https://official.test/"));

        var error = await Assert.ThrowsAsync<AvitoApiException>(() => service.GetReviewsAsync(
            AvitoApiSettings.Create("client-test-id", "client-test-secret")));

        Assert.Contains("Client ID или Client Secret", error.Message);
        Assert.DoesNotContain("client-test-secret", error.Message);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.ToString(),
                body,
                request.Headers.Authorization?.ToString() ?? string.Empty));
            return _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("Тест не подготовил ответ для запроса.");
        }
    }

    private sealed record RecordedRequest(string Method, string Uri, string Body, string Authorization);

    private sealed class XorProtector : IWorkspaceProtector
    {
        public byte[] Protect(byte[] data) => Transform(data);
        public byte[] Unprotect(byte[] data) => Transform(data);
        private static byte[] Transform(byte[] data) => data.Select(value => (byte)(value ^ 0x3C)).ToArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MasterFlow.AvitoApiTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
