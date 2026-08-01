namespace MasterFlow.Core;

public sealed record ReviewAnalysisResult(
    int ReviewCount,
    double? AverageRating,
    string Summary,
    IReadOnlyList<ReviewInsight> Strengths,
    IReadOnlyList<ReviewInsight> AttentionAreas,
    IReadOnlyList<ReviewRecommendation> Recommendations);
