using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MasterFlow.Core;

public sealed class OpenAiConversationService
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;

    public OpenAiConversationService(HttpClient httpClient, Uri? endpoint = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? new Uri("https://api.openai.com/v1/responses");
    }

    public async Task<string> AnalyzeAsync(
        string preparedText,
        AiSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(preparedText))
        {
            throw new ArgumentException("Подготовленный текст пуст.", nameof(preparedText));
        }

        ArgumentNullException.ThrowIfNull(settings);
        var requestBody = new
        {
            model = settings.Model,
            store = false,
            reasoning = new { effort = "low" },
            text = new { verbosity = "low" },
            max_output_tokens = 1400,
            safety_identifier = settings.SafetyIdentifier,
            instructions = """
                Ты помогаешь частному мастеру услуг улучшить общение с клиентами и объявление.
                Отвечай только на русском языке, ясными короткими фразами без жаргона.
                Не повторяй личные данные. Не ставь медицинских диагнозов и не обещай результат лечения.
                Дай четыре раздела: «Что уже хорошо», «Что можно улучшить в ответах»,
                «Что уточнить в объявлении», «Пример следующего ответа клиенту».
                В каждом разделе не больше четырёх конкретных пунктов.
                """,
            input = preparedText
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAiServiceException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "OpenAI не принял ключ API. Проверьте ключ в настройках.",
                HttpStatusCode.TooManyRequests => "OpenAI временно ограничил запросы. Подождите и попробуйте снова.",
                HttpStatusCode.BadRequest => "OpenAI не принял запрос. Сократите текст и попробуйте снова.",
                _ => "Не удалось получить ответ OpenAI. Проверьте интернет и попробуйте снова."
            });
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var texts = new List<string>();
            if (document.RootElement.TryGetProperty("output", out var output))
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content))
                    {
                        continue;
                    }

                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var type) &&
                            type.GetString() == "output_text" &&
                            part.TryGetProperty("text", out var text))
                        {
                            texts.Add(text.GetString() ?? string.Empty);
                        }
                    }
                }
            }

            var result = string.Join(Environment.NewLine, texts.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
            return string.IsNullOrWhiteSpace(result)
                ? throw new OpenAiServiceException("OpenAI вернул пустой ответ. Попробуйте снова.")
                : result;
        }
        catch (JsonException error)
        {
            throw new OpenAiServiceException("Не удалось прочитать ответ OpenAI. Попробуйте снова.", error);
        }
    }
}

public sealed class OpenAiServiceException : Exception
{
    public OpenAiServiceException(string message) : base(message) { }
    public OpenAiServiceException(string message, Exception innerException) : base(message, innerException) { }
}
