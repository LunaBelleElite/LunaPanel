using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class BindingsDiscoveryTests
{
    [Fact]
    public void Discover_Version4Point10BeatsVersion4Point9_NotALexicalSort()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Custom.4.9.binds");
        temp.CreateFile("Bindings/Custom.4.10.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        // A lexical/string sort would rank "4.9" above "4.10" (since '9' >
        // '1' as characters) - this pin exists specifically to catch that
        // regression.
        Assert.NotNull(result.LatestVersion);
        Assert.Equal(new BindsVersion(4, 10), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "Custom.4.10.binds"), result.LatestBindsFilePath);
    }

    [Fact]
    public void Discover_BackupFileIsIgnored_EvenWhenItWouldOtherwiseBeNewest()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Custom.4.9.binds");
        // A backup of a hypothetical, no-longer-present 4.10 file - must not
        // be picked up as if it were a real 4.10 binds file.
        temp.CreateFile("Bindings/Custom.4.10.binds.1574007660.backup");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 9), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "Custom.4.9.binds"), result.LatestBindsFilePath);
    }

    [Fact]
    public void Discover_BothEditionsOfVersionNumberingHandled_SingleDigitAndDoubleDigitMinor()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Custom.4.2.binds");
        temp.CreateFile("Bindings/Custom.4.3.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 3), result.LatestVersion);
    }

    [Fact]
    public void Discover_HigherMajorVersionBeatsHigherMinorOnLowerMajor()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Custom.4.99.binds");
        temp.CreateFile("Bindings/Custom.5.0.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(5, 0), result.LatestVersion);
    }

    [Fact]
    public void Discover_LocatesStartPresetFile()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Custom.4.2.binds");
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\nCustom\nXbox360Controller\nCustom\n");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new[] { temp.Combine("Bindings", "StartPreset.4.start") }, result.StartPresetFilePaths);
    }

    [Fact]
    public void Discover_EmptyBindingsDirectory_ReturnsCleanNotFound()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(bindingsDir, log);

        Assert.Null(result.LatestBindsFilePath);
        Assert.Null(result.LatestVersion);
        Assert.Empty(result.StartPresetFilePaths);
    }

    [Fact]
    public void Discover_BindingsDirectoryDoesNotExist_ReturnsCleanNotFound()
    {
        using var temp = TempDirectory.Create();

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("DoesNotExist"), log);

        Assert.Null(result.LatestBindsFilePath);
        Assert.Null(result.LatestVersion);
        Assert.Empty(result.StartPresetFilePaths);
    }

    [Fact]
    public void Discover_EveryBindsFileCandidateAppearsInTheLog()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Custom.4.9.binds");
        temp.CreateFile("Bindings/Custom.4.10.binds");

        var log = new DiagnosticRingBuffer(200);
        BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        var messages = log.Snapshot().Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.Contains("Custom.4.9.binds", StringComparison.Ordinal) && m.Contains("version 4.9", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Custom.4.10.binds", StringComparison.Ordinal) && m.Contains("version 4.10", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("newest binds file selected", StringComparison.Ordinal) && m.Contains("Custom.4.10.binds", StringComparison.Ordinal));
    }
}
