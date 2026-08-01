using System.Text;
using System.Text.Json;

namespace MasterFlow.Core;

public sealed class AvitoApiSettingsStore(string path, IWorkspaceProtector protector)
{
    private readonly string _path = Path.GetFullPath(path);
    private readonly IWorkspaceProtector _protector = protector ?? throw new ArgumentNullException(nameof(protector));

    public AvitoApiSettings? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_path)));
            var settings = JsonSerializer.Deserialize<AvitoApiSettings>(json)
                ?? throw new InvalidDataException("Файл настроек Avito API пуст.");
            return AvitoApiSettings.Create(settings.ClientId, settings.ClientSecret);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Не удалось прочитать защищённые настройки Avito API.", error);
        }
    }

    public void Save(AvitoApiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validated = AvitoApiSettings.Create(settings.ClientId, settings.ClientSecret);
        var folder = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Не удалось определить папку настроек Avito API.");
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

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
