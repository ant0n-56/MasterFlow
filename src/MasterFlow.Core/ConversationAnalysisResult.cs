namespace MasterFlow.Core;

public sealed record ConversationAnalysisResult(
    string Summary,
    string PrivacyNotice,
    IReadOnlyList<ConversationRecommendation> CommunicationRecommendations,
    IReadOnlyList<ConversationRecommendation> AdvertisementRecommendations);
