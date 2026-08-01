using System.Text.RegularExpressions;

namespace MasterFlow.Core;

public static partial class ReviewAnalyzer
{
    private static readonly Theme[] StrengthThemes =
    [
        new(
            "Результат услуги",
            ["помог", "результат", "улучш", "проблема решена", "стало легче", "боль отступ", "боли прош", "эффект"]),
        new(
            "Профессионализм",
            ["профессионал", "специалист", "мастер своего дела", "руки от бога", "грамотн", "опытн"]),
        new(
            "Внимание и забота",
            ["вниматель", "забот", "отзывчив", "добрый", "выслуш", "индивидуальн", "бережн"]),
        new(
            "Комфорт и атмосфера",
            ["комфорт", "уют", "атмосфер", "приятн", "чист", "расслаб", "масл"]),
        new(
            "Готовность вернуться",
            ["приду снова", "обращусь снова", "вернусь", "буду продолж", "запишусь ещё", "повторн"]),
        new(
            "Рекомендации другим",
            ["рекоменд", "советую", "смело обращ"]),
        new(
            "Понятное общение",
            ["объясн", "рассказал", "ответил", "общен", "понятно"]),
        new(
            "Пунктуальность",
            ["вовремя", "пунктуал", "без опоздан"])
    ];

    private static readonly Theme[] AttentionThemes =
    [
        new(
            "Результат не оправдал ожидания",
            ["не помог", "без результата", "нет результата", "стало хуже", "не понрав", "разочаров"]),
        new(
            "Стоимость услуги",
            ["слишком дорого", "дорого", "цена выше", "переплат"]),
        new(
            "Организация записи",
            ["опоздал", "не приш", "отменил", "перенёс", "перенес", "долго ждал"]),
        new(
            "Общение с клиентом",
            ["груб", "не отвечает", "игнор", "невниматель", "не объяснил"]),
        new(
            "Дискомфорт во время услуги",
            ["слишком больно", "неприятно", "дискомфорт", "гряз", "неаккурат"])
    ];

    public static ReviewAnalysisResult Analyze(
        IReadOnlyList<ReviewRecord> reviews,
        double? averageRating = null)
    {
        ArgumentNullException.ThrowIfNull(reviews);

        if (reviews.Count == 0)
        {
            return new ReviewAnalysisResult(
                0,
                averageRating,
                "Для анализа пока нет отзывов.",
                [],
                [],
                []);
        }

        var strengths = FindInsights(reviews, StrengthThemes, minimumMentions: reviews.Count >= 10 ? 2 : 1)
            .Take(5)
            .ToArray();
        var attentionAreas = FindAttentionInsights(reviews, minimumMentions: 1)
            .Take(4)
            .ToArray();
        var recommendations = BuildRecommendations(reviews.Count, strengths, attentionAreas);

        var summary = BuildSummary(reviews.Count, averageRating, strengths, attentionAreas);
        return new ReviewAnalysisResult(
            reviews.Count,
            averageRating,
            summary,
            strengths,
            attentionAreas,
            recommendations);
    }

    private static IEnumerable<ReviewInsight> FindInsights(
        IReadOnlyList<ReviewRecord> reviews,
        IReadOnlyList<Theme> themes,
        int minimumMentions)
    {
        return themes
            .Select(theme =>
            {
                var matches = reviews
                    .Where(review => ContainsStrengthTheme(review.Text, theme))
                    .ToArray();
                return new ReviewInsight(
                    theme.Title,
                    matches.Length,
                    reviews.Count,
                    matches.Length == 0 ? string.Empty : MakeExcerpt(matches[0].Text));
            })
            .Where(insight => insight.Mentions >= minimumMentions)
            .OrderByDescending(insight => insight.Mentions)
            .ThenBy(insight => insight.Title, StringComparer.CurrentCulture);
    }

    private static IEnumerable<ReviewInsight> FindAttentionInsights(
        IReadOnlyList<ReviewRecord> reviews,
        int minimumMentions)
    {
        return AttentionThemes
            .Select(theme =>
            {
                var matches = reviews
                    .Where(review => ContainsAttentionTheme(review.Text, theme))
                    .ToArray();
                return new ReviewInsight(
                    theme.Title,
                    matches.Length,
                    reviews.Count,
                    matches.Length == 0 ? string.Empty : MakeExcerpt(matches[0].Text));
            })
            .Where(insight => insight.Mentions >= minimumMentions)
            .OrderByDescending(insight => insight.Mentions)
            .ThenBy(insight => insight.Title, StringComparer.CurrentCulture);
    }

    private static IReadOnlyList<ReviewRecommendation> BuildRecommendations(
        int reviewCount,
        IReadOnlyList<ReviewInsight> strengths,
        IReadOnlyList<ReviewInsight> attentionAreas)
    {
        var result = new List<ReviewRecommendation>();

        if (strengths.Count > 0)
        {
            var strongest = FindBestAdvertisingStrength(strengths);
            result.Add(new ReviewRecommendation(
                "Усилить объявление",
                BuildAdvertisingRecommendation(strongest, reviewCount)));
        }

        var returnTheme = strengths.FirstOrDefault(insight => insight.Title == "Готовность вернуться");
        if (returnTheme is not null)
        {
            result.Add(new ReviewRecommendation(
                "Предлагать следующую запись",
                "Клиенты пишут, что готовы вернуться. В конце сеанса предлагайте сразу выбрать дату следующего визита."));
        }

        if (attentionAreas.Count > 0)
        {
            var mainRisk = attentionAreas[0];
            result.Add(new ReviewRecommendation(
                "Проверить замечания клиентов",
                $"Тема «{mainRisk.Title.ToLowerInvariant()}» встретилась в {mainRisk.Mentions} из {reviewCount} отзывов. " +
                "Проверьте эти отзывы отдельно и решите, какое изменение услуги поможет."));
        }
        else
        {
            result.Add(new ReviewRecommendation(
                "Сохранять качество",
                "Повторяющихся проблем в тексте не найдено. Продолжайте собирать отзывы и проверяйте анализ после новых оценок."));
        }

        result.Add(new ReviewRecommendation(
            "Использовать слова клиентов",
            "Возьмите одну короткую фразу из сильных сторон для объявления. Не указывайте имя клиента без его согласия."));

        return result;
    }

