namespace MasterFlow.Core;

public sealed record DisplaySettings(int TextScalePercent)
{
    public const int DefaultTextScalePercent = 100;
    public static readonly IReadOnlyList<int> AllowedTextScalePercents = [100, 125, 150, 175, 200];

    public static DisplaySettings Create(int textScalePercent)
    {
        if (!AllowedTextScalePercents.Contains(textScalePercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(textScalePercent),
                "Размер текста должен быть от 100 до 200 процентов.");
        }

        return new DisplaySettings(textScalePercent);
    }

    public static DisplaySettings Default => Create(DefaultTextScalePercent);
}
