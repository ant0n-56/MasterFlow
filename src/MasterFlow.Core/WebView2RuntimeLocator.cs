namespace MasterFlow.Core;

public static class WebView2RuntimeLocator
{
    public static string? FindLatestUsableVersion(IEnumerable<string> applicationFolders)
    {
        ArgumentNullException.ThrowIfNull(applicationFolders);

        return applicationFolders
            .Where(Directory.Exists)
            .SelectMany(GetVersionFolders)
            .Select(path => new
            {
                Path = path,
                Version = Version.TryParse(System.IO.Path.GetFileName(path), out var version)
                    ? version
                    : new Version()
            })
            .Where(candidate => File.Exists(Path.Combine(candidate.Path, "msedgewebview2.exe")))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetVersionFolders(string applicationFolder)
    {
        try
        {
            return Directory.EnumerateDirectories(applicationFolder).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
