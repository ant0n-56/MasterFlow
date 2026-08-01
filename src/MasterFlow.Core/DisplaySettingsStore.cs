using System.Text;
using System.Text.Json;

namespace MasterFlow.Core;

public sealed class DisplaySettingsStore(string path, IWorkspaceProtector protector)
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly IWorkspaceProtector _protector = protector ?? throw new ArgumentNullException(nameof(protector));

    public DisplaySettings Load()
    {
        if (!File.Exists(_path))
        {
            return DisplaySettings.Default;
        }

        try
        {
            var json = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_path)));
            var settings = JsonSerializer.Deserialize<DisplaySettings>(json)
                ?? throw new InvalidDataException("Файл настроек интерфейса пуст.");
            return DisplaySettings.Create(settings.TextScalePercent);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Не удалось прочитать настройки интерфейса.", error);
        }
    }

    public void Save(DisplaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validated = DisplaySettings.Create(settings.TextScalePercent);
        var folder = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Не удалось определить папку настроек интерфейса.");
        Directory.CreateDirectory(folder);
        var temporaryPath = _path + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(validated);
            File.WriteAllBytes(temporaryPath, _protector.Protect(Encoding.UTF8.GetBytes(json)));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