    private static string BuildSummary(
        int reviewCount,
        double? averageRating,
        IReadOnlyList<ReviewInsight> strengths,
        IReadOnlyList<ReviewInsight> attentionAreas)
    {
        var ratingText = averageRating.HasValue
            ? $" Средняя оценка: {averageRating:0.0} из 5."
            : string.Empty;
        var strengthText = strengths.Count > 0
            ? $" Чаще всего клиенты отмечают: {strengths[0].Title.ToLowerInvariant()}."
            : " Повторяющиеся сильные стороны пока не найдены.";
        var attentionText = attentionAreas.Count == 0
            ? " Повторяющихся проблем в тексте не найдено."
            : $" Требует внимания: {attentionAreas[0].Title.ToLowerInvariant()}.";

        return $"Проанализировано отзывов: {reviewCount}.{ratingText}{strengthText}{attentionText}";
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> keywords)
    {
        var normalized = text.ToLowerInvariant().Replace('ё', 'е');
        return keywords.Any(keyword => Regex.IsMatch(
            normalized,
            $@"(?<!\p{{L}}){Regex.Escape(keyword.Replace('ё', 'е'))}",
            RegexOptions.CultureInvariant));
    }

    private static bool ContainsAttentionTheme(string text, Theme theme)
    {
        if (!ContainsAny(text, theme.Keywords))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant().Replace('ё', 'е');
        return theme.Title != "Дискомфорт во время услуги" ||
               !ReliefContextRegex().IsMatch(normalized);
    }

    private static bool ContainsStrengthTheme(string text, Theme theme)
    {
        if (!ContainsAny(text, theme.Keywords))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant().Replace('ё', 'е');
        return theme.Title switch
        {
            "Результат услуги" => !NegativeResultRegex().IsMatch(normalized),
            "Рекомендации другим" => !NegativeRecommendationRegex().IsMatch(normalized),
            "Готовность вернуться" => !NegativeReturnRegex().IsMatch(normalized),
            _ => true
        };
    }

    private static ReviewInsight FindBestAdvertisingStrength(IReadOnlyList<ReviewInsight> strengths)
    {
        string[] priority = ["Результат услуги", "Профессионализм", "Внимание и забота", "Комфорт и атмосфера"];
        foreach (var title in priority)
        {
            var insight = strengths.FirstOrDefault(item => item.Title == title);
            if (insight is not null)
            {
                return insight;
            }
        }

        return strengths[0];
    }

    private static string BuildAdvertisingRecommendation(ReviewInsight strength, int reviewCount)
    {
        var action = strength.Title switch
        {
            "Результат услуги" => "Клиенты часто пишут о результате. Коротко опишите в объявлении, с какими задачами вы помогаете.",
            "Профессионализм" => "Клиенты отмечают профессионализм. Добавьте в объявление опыт, обучение и понятное описание вашего подхода.",
            "Внимание и забота" => "Клиенты ценят внимательное отношение. Расскажите в объявлении, как вы уточняете запрос и учитываете состояние клиента.",
            "Комфорт и атмосфера" => "Клиенты отмечают комфорт. Опишите, как проходит сеанс и что помогает человеку чувствовать себя спокойно.",
            _ => $"Добавьте в объявление подтверждённое преимущество «{strength.Title.ToLowerInvariant()}»."
        };

        return $"{action} Эту тему отметили {strength.Mentions} из {reviewCount} клиентов.";
    }

    private static string MakeExcerpt(string text)
    {
        var normalized = WhitespaceRegex().Replace(text.Trim(), " ");
        if (normalized.Length <= 160)
        {
            return normalized;
        }

        return $"{normalized[..157].TrimEnd()}…";
    }

    private sealed record Theme(string Title, string[] Keywords);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?:прош\w*|исчез\w*|отступ\w*|не\s+чувств\w*)\s+(?:\w+\s+){0,3}(?:боль|дискомфорт)|(?:боль|дискомфорт)\s+(?:\w+\s+){0,3}(?:прош\w*|исчез\w*|отступ\w*)", RegexOptions.IgnoreCase)]
    private static partial Regex ReliefContextRegex();

    [GeneratedRegex(@"(?:^|\s)(?:не\s+помог\w*|без\s+результат\w*|нет\s+результат\w*|стало\s+хуже)", RegexOptions.IgnoreCase)]
    private static partial Regex NegativeResultRegex();

    [GeneratedRegex(@"(?:^|\s)не\s+рекоменд\w*", RegexOptions.IgnoreCase)]
    private static partial Regex NegativeRecommendationRegex();

    [GeneratedRegex(@"(?:^|\s)не\s+(?:приду|обращусь|вернусь|буду\s+продолж\w*|запишусь)", RegexOptions.IgnoreCase)]
    private static partial Regex NegativeReturnRegex();
}
