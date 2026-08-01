namespace MasterFlow.Core;

public sealed record ReviewRecommendation(string Title, string Details)
{
    public string AccessibleSummary => $"{Title}. {Details}";
}
