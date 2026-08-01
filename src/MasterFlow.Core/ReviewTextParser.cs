using System.Globalization;
using System.Text.RegularExpressions;

namespace MasterFlow.Core;

public static partial class ReviewTextParser
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static IReadOnlyList<ReviewRecord> Parse(IEnumerable<string> visibleBlocks)
    {
        ArgumentNullException.ThrowIfNull(visibleBlocks);

        var candidates = visibleBlocks
            .Select(source => new Candidate(source, Normalize(source)))
            .Where(candidate => candidate.Normalized.Length >= 20)
            .Where(candidate => !AggregateReviewSummaryRegex().IsMatch(candidate.Normalized))
            .DistinctBy(candidate => candidate.Normalized, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var maximalCandidates = candidates
            .Where(candidate => !candidates.Any(other =>
                !ReferenceEquals(candidate, other) &&
                other.Normalized.Length > candidate.Normalized.Length &&
                other.Normalized.Contains(candidate.Normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var reviews = new List<ReviewRecord>();

        foreach (var candidate in maximalCandidates)
        {
            var lines = candidate.Source
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            var author = lines.FirstOrDefault() ?? "Автор не указан";
            if (author.Length > 80)
            {
                author = "Автор не указан";
            }

            var reviewText = ExtractReviewText(lines, author);
            if (reviewText.Length < 10)
            {
                continue;
            }

            reviews.Add(new ReviewRecord(
                author,
                ExtractRating(candidate.Source),
                ExtractDate(candidate.Source),
                reviewText));
        }

        return reviews;
    }

    private static int? ExtractRating(string text)
    {
        var stars = StarsRegex().Match(text);
        if (stars.Success)
        {
            return Math.Min(5, stars.Value.Length);
        }

        var numeric = NumericRatingRegex().Match(text);
        return numeric.Success && int.TryParse(numeric.Groups["rating"].Value, out var value)
            ? value
            : null;
    }

    private static DateOnly? ExtractDate(string text)
    {
        var match = RussianDateRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return DateOnly.TryParseExact(
            match.Value,
            "d MMMM yyyy",
            RussianCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var value)
            ? value
            : null;
    }

    private static string ExtractReviewText(IReadOnlyList<string> lines, string author)
    {
        var content = new List<string>();
        var skipServiceName = false;

        foreach (var sourceLine in lines)
        {
            var line = TechnicalRatingLabelRegex().Replace(sourceLine, string.Empty);
            line = ShowFullTextRegex().Replace(line, string.Empty).Trim();
            if (line.Length == 0 || line == "·")
            {
                continue;
            }

            if (string.Equals(line, author, StringComparison.OrdinalIgnoreCase) ||
                ShortOrFullRussianDateRegex().IsMatch(line))
            {
                continue;
            }

            if (CompletedDealRegex().IsMatch(line))
            {
                skipServiceName = true;
                continue;
            }

            if (CompletedDealWithServiceRegex().IsMatch(line))
            {
                continue;
            }

            if (skipServiceName)
            {
                skipServiceName = false;
                continue;
            }

            content.Add(line);
        }

        return Normalize(string.Join(' ', content));
    }

    private static string Normalize(string text) =>
        WhitespaceRegex().Replace(text.Trim(), " ");

    private sealed record Candidate(string Source, string Normalized);

    [GeneratedRegex("★{1,5}")]
    private static partial Regex StarsRegex();

    [GeneratedRegex(@"(?<rating>[1-5])(?:[.,]0)?\s*(?:из\s*5|зв[её]зд)", RegexOptions.IgnoreCase)]
    private static partial Regex NumericRatingRegex();

    [GeneratedRegex(@"\b\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\s+\d{4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex RussianDateRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[1-5][,.]\d\s+\d+\s+отзыв(?:ов|а)?\s+клиент", RegexOptions.IgnoreCase)]
    private static partial Regex AggregateReviewSummaryRegex();

    [GeneratedRegex(@"\bРейтинг\s+[1-5]\b", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalRatingLabelRegex();

    [GeneratedRegex(@"\bПоказать\s+целиком\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShowFullTextRegex();

    [GeneratedRegex(@"^Сделка\s+состоялась\s*[·•]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CompletedDealRegex();

    [GeneratedRegex(@"^Сделка\s+состоялась\s*[·•]?\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex CompletedDealWithServiceRegex();

    [GeneratedRegex(@"^\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)(?:\s+\d{4})?$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortOrFullRussianDateRegex();
}
