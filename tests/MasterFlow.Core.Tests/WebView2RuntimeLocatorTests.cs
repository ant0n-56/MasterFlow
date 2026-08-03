using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class WebView2RuntimeLocatorTests
{
    [Fact]
    public void FindLatestUsableVersion_FallsBackWhenNewestRegisteredVersionIsIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"MasterFlow.WebView2.{Guid.NewGuid():N}");
        try
        {
            var workingVersion = Directory.CreateDirectory(Path.Combine(root, "150.0.4078.105")).FullName;
            Directory.CreateDirectory(Path.Combine(root, "151.0.4129.59"));
            File.WriteAllText(Path.Combine(workingVersion, "msedgewebview2.exe"), "test executable marker");

            var actual = WebView2RuntimeLocator.FindLatestUsableVersion([root]);

            Assert.Equal(workingVersion, actual);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
