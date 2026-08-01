using System.Text.Json;
using System.Security.Cryptography;

namespace MasterFlow.Core;

public sealed class FileWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly IWorkspaceProtector _protector;

    public FileWorkspaceStore(string filePath, IWorkspaceProtector protector)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Не указан путь к локальным данным.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _backupPath = $"{_filePath}.backup";
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public WorkspaceLoadResult Load()
    {
        if (!File.Exists(_filePath))
        {
            return new WorkspaceLoadResult(new MasterWorkspace(), false);
        }

        try
        {
            return new WorkspaceLoadResult(LoadFile(_filePath), false);
        }
        catch (Exception primaryError) when (primaryError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or CryptographicException)
        {
            if (!File.Exists(_backupPath))
            {
                throw new InvalidDataException("Не удалось прочитать сохранённые данные клиентов.", primaryError);
            }

            try
            {
                var recoveredWorkspace = LoadFile(_backupPath);
                File.Copy(_backupPath, _filePath, overwrite: true);
                return new WorkspaceLoadResult(recoveredWorkspace, true);
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or CryptographicException)
            {
                throw new InvalidDataException("Не удалось прочитать сохранённые данные и резервную копию.", backupError);
            }
        }
    }

    public void Save(MasterWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Не удалось определить папку локальных данных.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.SerializeToUtf8Bytes(workspace.CreateSnapshot(), JsonOptions);
        var protectedData = _protector.Protect(json);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, protectedData);
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, _backupPath, overwrite: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private MasterWorkspace LoadFile(string path)
    {
        var protectedData = File.ReadAllBytes(path);
        var json = _protector.Unprotect(protectedData);
        var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException("Файл локальных данных пуст.");
        return MasterWorkspace.Restore(snapshot);
    }
}
