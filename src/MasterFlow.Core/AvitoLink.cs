namespace MasterFlow.Core;

public sealed record AvitoLink(Uri Uri)
{

    public static bool TryCreate(string? value, out AvitoLink? link, out string error)
    {
        link = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Вставьте ссылку на объявление или профиль Avito.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Ссылка должна начинаться с https:// и вести на Avito.";
            return false;
        }

        if (!IsAvitoHost(uri.Host))
        {
            error = "Можно открыть только ссылку на сайте avito.ru.";
            return false;
        }

        link = new AvitoLink(uri);
        return true;
    }

    private static bool IsAvitoHost(string host) =>
        string.Equals(host, "avito.ru", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".avito.ru", StringComparison.OrdinalIgnoreCase);
}
