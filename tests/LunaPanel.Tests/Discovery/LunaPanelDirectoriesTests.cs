using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class LunaPanelDirectoriesTests
{
    [Fact]
    public void Resolve_ReturnsThreeDistinctPathsUnderALunaPanelRoot()
    {
        var layout = LunaPanelDirectories.Resolve(@"C:\Users\Test\AppData\Local", isDevBuild: false);

        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel", layout.LogsDirectory);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel", layout.LayoutsDirectory);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel", layout.DeviceRegistryFilePath);
    }

    [Fact]
    public void EnsureCreated_CreatesLogsAndLayoutsDirectories()
    {
        using var temp = TempDirectory.Create();
        var layout = LunaPanelDirectories.Resolve(temp.Path, isDevBuild: false);

        var log = new DiagnosticRingBuffer(200);
        LunaPanelDirectories.EnsureCreated(layout, log);

        Assert.True(Directory.Exists(layout.LogsDirectory));
        Assert.True(Directory.Exists(layout.LayoutsDirectory));
    }

    [Fact]
    public void EnsureCreated_CreatesDeviceRegistryFilesParentDirectory_ButNotTheFileItself()
    {
        using var temp = TempDirectory.Create();
        var layout = LunaPanelDirectories.Resolve(temp.Path, isDevBuild: false);

        var log = new DiagnosticRingBuffer(200);
        LunaPanelDirectories.EnsureCreated(layout, log);

        Assert.True(Directory.Exists(Path.GetDirectoryName(layout.DeviceRegistryFilePath)));
        Assert.False(File.Exists(layout.DeviceRegistryFilePath));
    }

    [Fact]
    public void EnsureCreated_CalledTwice_DoesNotThrow()
    {
        using var temp = TempDirectory.Create();
        var layout = LunaPanelDirectories.Resolve(temp.Path, isDevBuild: false);
        var log = new DiagnosticRingBuffer(200);

        LunaPanelDirectories.EnsureCreated(layout, log);
        LunaPanelDirectories.EnsureCreated(layout, log);

        Assert.True(Directory.Exists(layout.LogsDirectory));
    }

    /// <summary>
    /// A dev build must root under a completely different folder than a real
    /// install, never a suffix or subfolder of it - the whole point of this
    /// fix is that a dev launch's Logs/Layouts/Pairing state can never be
    /// found by, or collide with, a real install walking the plain
    /// <c>LunaPanel</c> path.
    /// </summary>
    [Fact]
    public void Resolve_WithIsDevBuildTrue_RootsUnderLunaPanelDashDev()
    {
        var layout = LunaPanelDirectories.Resolve(@"C:\Users\Test\AppData\Local", isDevBuild: true);

        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel-Dev", layout.LogsDirectory);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel-Dev", layout.LayoutsDirectory);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\LunaPanel-Dev", layout.DeviceRegistryFilePath);
    }

    /// <summary>
    /// The critical regression guard this fix exists to add: a non-dev call
    /// must resolve to EXACTLY the same path a real install used before
    /// <c>isDevBuild</c> existed at all - never <c>LunaPanel-Dev</c>, and
    /// never anything that merely starts with <c>LunaPanel</c> (which
    /// <c>LunaPanel-Dev</c> itself also does, so a bare <c>StartsWith</c>
    /// check here would not catch a future accidental default-to-dev
    /// regression).
    /// </summary>
    [Fact]
    public void Resolve_WithIsDevBuildFalse_RootsUnderPlainLunaPanel_UnchangedFromBeforeThisFix()
    {
        var layout = LunaPanelDirectories.Resolve(@"C:\Users\Test\AppData\Local", isDevBuild: false);

        Assert.Equal(@"C:\Users\Test\AppData\Local\LunaPanel\Logs", layout.LogsDirectory);
        Assert.Equal(@"C:\Users\Test\AppData\Local\LunaPanel\Layouts", layout.LayoutsDirectory);
        Assert.Equal(@"C:\Users\Test\AppData\Local\LunaPanel\Pairing\device-registry.json", layout.DeviceRegistryFilePath);
    }
}
