using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class PathDiscoveryServiceTests
{
    private static PathDiscoveryEnvironment EmptyEnvironment(TempDirectory temp) => new(
        SteamRoots: Array.Empty<string>(),
        EpicManifestsDirectory: temp.Combine("NoEpic"),
        FrontierRegistryInstallPath: null,
        FrontierDefaultInstallRoot: temp.Combine("NoFrontier"),
        BindingsDirectory: temp.Combine("NoBindings"),
        EdhmSettingsJsonPath: temp.Combine("NoEdhm", "Settings.json"),
        EnvironmentVariables: new Dictionary<string, string>(),
        LocalAppData: temp.CreateSubdirectory("LocalAppData"));

    [Fact]
    public void Discover_CompletelyEmptyEnvironment_ReturnsCleanNotFoundEverywhere_WithoutThrowing()
    {
        using var temp = TempDirectory.Create();
        var log = new DiagnosticRingBuffer(500);

        var result = PathDiscoveryService.Discover(EmptyEnvironment(temp), log);

        Assert.Empty(result.EliteInstallations);
        Assert.Null(result.Bindings.LatestBindsFilePath);
        Assert.Null(result.BindingsSelection.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Fallback, result.BindingsSelection.Method);
        Assert.False(result.Edhm.SettingsFound);

        // LunaPanel's own directories are the one thing that must exist
        // regardless of anything else being found - the app must remain
        // usable with no game at all.
        Assert.True(Directory.Exists(result.LunaPanelDirectories.LogsDirectory));
        Assert.True(Directory.Exists(result.LunaPanelDirectories.LayoutsDirectory));
    }

    [Fact]
    public void Discover_EmptyEnvironment_LogsANotFoundSummaryForTheEliteInstall()
    {
        using var temp = TempDirectory.Create();
        var log = new DiagnosticRingBuffer(500);

        PathDiscoveryService.Discover(EmptyEnvironment(temp), log);

        Assert.Contains(
            log.Snapshot(),
            e => e.Message.Contains("Elite Dangerous install: not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_FullSyntheticTree_FindsEverythingAndLogsEachProbe()
    {
        using var temp = TempDirectory.Create();

        // Steam, one library, Odyssey.
        var steamRoot = temp.CreateSubdirectory("Steam");
        var steamLibrary = temp.CreateSubdirectory("SteamLibrary");
        temp.CreateFile("SteamLibrary/steamapps/common/Elite Dangerous/Products/od/EliteDangerous64.exe");
        temp.CreateFile(
            "Steam/steamapps/libraryfolders.vdf",
            $$"""
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"{{steamLibrary.Replace(@"\", @"\\")}}"
            	}
            }
            """);

        // Bindings folder with a version-ordering case and a StartPreset file.
        temp.CreateFile("Bindings/Custom.4.9.binds");
        temp.CreateFile("Bindings/Custom.4.10.binds");
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\nCustom\nXbox360Controller\nCustom\n");

        // EDHM-UI, Odyssey data only, at a custom UserDataFolder.
        var edhmUserData = temp.CreateSubdirectory("MyEdhmData");
        temp.CreateFile("MyEdhmData/ODYSS/EDHM/EDHM-Ini/ThemeSettings.json", "{}");
        temp.CreateFile(
            "EDHM-UI-V3/resources/data/Settings.json",
            $$"""{ "UserDataFolder": "{{edhmUserData.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var environment = new PathDiscoveryEnvironment(
            SteamRoots: new[] { steamRoot },
            EpicManifestsDirectory: temp.Combine("NoEpic"),
            FrontierRegistryInstallPath: null,
            FrontierDefaultInstallRoot: temp.Combine("NoFrontier"),
            BindingsDirectory: temp.Combine("Bindings"),
            EdhmSettingsJsonPath: temp.Combine("EDHM-UI-V3", "resources", "data", "Settings.json"),
            EnvironmentVariables: new Dictionary<string, string>(),
            LocalAppData: temp.CreateSubdirectory("LocalAppData"));

        var log = new DiagnosticRingBuffer(500);
        var result = PathDiscoveryService.Discover(environment, log);

        Assert.Single(result.EliteInstallations);
        Assert.Equal(EliteEdition.Odyssey, result.EliteInstallations[0].Edition);

        Assert.Equal(new BindsVersion(4, 10), result.Bindings.LatestVersion);
        Assert.Single(result.Bindings.StartPresetFilePaths);

        // BindingsSelection (ref/docs/bindings-source.md) is wired end to
        // end here too: StartPreset's first name ("Custom") resolves to a
        // real commander file, and it happens to already be the highest
        // version - the ControlSchemes/stock wiring itself is covered by a
        // dedicated test below, since this environment has no ControlSchemes
        // directory at all.
        Assert.Equal(temp.Combine("Bindings", "Custom.4.10.binds"), result.BindingsSelection.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.CommanderAuthored, result.BindingsSelection.Method);

        Assert.True(result.Edhm.SettingsFound);
        Assert.Single(result.Edhm.Editions);

        // "Not a summary" - the actual candidates must appear, in order,
        // not just a final verdict.
        var messages = log.Snapshot().Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.Contains("Steam libraryfolders.vdf", StringComparison.Ordinal) && m.Contains("found", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Elite product (Steam)", StringComparison.Ordinal) && m.Contains("Odyssey", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Binds file", StringComparison.Ordinal) && m.Contains("version 4.9", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Binds file", StringComparison.Ordinal) && m.Contains("version 4.10", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Start preset file", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("EDHM-UI settings", StringComparison.Ordinal) && m.Contains("found", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("EDHM-UI ODYSS data", StringComparison.Ordinal) && m.Contains("found", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("LunaPanel logs directory", StringComparison.Ordinal) && m.Contains("ensured", StringComparison.Ordinal));
    }

    /// <summary>
    /// The install root <see cref="PresetSelector"/>'s stock resolution needs
    /// (<c>Products\elite-dangerous-odyssey-64\ControlSchemes</c>) comes from
    /// the SAME Elite installation this service already discovers - this
    /// test exists specifically to prove that wiring, not just
    /// <see cref="PresetSelector"/>'s own logic (covered directly in
    /// <c>PresetSelectorTests</c>).
    /// </summary>
    [Fact]
    public void Discover_OdysseyInstallFound_BindingsSelectionResolvesAStockControlScheme_WhenNoCommanderFileExists()
    {
        using var temp = TempDirectory.Create();

        var steamRoot = temp.CreateSubdirectory("Steam");
        var steamLibrary = temp.CreateSubdirectory("SteamLibrary");
        var productDir = temp.CreateSubdirectory("SteamLibrary/steamapps/common/Elite Dangerous/Products/elite-dangerous-odyssey-64");
        temp.CreateFile("SteamLibrary/steamapps/common/Elite Dangerous/Products/elite-dangerous-odyssey-64/EliteDangerous64.exe");
        temp.CreateFile(
            "SteamLibrary/steamapps/common/Elite Dangerous/Products/elite-dangerous-odyssey-64/ControlSchemes/KeyboardMouseOnly.binds",
            """<Root PresetName="KeyboardMouseOnly" SortOrder="0"></Root>""");
        temp.CreateFile(
            "Steam/steamapps/libraryfolders.vdf",
            $$"""
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"{{steamLibrary.Replace(@"\", @"\\")}}"
            	}
            }
            """);

        // No commander bindings file for this name at all - only StartPreset
        // naming the stock scheme.
        temp.CreateFile("Bindings/StartPreset.4.start", "KeyboardMouseOnly\n");

        var environment = new PathDiscoveryEnvironment(
            SteamRoots: new[] { steamRoot },
            EpicManifestsDirectory: temp.Combine("NoEpic"),
            FrontierRegistryInstallPath: null,
            FrontierDefaultInstallRoot: temp.Combine("NoFrontier"),
            BindingsDirectory: temp.Combine("Bindings"),
            EdhmSettingsJsonPath: temp.Combine("NoEdhm", "Settings.json"),
            EnvironmentVariables: new Dictionary<string, string>(),
            LocalAppData: temp.CreateSubdirectory("LocalAppData"));

        var result = PathDiscoveryService.Discover(environment, new DiagnosticRingBuffer(500));

        Assert.Equal(
            Path.Combine(productDir, "ControlSchemes", "KeyboardMouseOnly.binds"),
            result.BindingsSelection.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Stock, result.BindingsSelection.Method);
        Assert.Equal(PresetOrigin.Stock, result.BindingsSelection.Origin);
    }

    [Fact]
    public void Discover_StatusJsonDirectoryNotConfigured_ReportsNotFound()
    {
        using var temp = TempDirectory.Create();

        var result = PathDiscoveryService.Discover(EmptyEnvironment(temp), new DiagnosticRingBuffer(500));

        Assert.Null(result.StatusJson.Directory);
    }

    [Fact]
    public void Discover_StatusJsonDirectoryExists_IsReportedFound()
    {
        using var temp = TempDirectory.Create();
        var statusDir = temp.CreateSubdirectory("StatusDir");
        var environment = EmptyEnvironment(temp) with { StatusJsonDirectory = statusDir };

        var result = PathDiscoveryService.Discover(environment, new DiagnosticRingBuffer(500));

        Assert.Equal(statusDir, result.StatusJson.Directory);
    }

    /// <summary>
    /// A manual override replaces the whole Steam/Epic/Frontier sweep rather
    /// than adding to it - the environment here deliberately ALSO has a real
    /// Steam install a plain sweep would find, at a different path than the
    /// override, so finding only the override's install proves the sweep
    /// was skipped entirely rather than merged.
    /// </summary>
    [Fact]
    public void Discover_EliteInstallPathOverrideSet_SkipsStorefrontSweep_AndReportsSourceManual()
    {
        using var temp = TempDirectory.Create();

        // What a plain Steam sweep would find, if it ran.
        var steamRoot = temp.CreateSubdirectory("Steam");
        var steamLibrary = temp.CreateSubdirectory("SteamLibrary");
        temp.CreateFile("SteamLibrary/steamapps/common/Elite Dangerous/Products/od/EliteDangerous64.exe");
        temp.CreateFile(
            "Steam/steamapps/libraryfolders.vdf",
            $$"""
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"{{steamLibrary.Replace(@"\", @"\\")}}"
            	}
            }
            """);

        // The commander's own manual pick - a completely different install
        // root than the Steam one above.
        var manualRoot = temp.CreateSubdirectory("ManualInstall");
        temp.CreateFile("ManualInstall/Products/manual/EliteDangerous32.exe");

        var environment = new PathDiscoveryEnvironment(
            SteamRoots: new[] { steamRoot },
            EpicManifestsDirectory: temp.Combine("NoEpic"),
            FrontierRegistryInstallPath: null,
            FrontierDefaultInstallRoot: temp.Combine("NoFrontier"),
            BindingsDirectory: temp.Combine("NoBindings"),
            EdhmSettingsJsonPath: temp.Combine("NoEdhm", "Settings.json"),
            EnvironmentVariables: new Dictionary<string, string>(),
            LocalAppData: temp.CreateSubdirectory("LocalAppData"),
            EliteInstallPathOverride: manualRoot);

        var result = PathDiscoveryService.Discover(environment, new DiagnosticRingBuffer(500));

        var found = Assert.Single(result.EliteInstallations);
        Assert.Equal(EliteSource.Manual, found.Source);
        Assert.Equal(Path.Combine(manualRoot, "Products", "manual"), found.ProductPath);
        Assert.Equal(EliteEdition.Horizons, found.Edition);
    }

    [Fact]
    public void Discover_NoEliteInstallPathOverride_FallsThroughToTodaysSweep_Unchanged()
    {
        using var temp = TempDirectory.Create();
        var steamRoot = temp.CreateSubdirectory("Steam");
        var steamLibrary = temp.CreateSubdirectory("SteamLibrary");
        temp.CreateFile("SteamLibrary/steamapps/common/Elite Dangerous/Products/od/EliteDangerous64.exe");
        temp.CreateFile(
            "Steam/steamapps/libraryfolders.vdf",
            $$"""
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"{{steamLibrary.Replace(@"\", @"\\")}}"
            	}
            }
            """);

        var environment = EmptyEnvironment(temp) with { SteamRoots = new[] { steamRoot } };

        var result = PathDiscoveryService.Discover(environment, new DiagnosticRingBuffer(500));

        var found = Assert.Single(result.EliteInstallations);
        Assert.Equal(EliteSource.Steam, found.Source);
    }

    /// <summary>
    /// Proves <see cref="EdhmDiscovery"/> needs no changes for a manually
    /// chosen settings file: <see cref="PathDiscoveryEnvironment.EdhmSettingsJsonPath"/>
    /// resolves identically regardless of whether the value came from
    /// auto-detection or a commander's manual pick - there is no separate
    /// override field for EDHM (unlike Elite) because the caller
    /// (<c>RealServerEnvironment.Build</c>) simply substitutes the override
    /// value in place of the guessed default before this record is built.
    /// </summary>
    [Fact]
    public void Discover_EdhmSettingsJsonPathAtAManuallyChosenLocation_ResolvesExactlyAsAutoGuessedPathWould()
    {
        using var temp = TempDirectory.Create();
        var edhmUserData = temp.CreateSubdirectory("MyEdhmData");
        temp.CreateFile("MyEdhmData/ODYSS/EDHM/EDHM-Ini/ThemeSettings.json", "{}");

        // A deliberately non-conventional location, standing in for a
        // commander's manual "Select..." pick rather than the guessed
        // %LOCALAPPDATA%\EDHM-UI-V3\... default.
        temp.CreateFile(
            "SomeOtherFolder/Settings.json",
            $$"""{ "UserDataFolder": "{{edhmUserData.Replace(@"\", @"\\")}}", "ActiveInstance": "Steam (Odyssey (Live))" }""");

        var environment = EmptyEnvironment(temp) with
        {
            EdhmSettingsJsonPath = temp.Combine("SomeOtherFolder", "Settings.json"),
        };

        var result = PathDiscoveryService.Discover(environment, new DiagnosticRingBuffer(500));

        Assert.True(result.Edhm.SettingsFound);
        Assert.Equal(edhmUserData, result.Edhm.UserDataFolder);
        Assert.Single(result.Edhm.Editions);
    }

    [Fact]
    public void Discover_SameInstallReachableTwice_IsDedupedToOneEntry()
    {
        using var temp = TempDirectory.Create();
        var sharedInstall = temp.CreateSubdirectory("SharedInstall");
        temp.CreateFile("SharedInstall/Products/p/EliteDangerous64.exe");

        // Both the "registry" path and the "default" path resolve to the
        // exact same folder - a realistic case if a player's Frontier
        // launcher install happens to sit at the conventional default.
        var environment = EmptyEnvironment(temp) with
        {
            FrontierRegistryInstallPath = sharedInstall,
            FrontierDefaultInstallRoot = sharedInstall,
        };

        var log = new DiagnosticRingBuffer(500);
        var result = PathDiscoveryService.Discover(environment, log);

        Assert.Single(result.EliteInstallations);
    }
}
