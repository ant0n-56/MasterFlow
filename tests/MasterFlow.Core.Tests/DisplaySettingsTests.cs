using System.Text;
using MasterFlow.Core;

namespace MasterFlow.Core.Tests;

public sealed class DisplaySettingsTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    [InlineData(175)]
    [InlineData(200)]
    public void Create_AcceptsEveryDocumentedScale(int percent)
    {
        Assert.Equal(percent, DisplaySettings.Create(percent).TextScalePercent);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(201)]
    public void Create_RejectsUnsupportedScale(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplaySettings.Create(percent));
    }

    [Fact]
    public void Store_ProtectsAndRestoresTextScale()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "display-settings.dat");
        var store = new DisplaySettingsStore(path, new XorProtector());

        store.Save(DisplaySettings.Create(200));
        var restored = store.Load();

        Assert.Equal(200, restored.TextScalePercent);
        Assert.DoesNotContain("200", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
    }

    private sealed class XorProtector : IWorkspaceProtector
    {
        public byte[] Protect(byte[] data) => data.Select(value => (byte)(value ^ 0x7D)).ToArray();
        public byte[] Unprotect(byte[] data) => Protect(data);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MasterFlow.DisplayTests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
