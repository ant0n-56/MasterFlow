using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class ConversationAnalyzerTests
{
    [Fact]
    public void Analyze_FindsUnansweredQuestionAndAdvertisementTopics()
    {
        const string text = """
            Клиент: Здравствуйте! Сколько стоит массаж и где вы принимаете?
            Клиент: Есть свободное время завтра?
            Мастер: Добрый день! Стоимость 1500 рублей, работаю с выездом на дом.
            """;

        var result = ConversationAnalyzer.Analyze(text);

        Assert.Contains("Вопросов клиента: 2", result.Summary);
        Assert.Contains("не найден ответ мастера: 1", result.Summary);
        Assert.Contains(result.CommunicationRecommendations, item => item.Title == "Ответьте на каждый вопрос");
        Assert.Contains(result.AdvertisementRecommendations, item => item.Title == "Цена");
        Assert.Contains(result.AdvertisementRecommendations, item => item.Title == "Место и выезд");
        Assert.Contains(result.AdvertisementRecommendations, item => item.Title == "Свободное время");
    }

    [Fact]
    public void Analyze_DoesNotExposeDetectedPersonalData()
    {
        const string text = "Клиент: Мой телефон +7 900 123-45-67, почта client@example.com. Сколько стоит услуга?\nМастер: Цена 1500 рублей.";

        var result = ConversationAnalyzer.Analyze(text);
        var allOutput = string.Join(" ",
            new[] { result.Summary, result.PrivacyNotice }
                .Concat(result.CommunicationRecommendations.Select(item => item.AccessibleSummary))
                .Concat(result.AdvertisementRecommendations.Select(item => item.AccessibleSummary)));

        Assert.Contains("Найдены виды личных данных: 2", result.PrivacyNotice);
        Assert.DoesNotContain("+7 900 123-45-67", allOutput);
        Assert.DoesNotContain("client@example.com", allOutput);
    }

    [Fact]
    public void Analyze_ExplainsRoleLabelsWhenTheyAreMissing()
    {
        const string text = "Здравствуйте. Подскажите цену массажа и свободное время на завтра, пожалуйста.";

        var result = ConversationAnalyzer.Analyze(text);

        Assert.Contains("начинайте сообщения словами", result.Summary);
        Assert.Contains(result.CommunicationRecommendations, item => item.Title == "Обозначьте участников");
    }

    [Fact]
    public void Analyze_RejectsTextThatIsTooShort()
    {
        var error = Assert.Throws<ArgumentException>(() => ConversationAnalyzer.Analyze("Привет"));

        Assert.StartsWith("Добавьте переписку длиной не меньше 20 знаков.", error.Message);
    }
}
