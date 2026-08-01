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
        const string complete = "Виктория\nОценка 5 из 5\nЗамечательный специалист, мастер своего дела. Обязательно приду снова.";
        const string authorFragment = "Виктория\nОценка 5 из 5";
        const string textFragment = "Замечательный специалист, мастер своего дела. Обязательно приду снова.";

        var review = Assert.Single(ReviewTextParser.Parse([complete, authorFragment, textFragment]));

        Assert.Equal("Виктория", review.Author);
        Assert.Equal(5, review.Rating);
    }

    [Fact]
    public void AccessibleSummary_UsesPlainLanguage()
    {
        var review = new ReviewRecord("Анна", 5, new DateOnly(2026, 7, 12), "Всё понравилось.");

        Assert.Equal(
            "Оценка 5 из 5. Дата 12.07.2026. Автор: Анна. Всё понравилось.",
            review.AccessibleSummary);
    }
}
