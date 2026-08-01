namespace MasterFlow.Core;

public sealed record ReviewInsight(
    string Title,
    int Mentions,
    int TotalReviews,
    string Example)
{
    public string AccessibleSummary =>
        $"{Title}. Упоминаний: {Mentions} из {TotalReviews}. Пример из отзывов: {Example}";
}
