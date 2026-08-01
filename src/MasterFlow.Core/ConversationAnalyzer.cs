using System.Text.RegularExpressions;

namespace MasterFlow.Core;

public static partial class ConversationAnalyzer
{
    private const int MaximumLength = 200_000;

    private static readonly (string Key, string Title, string Advice, string[] Words)[] Topics =
    [
        ("price", "Цена", "Укажите понятную цену или диапазон и объясните, что входит в услугу.", ["цен", "стоим", "сколько", "руб"]),
        ("time", "Свободное время", "Добавьте актуальные часы работы и объясните, как быстро подтвердить запись.", ["время", "свобод", "когда", "запис", "сегодня", "завтра"]),
        ("place", "Место и выезд", "Укажите район, условия выезда и возможную доплату за дорогу.", ["адрес", "район", "куда", "где", "выезд", "дом"]),
        ("service", "Состав услуги", "Коротко опишите длительность, этапы и ожидаемый результат услуги.", ["услуг", "массаж", "сеанс", "длитель", "процедур", "результат"]),
        ("safety", "Ограничения и безопасность", "Добавьте ясный список важных ограничений и случаев, когда нужна консультация врача.", ["можно ли", "противопоказ", "беремен", "бол", "давлен", "здоров"]),
        ("trust", "Доверие к мастеру", "Укажите опыт, подтверждённые навыки и то, как проходит первый визит.", ["опыт", "образован", "сертифик", "отзыв", "гарант", "безопас"])
    ];

    public static ConversationAnalysisResult Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 20)
        {
            throw new ArgumentException("Добавьте переписку длиной не меньше 20 знаков.", nameof(text));
        }

        if (text.Length > MaximumLength)
        {
            throw new ArgumentException("Переписка слишком большая. Оставьте не больше 200 000 знаков.", nameof(text));
        }

        var normalized = text.Replace("\r\n", "\n").Trim();
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasRoles = lines.Any(IsClientLine) && lines.Any(IsMasterLine);
        var clientQuestions = lines.Count(line => IsClientLine(line) && line.Contains('?'));
        var unansweredQuestions = hasRoles ? CountUnansweredClientQuestions(lines) : 0;
        var sensitiveKinds = CountSensitiveKinds(normalized);

        var communication = BuildCommunicationRecommendations(normalized, hasRoles, clientQuestions, unansweredQuestions);
        var advertisement = BuildAdvertisementRecommendations(normalized);
        var summary = hasRoles
            ? $"Проанализировано строк: {lines.Length}. Вопросов клиента: {clientQuestions}. " +
              (unansweredQuestions == 0
                  ? "Явных вопросов без ответа не найдено."
                  : $"Вопросов, после которых не найден ответ мастера: {unansweredQuestions}.")
            : $"Проанализировано строк: {lines.Length}. Для точной проверки ответов начинайте сообщения словами «Клиент:» и «Мастер:».";
        var privacy = sensitiveKinds == 0
            ? "Явные телефоны и адреса электронной почты не найдены. Текст обработан только в памяти программы."
            : $"Найдены виды личных данных: {sensitiveKinds}. Они не показаны в выводах и не сохранены программой.";

        return new ConversationAnalysisResult(summary, privacy, communication, advertisement);
    }

    private static List<ConversationRecommendation> BuildCommunicationRecommendations(
        string text,
        bool hasRoles,
        int clientQuestions,
        int unansweredQuestions)
    {
        var result = new List<ConversationRecommendation>();
        if (!hasRoles)
        {
            result.Add(new ConversationRecommendation(
                "Обозначьте участников",
                "Добавьте перед сообщениями подписи «Клиент:» и «Мастер:». Тогда программа сможет проверить, на какие вопросы дан ответ."));
        }
        else if (unansweredQuestions > 0)
        {
            result.Add(new ConversationRecommendation(
                "Ответьте на каждый вопрос",
                $"Найдено вопросов без следующего ответа мастера: {unansweredQuestions}. Проверьте цену, время, место и условия услуги."));
        }
        else if (clientQuestions > 0)
        {
            result.Add(new ConversationRecommendation(
                "Ответы не пропущены",
                "После каждого явного вопроса клиента найден ответ мастера. Сохраните этот порядок в будущих диалогах."));
        }

        if (!GreetingPattern().IsMatch(text))
        {
            result.Add(new ConversationRecommendation(
                "Начните с приветствия",
                "Короткое приветствие делает ответ спокойнее: «Здравствуйте! Спасибо за обращение»."));
        }

        if (!NextStepPattern().IsMatch(text))
        {
            result.Add(new ConversationRecommendation(
                "Предложите следующий шаг",
                "Завершите ответ конкретным действием: предложите два свободных времени или попросите выбрать удобный вариант."));
        }

        if (!ConfirmationPattern().IsMatch(text))
        {
            result.Add(new ConversationRecommendation(
                "Подтвердите договорённость",
                "После выбора времени повторите дату, время, услугу и адрес. Это снижает риск недопонимания."));
        }

        return result.Take(5).ToList();
    }

    private static List<ConversationRecommendation> BuildAdvertisementRecommendations(string text)
    {
        var lower = text.ToLowerInvariant();
        var result = Topics
            .Where(topic => topic.Words.Any(lower.Contains))
            .Select(topic => new ConversationRecommendation(topic.Title, topic.Advice))
            .Take(5)
            .ToList();

        if (result.Count == 0)
        {
            result.Add(new ConversationRecommendation(
                "Сделайте объявление конкретнее",
                "Укажите цену, длительность, район, свободное время, ограничения и понятный способ записи."));
        }

        return result;
    }

    private static int CountUnansweredClientQuestions(string[] lines)
    {
        var count = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (!IsClientLine(lines[index]) || !lines[index].Contains('?'))
            {
                continue;
            }

            var answered = false;
            for (var next = index + 1; next < lines.Length; next++)
            {
                if (IsMasterLine(lines[next]))
                {
                    answered = true;
                    break;
                }

                if (IsClientLine(lines[next]))
                {
                    break;
                }
            }

            if (!answered)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsClientLine(string line) =>
        line.StartsWith("Клиент:", StringComparison.CurrentCultureIgnoreCase) ||
        line.StartsWith("Покупатель:", StringComparison.CurrentCultureIgnoreCase);

    private static bool IsMasterLine(string line) =>
        line.StartsWith("Мастер:", StringComparison.CurrentCultureIgnoreCase) ||
        line.StartsWith("Вы:", StringComparison.CurrentCultureIgnoreCase);

    private static int CountSensitiveKinds(string text)
    {
        var count = 0;
        if (PhonePattern().IsMatch(text)) count++;
        if (EmailPattern().IsMatch(text)) count++;
        if (LinkPattern().IsMatch(text)) count++;
        return count;
    }

    [GeneratedRegex(@"\b(?:здравств|добрый\s+(?:день|вечер|утро)|привет)", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingPattern();

    [GeneratedRegex(@"\b(?:выберите|подойд[её]т|записать|запишу|удобн(?:о|ый)|свободн(?:о|ое|ые))\b", RegexOptions.IgnoreCase)]
    private static partial Regex NextStepPattern();

    [GeneratedRegex(@"\b(?:подтверждаю|договорились|жду\s+вас|записал|записала)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmationPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?7|8)[\s()\-]*\d{3}[\s()\-]*\d{3}[\s\-]*\d{2}[\s\-]*\d{2}(?!\d)")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.[A-Za-zА-Яа-я]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
