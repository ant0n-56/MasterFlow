namespace MasterFlow.Core;

public sealed record ReviewRecord(
    string Author,
    int? Rating,
    DateOnly? PublishedOn,
    string Text)
{
    public string AccessibleSummary
    {
        get
        {
            var rating = Rating.HasValue ? $"Оценка {Rating} из 5. " : "Оценка не указана. ";
            var date = PublishedOn.HasValue ? $"Дата {PublishedOn:dd.MM.yyyy}. " : string.Empty;
            return $"{rating}{date}Автор: {Author}. {Text}";
        }
    }
}
