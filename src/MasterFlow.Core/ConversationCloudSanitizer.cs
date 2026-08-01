using System.Text.RegularExpressions;

namespace MasterFlow.Core;

public static partial class ConversationCloudSanitizer
{
    public static CloudTextPreparation Prepare(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Сначала добавьте переписку.", nameof(text));
        }

        var count = 0;
        var prepared = Replace(EmailPattern(), text, "[email удалён]", ref count);
        prepared = Replace(PhonePattern(), prepared, "[телефон удалён]", ref count);
        prepared = Replace(LinkPattern(), prepared, "[ссылка удалена]", ref count);
        return new CloudTextPreparation(prepared.Trim(), count);
    }

    private static string Replace(Regex pattern, string value, string replacement, ref int count)
    {
        count += pattern.Matches(value).Count;
        return pattern.Replace(value, replacement);
    }

    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.[A-Za-zА-Яа-я]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?7|8)[\s()\-]*\d{3}[\s()\-]*\d{3}[\s\-]*\d{2}[\s\-]*\d{2}(?!\d)")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
