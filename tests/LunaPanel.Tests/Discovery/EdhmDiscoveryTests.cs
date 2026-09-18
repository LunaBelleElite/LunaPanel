using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class EdhmDiscoveryTests
{
    private static IReadOnlyDictionary<string, string> NoEnvironmentVariables => new Dictionary<string, string>();

    [Fact]
    public void Discover_SettingsFileAbsent_IsCleanNotFound()
    {
        using var temp = TempDirectory.Create();

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(temp.Combine("Settings.json"), NoEnvironmentVariables, log);

        Assert.False(result.SettingsFound);
        Assert.Null(result.UserDataFolder);
        Assert.Empty(result.Editions);
    }

    [Fact]
    public void Discover_UserDataFolderIsANonDefaultCustomPath_IsFoundThereNotAtAnyAssumedDefault()
    {
        using var temp = TempDirectory.Create();
        // Deliberately NOT under the settings file's own install folder, and
        // NOT a "%LOCALAPPDATA%\EDHM-UI-V3"-shaped path - a custom user
        // choice, matching what the authoring machine's own live settings
        // file actually recorded.
        var customUserDataFolder = temp.CreateSubdirectory("SomeCustomPlace/EDHM_UI");
        temp.CreateFile("SomeCustomPlace/EDHM_UI/ODYSS/EDHM/EDHM-Ini/ThemeSettings.json", "{}");
        temp.CreateFile("SomeCustomPlace/EDHM_UI/ODYSS/EDHM/EDHM-Ini/XML-Profile.ini", "[Constants]");

        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{customUserDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), NoEnvironmentVariables, log);

        Assert.True(result.SettingsFound);
        Assert.Equal(customUserDataFolder, result.UserDataFolder);
        Assert.Equal("Steam (Odyssey (Live))", result.ActiveInstance);

        var odyssey = Assert.Single(result.Editions);
        Assert.Equal("ODYSS", odyssey.EditionFolderName);
        Assert.NotNull(odyssey.ThemeSettingsJsonPath);
        Assert.NotNull(odyssey.XmlProfileIniPath);
    }

    [Fact]
    public void Discover_UserDataFolderIsAnUnexpandedPlaceholder_IsExpandedBeforeProbing()
    {
        using var temp = TempDirectory.Create();
        var expandedRoot = temp.CreateSubdirectory("FakeUserProfile/EDHM_UI");
        temp.CreateSubdirectory("FakeUserProfile/EDHM_UI/ODYSS/EDHM/EDHM-Ini");

        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            """{ "UserDataFolder": "%USERPROFILE%\\EDHM_UI", "ActiveInstance": "" }""");

        var envVariables = new Dictionary<string, string>
        {
            ["USERPROFILE"] = temp.Combine("FakeUserProfile"),
        };

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), envVariables, log);

        Assert.Equal(expandedRoot, result.UserDataFolder);
        Assert.Single(result.Editions);
    }

    [Fact]
    public void Discover_OnlyOdysseyDataPresent_HorizonsAbsentIsNotAnError()
    {
        using var temp = TempDirectory.Create();
        var userDataFolder = temp.CreateSubdirectory("EDHM_UI");
        temp.CreateSubdirectory("EDHM_UI/ODYSS/EDHM/EDHM-Ini");

        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{userDataFolder.Replace(@"\", @"\\")}}" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), NoEnvironmentVariables, log);

        var edition = Assert.Single(result.Editions);
        Assert.Equal("ODYSS", edition.EditionFolderName);
    }

    [Fact]
    public void Discover_MalformedSettingsJson_IsCleanNotFoundNotThrown()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Settings.json", "{ not valid json");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(temp.Combine("Settings.json"), NoEnvironmentVariables, log);

        Assert.False(result.SettingsFound);
        Assert.Empty(result.Editions);
    }

    [Fact]
    public void Discover_SettingsFoundButNoUserDataFolderRecorded_ReturnsNoEditionsWithoutThrowing()
    {
        using var temp = TempDirectory.Create();
        temp.CreateFile("Settings.json", "{ }");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(temp.Combine("Settings.json"), NoEnvironmentVariables, log);

        Assert.True(result.SettingsFound);
        Assert.Null(result.UserDataFolder);
        Assert.Empty(result.Editions);
    }

    // -------------------------------------------------------------------
    // O17, closed 2026-09-17: the install-time settings file is never
    // rewritten after install, so it can go on naming a folder the
    // commander has since moved away from. The live copy EDHM actually
    // keeps current lives under that folder itself.
    // -------------------------------------------------------------------

    [Fact]
    public void Discover_LiveCopyNamesADifferentFolder_FollowsTheLiveCopy_NotTheInstallTimeOne()
    {
        using var temp = TempDirectory.Create();
        var oldUserDataFolder = temp.CreateSubdirectory("OldPlace/EDHM_UI");
        var newUserDataFolder = temp.CreateSubdirectory("NewPlace/EDHM_UI");
        temp.CreateFile("NewPlace/EDHM_UI/ODYSS/EDHM/EDHM-Ini/ThemeSettings.json", "{}");

        // Install-time file: stale, still names the old folder.
        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{oldUserDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Stale" }""");

        // Live copy, written by EDHM itself at the OLD folder it once used,
        // recording that it has since moved to the new one.
        temp.CreateFile(
            "OldPlace/EDHM_UI/Settings.json",
            $$"""{ "UserDataFolder": "{{newUserDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), NoEnvironmentVariables, log);

        Assert.Equal(newUserDataFolder, result.UserDataFolder);
        Assert.Equal("Steam (Odyssey (Live))", result.ActiveInstance);
        Assert.Single(result.Editions);
    }

    [Fact]
    public void Discover_NoLiveCopyExistsYet_FreshInstall_KeepsTheInstallTimeValues()
    {
        using var temp = TempDirectory.Create();
        var userDataFolder = temp.CreateSubdirectory("EDHM_UI");
        temp.CreateSubdirectory("EDHM_UI/ODYSS/EDHM/EDHM-Ini");

        // No Settings.json written under userDataFolder itself - EDHM has
        // never run since install, so the install-time file IS the only
        // record there is.
        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{userDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), NoEnvironmentVariables, log);

        Assert.Equal(userDataFolder, result.UserDataFolder);
        Assert.Equal("Steam (Odyssey (Live))", result.ActiveInstance);
    }

    [Fact]
    public void Discover_LiveCopyIsMalformed_FallsBackToTheInstallTimeValues_WithoutThrowing()
    {
        using var temp = TempDirectory.Create();
        var userDataFolder = temp.CreateSubdirectory("EDHM_UI");
        temp.CreateSubdirectory("EDHM_UI/ODYSS/EDHM/EDHM-Ini");
        temp.CreateFile("EDHM_UI/Settings.json", "{ not valid json");

        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{userDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), NoEnvironmentVariables, log);

        Assert.Equal(userDataFolder, result.UserDataFolder);
        Assert.Equal("Steam (Odyssey (Live))", result.ActiveInstance);
        Assert.Single(result.Editions);
    }

    [Fact]
    public void Discover_LiveCopyExistsButHasNoUserDataFolder_FallsBackToTheInstallTimeValue()
    {
        using var temp = TempDirectory.Create();
        var userDataFolder = temp.CreateSubdirectory("EDHM_UI");
        temp.CreateSubdirectory("EDHM_UI/ODYSS/EDHM/EDHM-Ini");
        temp.CreateFile("EDHM_UI/Settings.json", "{ }");

        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{userDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(
            temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"), NoEnvironmentVariables, log);

        Assert.Equal(userDataFolder, result.UserDataFolder);
        Assert.Equal("Steam (Odyssey (Live))", result.ActiveInstance);
    }

    [Fact]
    public void Discover_SettingsPathIsAlreadyTheLiveOne_DoesNotReReadItself()
    {
        // When settingsJsonPath itself resolves to <UserDataFolder>\Settings.json
        // (e.g. LunaPanel is ever pointed directly at the live copy rather
        // than the install-time one), the live-copy check must not loop
        // back and re-read the same file as though it were a second source.
        using var temp = TempDirectory.Create();
        var userDataFolder = temp.CreateSubdirectory("EDHM_UI");
        temp.CreateSubdirectory("EDHM_UI/ODYSS/EDHM/EDHM-Ini");
        temp.CreateFile(
            "EDHM_UI/Settings.json",
            $$"""{ "UserDataFolder": "{{userDataFolder.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var log = new DiagnosticRingBuffer(200);
        var result = EdhmDiscovery.Discover(temp.Combine("EDHM_UI", "Settings.json"), NoEnvironmentVariables, log);

        Assert.Equal(userDataFolder, result.UserDataFolder);
        Assert.Equal("Steam (Odyssey (Live))", result.ActiveInstance);
        Assert.Single(result.Editions);
    }
}
