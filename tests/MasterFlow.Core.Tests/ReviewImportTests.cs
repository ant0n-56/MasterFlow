using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class ReviewImportTests
{
    [Theory]
    [InlineData("https://www.avito.ru/nizhniy_novgorod/predlozheniya_uslug/example", true)]
    [InlineData("https://avito.ru/profile", true)]
    [InlineData("https://login.avito.ru/account", true)]
    [InlineData("http://www.avito.ru/profile", false)]
    [InlineData("https://example.org/avito", false)]
    [InlineData("https://avito.ru.example.org/profile", false)]
    [InlineData("", false)]
    public void AvitoLink_AcceptsOnlySecureAvitoAddresses(string value, bool expected)
    {
        var actual = AvitoLink.TryCreate(value, out _, out _);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Parse_ExtractsRatingDateAuthorAndText()
    {
        var blocks = new[]
        {
            "Анна\n★★★★★\n12 июля 2026\nМастер внимательно выслушал и помог снять боль в спине."
        };

        var review = Assert.Single(ReviewTextParser.Parse(blocks));

        Assert.Equal("Анна", review.Author);
        Assert.Equal(5, review.Rating);
        Assert.Equal(new DateOnly(2026, 7, 12), review.PublishedOn);
        Assert.Contains("помог снять боль", review.Text);
    }

    [Fact]
    public void Parse_RemovesDuplicateVisibleBlocks()
    {
        const string block = "Ирина\nОценка 5 из 5\nОчень внимательный мастер, обязательно приду снова.";

        var reviews = ReviewTextParser.Parse([block, block.ToUpperInvariant()]);

        Assert.Single(reviews);
    }

    [Fact]
    public void Parse_RemovesNestedFragmentsOfTheSameReview()
    {
        const string complete = "Ольга\nОценка 5 из 5\nВсё прошло хорошо. Планирую обратиться ещё раз.";
        const string authorFragment = "Ольга\nОценка 5 из 5";
        const string textFragment = "Всё прошло хорошо. Планирую обратиться ещё раз.";

        var review = Assert.Single(ReviewTextParser.Parse([complete, authorFragment, textFragment]));

        Assert.Equal("Ольга", review.Author);
        Assert.Equal(5, review.Rating);
    }

    [Fact]
    public void Parse_IgnoresAggregateReviewSummary()
    {
        var reviews = ReviewTextParser.Parse([
            "4,9\n27 отзывов клиентов",
            "Ирина\nОчень внимательный мастер. Обязательно обращусь снова."
        ]);

        var review = Assert.Single(reviews);
        Assert.Equal("Ирина", review.Author);
    }

    [Fact]
    public void Parse_RemovesAvitoMetadataAndTechnicalRatingLabels()
    {
        const string block = """
            Ирина
            20 июля
            Сделка состоялась · Помощь специалиста на дому
            Очень внимательный мастер. Обязательно обращусь снова.
            Показать целиком
            Рейтинг 1
            Рейтинг 2
            Рейтинг 3
            Рейтинг 4
            Рейтинг 5
            """;

        var review = Assert.Single(ReviewTextParser.Parse([block]));

        Assert.Equal("Ирина", review.Author);
        Assert.Null(review.Rating);
        Assert.Equal("Очень внимательный мастер. Обязательно обращусь снова.", review.Text);
        Assert.Equal(
            "Автор: Ирина. Очень внимательный мастер. Обязательно обращусь снова.",
            review.AccessibleSummary);
    }

    [Fact]
    public void AccessibleSummary_UsesPlainLanguage()
    {
        var review = new ReviewRecord("Анна", 5, new DateOnly(2026, 7, 12), "Всё понравилось.");

        Assert.Equal(
            "Оценка 5 из 5. Дата 12.07.2026. Автор: Анна. Всё понравилось.",
            review.AccessibleSummary);
    }

    [Fact]
    public void AccessibleSummary_DoesNotRepeatMissingRatingForEveryReview()
    {
        var review = new ReviewRecord("Ирина", null, null, "Всё понравилось.");

        Assert.Equal("Автор: Ирина. Всё понравилось.", review.AccessibleSummary);
    }
}
