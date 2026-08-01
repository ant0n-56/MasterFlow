using System.Net;
using System.Text;
using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class OptionalAiTests
{
    [Fact]
    public void CloudSanitizer_RemovesExplicitContactsAndKeepsUsefulText()
    {
        const string source = "Клиент: телефон +7 900 123-45-67, client@example.com, https://example.com. Сколько стоит массаж?";

        var result = ConversationCloudSanitizer.Prepare(source);

        Assert.Equal(3, result.RedactionCount);
        Assert.DoesNotContain("123-45-67", result.Text);
        Assert.DoesNotContain("client@example.com", result.Text);
        Assert.DoesNotContain("https://example.com", result.Text);
        Assert.Contains("Сколько стоит массаж?", result.Text);
    }

    [Fact]
    public void AiSettingsStore_EncryptsKeyAndCanDeleteIt()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "ai-settings.dat");
        var store = new AiSettingsStore(path, new XorProtector());
        var settings = AiSettings.Create("sk-test-abcdefghijklmnopqrstuvwxyz");

        store.Save(settings);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(settings.ApiKey, restored.ApiKey);
        Assert.Equal(AiSettings.DefaultModel, restored.Model);
        Assert.DoesNotContain(settings.ApiKey, Encoding.UTF8.GetString(File.ReadAllBytes(path)));

        store.Delete();
        Assert.Null(store.Load());
    }

    [Fact]
    public async Task OpenAiService_SendsNonStoredRequestAndReadsOutputText()
    {
        var handler = new RecordingHandler("""
            {
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "Что уже хорошо\n- Ответ понятный." }
                  ]
                }
              ]
            }
            """);
        using var client = new HttpClient(handler);
        var service = new OpenAiConversationService(client, new Uri("https://local.test/v1/responses"));
        var settings = AiSettings.Create("sk-test-abcdefghijklmnopqrstuvwxyz", "mf_test_safety");

        var result = await service.AnalyzeAsync("Клиент: Сколько стоит услуга?", settings);

        Assert.Contains("Ответ понятный", result);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(settings.ApiKey, handler.AuthorizationParameter);
        Assert.Contains("\"model\":\"gpt-5.6-terra\"", handler.RequestBody);
        Assert.Contains("\"store\":false", handler.RequestBody);
        Assert.Contains("\"safety_identifier\":\"mf_test_safety\"", handler.RequestBody);
    }

    [Fact]
    public async Task OpenAiService_ExplainsInvalidKeyWithoutExposingServerBody()
    {
        var handler = new RecordingHandler("secret server detail", HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        var service = new OpenAiConversationService(client, new Uri("https://local.test/v1/responses"));

        var error = await Assert.ThrowsAsync<OpenAiServiceException>(() => service.AnalyzeAsync(
            "Подготовленный текст переписки",
            AiSettings.Create("sk-test-abcdefghijklmnopqrstuvwxyz")));

        Assert.Equal("OpenAI не принял ключ API. Проверьте ключ в настройках.", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    private sealed class RecordingHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class XorProtector : IWorkspaceProtector
    {
        public byte[] Protect(byte[] data) => Transform(data);
        public byte[] Unprotect(byte[] data) => Transform(data);
        private static byte[] Transform(byte[] data) => data.Select(value => (byte)(value ^ 0x5A)).ToArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MasterFlow.AiTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
