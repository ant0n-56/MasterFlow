namespace MasterFlow.Core;

public sealed record AiSettings(string ApiKey, string Model, string SafetyIdentifier)
{
    public const string DefaultModel = "gpt-5.6-terra";

    public static AiSettings Create(string apiKey, string? safetyIdentifier = null)
    {
        var normalizedKey = apiKey.Trim();
        if (normalizedKey.Length < 20 || normalizedKey.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Ключ API выглядит неполным. Проверьте его и попробуйте снова.", nameof(apiKey));
        }

        return new AiSettings(
            normalizedKey,
            DefaultModel,
            string.IsNullOrWhiteSpace(safetyIdentifier)
                ? $"mf_{Guid.NewGuid():N}"
                : safetyIdentifier.Trim());
    }
}
