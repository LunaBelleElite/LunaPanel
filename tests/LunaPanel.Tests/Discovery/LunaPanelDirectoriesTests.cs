using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class LunaPanelDirectoriesTests
{
    [Fact]
    public void Resolve_ReturnsThreeDistinctPathsUnderALunaPanelRoot()
    {
        var layout = LunaPanelDirectories.Resolve(@"C:\Users\Test\AppData\Local");

        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel", layout.LogsDirectory);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel", layout.LayoutsDirectory);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel", layout.DeviceRegistryFilePath);
    }

    [Fact]
    public void EnsureCreated_CreatesLogsAndLayoutsDirectories()
    {
        using var temp = TempDirectory.Create();
        var layout = LunaPanelDirectories.Resolve(temp.Path);

        var log = new DiagnosticRingBuffer(200);
        LunaPanelDirectories.EnsureCreated(layout, log);

        Assert.True(Directory.Exists(layout.LogsDirectory));
        Assert.True(Directory.Exists(layout.LayoutsDirectory));
    }

    [Fact]
    public void EnsureCreated_CreatesDeviceRegistryFilesParentDirectory_ButNotTheFileItself()
    {
        using var temp = TempDirectory.Create();
        var layout = LunaPanelDirectories.Resolve(temp.Path);

        var log = new DiagnosticRingBuffer(200);
        LunaPanelDirectories.EnsureCreated(layout, log);

        Assert.True(Directory.Exists(Path.GetDirectoryName(layout.DeviceRegistryFilePath)));
        Assert.False(File.Exists(layout.DeviceRegistryFilePath));
    }

    [Fact]
    public void EnsureCreated_CalledTwice_DoesNotThrow()
    {
        using var temp = TempDirectory.Create();
        var layout = LunaPanelDirectories.Resolve(temp.Path);
        var log = new DiagnosticRingBuffer(200);

        LunaPanelDirectories.EnsureCreated(layout, log);
        LunaPanelDirectories.EnsureCreated(layout, log);

        Assert.True(Directory.Exists(layout.LogsDirectory));
    }
}
