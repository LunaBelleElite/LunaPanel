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

    // -----------------------------------------------------------------
    // Preset names may themselves contain dots and spaces; the version is
    // the LAST two numeric groups. Fixture names here are generic.
    // -----------------------------------------------------------------

    [Fact]
    public void Discover_DottedPresetName_HighestVersionWins()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/My Pad v1.2 Fix.3.0.binds");
        temp.CreateFile("Bindings/My Pad v1.2 Fix.4.1.binds");
        temp.CreateFile("Bindings/My Pad v1.2 Fix.4.2.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 2), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "My Pad v1.2 Fix.4.2.binds"), result.LatestBindsFilePath);
    }

    [Fact]
    public void Discover_PresetNameEndingInDigits_LastTwoNumericGroupsAreTheVersion()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/Foo.2.4.1.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 1), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "Foo.2.4.1.binds"), result.LatestBindsFilePath);
    }

    [Fact]
    public void Discover_DottedPresetNameBackupFile_IsStillIgnored()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Bindings/My Pad v1.2 Fix.4.1.binds");
        temp.CreateFile("Bindings/My Pad v1.2 Fix.4.1.binds.1574007660.backup");
        temp.CreateFile("Bindings/Acme X.Pro.9.9.binds.1574007660.backup");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 1), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "My Pad v1.2 Fix.4.1.binds"), result.LatestBindsFilePath);
    }

    // -----------------------------------------------------------------
    // Every character Windows allows in a file name must survive in the
    // preset part (one case per character class, plus a mixed one).
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("Plain Spaces Name")]
    [InlineData("dots.in.the.name")]
    [InlineData("hy-phen_under_score")]
    [InlineData("O'Brien's & Co (v2), Test")]
    [InlineData("100% #1 @home +x =y")]
    [InlineData("semi;colon [br] {cu} ~t !e $d ^c `b")]
    [InlineData("Café Ünï")]
    [InlineData("Пульт")]
    [InlineData("手柄配置")]
    [InlineData("Acme-X.Pro v2 (Ünï) Fix, #3_final")]
    public void Discover_WindowsLegalPresetNames_AreRecognised_WithTheRightVersion(string presetName)
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile($"Bindings/{presetName}.4.6.binds");
        temp.CreateFile($"Bindings/{presetName}.4.7.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 7), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", $"{presetName}.4.7.binds"), result.LatestBindsFilePath);
    }

    [Fact]
    public void Discover_ArabicIndicDigitVersion_IsIgnoredWithoutThrowing()
    {
        using var temp = TempDirectory.Create();
        // ٤ ٢ are Arabic-Indic digits: \d matches them, int.Parse rejects them.
        temp.CreateFile("Bindings/Custom.٤.٢.binds");
        temp.CreateFile("Bindings/Custom.4.1.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 1), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "Custom.4.1.binds"), result.LatestBindsFilePath);
    }

    [Theory]
    [InlineData("Custom.99999999999.1.binds")]
    [InlineData("Custom.4.99999999999.binds")]
    public void Discover_VersionNumberBeyondIntRange_IsIgnoredWithoutThrowing(string overflowName)
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile($"Bindings/{overflowName}");
        temp.CreateFile("Bindings/Custom.4.1.binds");

        var log = new DiagnosticRingBuffer(200);
        var result = BindingsDiscovery.Discover(temp.Combine("Bindings"), log);

        Assert.Equal(new BindsVersion(4, 1), result.LatestVersion);
        Assert.Equal(temp.Combine("Bindings", "Custom.4.1.binds"), result.LatestBindsFilePath);
    }
}
