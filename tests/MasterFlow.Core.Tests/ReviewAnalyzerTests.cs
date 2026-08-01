using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class ReviewAnalyzerTests
{
    [Fact]
    public void Analyze_ReturnsClearEmptyState()
    {
        var result = ReviewAnalyzer.Analyze([]);

        Assert.Equal(0, result.ReviewCount);
        Assert.Equal("Для анализа пока нет отзывов.", result.Summary);
        Assert.Empty(result.Strengths);
        Assert.Empty(result.AttentionAreas);
        Assert.Empty(result.Recommendations);
    }

    [Fact]
    public void Analyze_FindsRepeatedStrengthsAndCountsEachReviewOnce()
    {
        var reviews = new[]
        {
            Review("Ольга", "Очень внимательный и заботливый мастер. Всё подробно объяснил."),
            Review("Ирина", "Внимательный специалист, внимательно выслушал и помог."),
            Review("Анна", "Получила хороший результат и обязательно приду снова.")
        };

        var result = ReviewAnalyzer.Analyze(reviews, 4.9);
        var attention = Assert.Single(result.Strengths, item => item.Title == "Внимание и забота");

        Assert.Equal(2, attention.Mentions);
        Assert.Equal(3, attention.TotalReviews);
        Assert.Contains("Средняя оценка: 4,9 из 5", result.Summary);
    }

    [Fact]
    public void Analyze_FindsAttentionAreaAndCreatesAction()
    {
        var reviews = new[]
        {
            Review("Ольга", "Мастер опоздал, поэтому пришлось долго ждать."),
            Review("Ирина", "Услуга понравилась, специалист был внимательным.")
        };

        var result = ReviewAnalyzer.Analyze(reviews);
        var issue = Assert.Single(result.AttentionAreas);

        Assert.Equal("Организация записи", issue.Title);
        Assert.Contains(result.Recommendations, item => item.Title == "Проверить замечания клиентов");
    }

    [Fact]
    public void Analyze_DoesNotTreatPositivePhrasesAsProblems()
    {
        var reviews = new[]
        {
            Review("Ольга", "Мне понравилось. После сеанса прошла боль и дискомфорт."),
            Review("Ирина", "Получила хороший результат, стало заметно легче.")
        };

        var result = ReviewAnalyzer.Analyze(reviews);

        Assert.Empty(result.AttentionAreas);
        Assert.Contains("Повторяющихся проблем в тексте не найдено", result.Summary);
    }

    [Fact]
    public void Analyze_PrioritizesResultForAdvertisingRecommendation()
    {
        var reviews = new[]
        {
            Review("Ольга", "Получила заметный результат. Всем рекомендую."),
            Review("Ирина", "Есть хороший результат. Обязательно рекомендую знакомым."),
            Review("Анна", "Рекомендую этого специалиста.")
        };

        var result = ReviewAnalyzer.Analyze(reviews);
        var recommendation = Assert.Single(result.Recommendations, item => item.Title == "Усилить объявление");

        Assert.Contains("Клиенты часто пишут о результате", recommendation.Details);
    }

    [Fact]
    public void Analyze_DoesNotCountNegatedPhrasesAsStrengths()
    {
        var reviews = new[]
        {
            Review("Ольга", "Мастер не помог, результата нет. Не рекомендую и не приду снова.")
        };

        var result = ReviewAnalyzer.Analyze(reviews);

        Assert.DoesNotContain(result.Strengths, item => item.Title == "Результат услуги");
        Assert.DoesNotContain(result.Strengths, item => item.Title == "Рекомендации другим");
        Assert.DoesNotContain(result.Strengths, item => item.Title == "Готовность вернуться");
        Assert.Contains(result.AttentionAreas, item => item.Title == "Результат не оправдал ожидания");
    }

    [Fact]
    public void Analyze_DoesNotIncludeClientNameInEvidence()
    {
        var reviews = new[]
        {
            Review("Светлана", "Профессиональный мастер, получила заметный результат.")
        };

        var result = ReviewAnalyzer.Analyze(reviews);

        Assert.All(result.Strengths, item => Assert.DoesNotContain("Светлана", item.Example));
    }

    [Fact]
    public void InsightAccessibleSummary_ExplainsCountAndEvidence()
    {
        var insight = new ReviewInsight("Профессионализм", 3, 5, "Профессиональный подход.");

        Assert.Equal(
            "Профессионализм. Упоминаний: 3 из 5. Пример из отзывов: Профессиональный подход.",
            insight.AccessibleSummary);
    }

    private static ReviewRecord Review(string author, string text) =>
        new(author, null, null, text);
}
