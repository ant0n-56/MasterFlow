using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MasterFlow.Core;

public sealed record AvitoReviewImportResult(
    IReadOnlyList<ReviewRecord> Reviews,
    double? AverageRating,
    int TotalAvailable);

public sealed class AvitoRatingsService
{
    private const int PageSize = 100;
    private const int MaximumReviews = 10_000;
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;

    public AvitoRatingsService(HttpClient httpClient, Uri? baseAddress = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseAddress = baseAddress ?? new Uri("https://api.avito.ru/");
    }

    public async Task<AvitoReviewImportResult> GetReviewsAsync(
        AvitoApiSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validated = AvitoApiSettings.Create(settings.ClientId, settings.ClientSecret);

        try
        {
            var token = await RequestAccessTokenAsync(validated, cancellationToken);
            var averageRating = await GetAverageRatingAsync(token, cancellationToken);
            var reviews = new List<ReviewRecord>();
            var ids = new HashSet<long>();
            var offset = 0;
            var total = 0;

            do
            {
                var page = await GetReviewsPageAsync(token, offset, cancellationToken);
                total = page.Total;
                foreach (var review in page.Reviews)
                {
                    if (ids.Add(review.Id))
                    {
                        reviews.Add(review.Record);
                    }
                }

                offset += page.ReturnedCount;
                if (page.ReturnedCount == 0)
                {
                    break;
                }

                if (offset > MaximumReviews)
                {
                    throw new AvitoApiException("Avito вернул слишком много отзывов за один импорт. Обратитесь в поддержку МастерFlow.");
                }
            }
            while (offset < total);

            return new AvitoReviewImportResult(reviews, averageRating, total);
        }
        catch (AvitoApiException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AvitoApiException("Avito не ответил вовремя. Проверьте интернет и попробуйте снова.");
        }
        catch (HttpRequestException error)
        {
            throw new AvitoApiException("Не удалось подключиться к Avito. Проверьте интернет и попробуйте снова.", error);
        }
        catch (JsonException error)
        {
            throw new AvitoApiException("Avito вернул ответ в неожиданном формате. Попробуйте позже.", error);
        }
    }

    private async Task<string> RequestAccessTokenAsync(
        AvitoApiSettings settings,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret
            })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, isTokenRequest: true);

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement) ||
            string.IsNullOrWhiteSpace(tokenElement.GetString()))
        {
            throw new AvitoApiException("Avito не вернул временный ключ доступа. Попробуйте позже.");
        }

        return tokenElement.GetString()!;
    }

    private async Task<double?> GetAverageRatingAsync(string token, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedGet("ratings/v1/info", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, isTokenRequest: false);

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("rating", out var rating) &&
            rating.ValueKind == JsonValueKind.Object &&
            rating.TryGetProperty("score", out var score) &&
            score.TryGetDouble(out var value))
        {
            return value;
        }

        return null;
    }

    private async Task<ReviewsPage> GetReviewsPageAsync(
        string token,
        int offset,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedGet(
            $"ratings/v1/reviews?offset={offset}&limit={PageSize}",
            token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, isTokenRequest: false);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var total = root.TryGetProperty("total", out var totalElement) && totalElement.TryGetInt32(out var parsedTotal)
            ? parsedTotal
            : 0;
        var parsed = new List<ImportedReview>();
        var returnedCount = 0;
        if (root.TryGetProperty("reviews", out var reviews) && reviews.ValueKind == JsonValueKind.Array)
        {
            foreach (var review in reviews.EnumerateArray())
            {
                returnedCount++;
                parsed.Add(ParseReview(review));
            }
        }

        return new ReviewsPage(parsed, returnedCount, total);
    }

    private static ImportedReview ParseReview(JsonElement review)
    {
        var id = review.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var parsedId)
            ? parsedId
            : 0;
        var rating = review.TryGetProperty("score", out var scoreElement) &&
                     scoreElement.TryGetInt32(out var score) && score is >= 1 and <= 5
            ? score
            : (int?)null;
        DateOnly? publishedOn = null;
        if (review.TryGetProperty("createdAt", out var createdElement) &&
            createdElement.TryGetInt64(out var timestamp))
        {
            try
            {
                publishedOn = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Не отбрасываем весь отзыв, если внешняя дата повреждена.
            }
        }
        var author = "Клиент Avito";
        if (review.TryGetProperty("sender", out var sender) &&
            sender.ValueKind == JsonValueKind.Object &&
            sender.TryGetProperty("name", out var name) &&
            !string.IsNullOrWhiteSpace(name.GetString()))
        {
            author = name.GetString()!.Trim();
        }

        var text = review.TryGetProperty("text", out var textElement)
            ? textElement.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "Отзыв оставлен без текста.";
        }

        return new ImportedReview(id, new ReviewRecord(author, rating, publishedOn, text));
    }

    private HttpRequestMessage CreateAuthorizedGet(string relativeUri, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, relativeUri));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, bool isTokenRequest)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            return;
        }

        throw new AvitoApiException(statusCode switch
        {
            HttpStatusCode.BadRequest when isTokenRequest => "Avito не принял Client ID или Client Secret. Проверьте данные в настройках.",
            HttpStatusCode.Unauthorized => "Avito не принял Client ID или Client Secret. Проверьте данные в настройках.",
            HttpStatusCode.Forbidden => "У приложения Avito нет доступа к отзывам. Проверьте тариф и доступ к API «Рейтинги и отзывы».",
            HttpStatusCode.TooManyRequests => "Avito временно ограничил запросы. Подождите и попробуйте снова.",
            _ => "Avito временно не смог вернуть отзывы. Попробуйте позже."
        });
    }

    private sealed record ImportedReview(long Id, ReviewRecord Record);
    private sealed record ReviewsPage(IReadOnlyList<ImportedReview> Reviews, int ReturnedCount, int Total);
}

public sealed class AvitoApiException : Exception
{
    public AvitoApiException(string message) : base(message) { }
    public AvitoApiException(string message, Exception innerException) : base(message, innerException) { }
}
