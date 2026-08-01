namespace MasterFlow.Core;

public sealed record ConversationRecommendation(string Title, string Details)
{
    public string AccessibleSummary => $"{Title}. {Details}";
}
