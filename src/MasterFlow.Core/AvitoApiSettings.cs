namespace MasterFlow.Core;

public sealed record AvitoApiSettings(string ClientId, string ClientSecret)
{
    public static AvitoApiSettings Create(string clientId, string clientSecret)
    {
        var normalizedId = clientId.Trim();
        var normalizedSecret = clientSecret.Trim();
        if (normalizedId.Length < 4 || normalizedId.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Client ID Avito выглядит неполным. Проверьте его и попробуйте снова.", nameof(clientId));
        }

        if (normalizedSecret.Length < 8 || normalizedSecret.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Client Secret Avito выглядит неполным. Проверьте его и попробуйте снова.", nameof(clientSecret));
        }

        return new AvitoApiSettings(normalizedId, normalizedSecret);
    }
}
