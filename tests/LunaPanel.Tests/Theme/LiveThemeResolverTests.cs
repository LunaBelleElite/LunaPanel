using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Theme;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Drives <see cref="LiveThemeResolver"/> against real temp files (never the
/// repo tree or the user's real profile) - the one place in
/// <c>LunaPanel.Server</c> that assembles <see cref="HudThemeResolver.Resolve"/>'s
/// five inputs from files <see cref="PathDiscoveryService"/> already found.
/// </summary>
public class LiveThemeResolverTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "live-theme-resolver", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FixturePath(params string[] segments) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(segments).ToArray());

    private static PathDiscoveryEnvironment EnvironmentWithOverridePath(string? overridePath) => new(
        SteamRoots: Array.Empty<string>(),
        EpicManifestsDirectory: @"C:\fake\NoEpic",
        FrontierRegistryInstallPath: null,
        FrontierDefaultInstallRoot: @"C:\fake\NoFrontier",
        BindingsDirectory: @"C:\fake\NoBindings",
        EdhmSettingsJsonPath: @"C:\fake\NoEdhm\Settings.json",
        EnvironmentVariables: new Dictionary<string, string>(),
        LocalAppData: @"C:\fake\LocalAppData",
        IsDevBuild: false,
        GraphicsConfigurationOverridePath: overridePath);

    private static PathDiscoveryResult DiscoveryWithEdhm(string? activeInstance, IReadOnlyList<EdhmEditionData> editions) => new(
        EliteInstallations: Array.Empty<EliteInstallation>(),
        Bindings: new BindingsDiscoveryResult(null, null, Array.Empty<string>()),
        BindingsSelection: new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null),
        Edhm: new EdhmDiscoveryResult(editions.Count > 0, @"C:\fake\EDHM_UI", activeInstance, editions),
        LunaPanelDirectories: new LunaPanelDirectoryLayout(@"C:\fake\Logs", @"C:\fake\Layouts", @"C:\fake\Pairing\device-registry.json"),
        StatusJson: new StatusJsonDiscoveryResult(null));

    [Fact]
    public void Resolve_NoEdhm_NoOverride_FallsBackToStock()
    {
        var log = new CapturingDiagnosticLog();
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(null),
            DiscoveryWithEdhm(null, Array.Empty<EdhmEditionData>()),
            log);

        var theme = resolver.Resolve();

        Assert.Equal(HudThemeSource.Stock, theme.Source);
    }

    [Fact]
    public void Resolve_EdhmThemeSettingsAndAdvancedIni_ResolvesViaEdhm_PerElementColours()
    {
        var iniDir = NewTempDir();
        File.Copy(FixturePath("edhm", "Advanced.sample.ini"), Path.Combine(iniDir, "Advanced.ini"));
        var themeSettingsPath = Path.Combine(iniDir, "ThemeSettings.json");
        File.Copy(FixturePath("edhm", "ThemeSettings.sample.json"), themeSettingsPath);

        var edition = new EdhmEditionData("ODYSS", iniDir, themeSettingsPath, XmlProfileIniPath: null);
        var log = new CapturingDiagnosticLog();
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(null),
            DiscoveryWithEdhm("ODYSS", new[] { edition }),
            log);

        var theme = resolver.Resolve();

        Assert.Equal(HudThemeSource.Edhm, theme.Source);
    }

    [Fact]
    public void Resolve_NoEdhm_OverrideXmlPresent_ResolvesViaGraphicsConfigurationOverride()
    {
        var dir = NewTempDir();
        var overridePath = Path.Combine(dir, "GraphicsConfigurationOverride.xml");
        File.Copy(FixturePath("graphics", "guicolour-custom.xml"), overridePath);

        var log = new CapturingDiagnosticLog();
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(overridePath),
            DiscoveryWithEdhm(null, Array.Empty<EdhmEditionData>()),
            log);

        var theme = resolver.Resolve();

        Assert.Equal(HudThemeSource.GraphicsConfigurationOverride, theme.Source);
    }

    [Fact]
    public void Resolve_ActiveInstanceNamesAnEditionNotInTheList_FallsBackToFirstAvailableEdition()
    {
        var iniDir = NewTempDir();
        File.Copy(FixturePath("edhm", "Advanced.sample.ini"), Path.Combine(iniDir, "Advanced.ini"));
        var themeSettingsPath = Path.Combine(iniDir, "ThemeSettings.json");
        File.Copy(FixturePath("edhm", "ThemeSettings.sample.json"), themeSettingsPath);

        var edition = new EdhmEditionData("ODYSS", iniDir, themeSettingsPath, XmlProfileIniPath: null);
        var log = new CapturingDiagnosticLog();
        // ActiveInstance names an edition that isn't in the discovered list
        // at all - the resolver still has one real edition to fall back to
        // rather than resolving nothing.
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(null),
            DiscoveryWithEdhm("HORIZ", new[] { edition }),
            log);

        var theme = resolver.Resolve();

        Assert.Equal(HudThemeSource.Edhm, theme.Source);
    }

    [Fact]
    public void Resolve_MissingIniDirectory_DoesNotThrow_DegradesPastEdhm()
    {
        var themeSettingsPath = Path.Combine(NewTempDir(), "ThemeSettings.json");
        File.Copy(FixturePath("edhm", "ThemeSettings.sample.json"), themeSettingsPath);

        // IniDirectory points nowhere real - neither title family can
        // resolve without an "Advanced" ini to read, so EDHM is abandoned
        // for this theme.
        var edition = new EdhmEditionData("ODYSS", @"C:\fake\does-not-exist-ini-dir", themeSettingsPath, XmlProfileIniPath: null);
        var log = new CapturingDiagnosticLog();
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(null),
            DiscoveryWithEdhm("ODYSS", new[] { edition }),
            log);

        var theme = resolver.Resolve();

        Assert.Equal(HudThemeSource.Stock, theme.Source);
    }

    // ---------------------------------------------------------------
    // GetDiscoveredColours - feeds the settings gear's "From your HUD"
    // picker group. Re-reads the same real files as Resolve.
    // ---------------------------------------------------------------

    [Fact]
    public void GetDiscoveredColours_EdhmThemeSettingsAndAdvancedIni_ReturnsTheDistinctResolvedColours()
    {
        var iniDir = NewTempDir();
        File.Copy(FixturePath("edhm", "Advanced.sample.ini"), Path.Combine(iniDir, "Advanced.ini"));
        var themeSettingsPath = Path.Combine(iniDir, "ThemeSettings.json");
        File.Copy(FixturePath("edhm", "ThemeSettings.sample.json"), themeSettingsPath);

        var edition = new EdhmEditionData("ODYSS", iniDir, themeSettingsPath, XmlProfileIniPath: null);
        var log = new CapturingDiagnosticLog();
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(null),
            DiscoveryWithEdhm("ODYSS", new[] { edition }),
            log);

        var colours = resolver.GetDiscoveredColours();

        // Same fixture set HudThemeResolverTests pins directly against the
        // pure function - proven here through the real file-reading path
        // instead.
        Assert.Equal(2, colours.Count);
        Assert.Contains(colours, c => c.Label == "Chat Panel Text");
        Assert.Contains(colours, c => c.Label == "Radar Grid");
    }

    [Fact]
    public void GetDiscoveredColours_NoEdhm_ReturnsEmpty()
    {
        var log = new CapturingDiagnosticLog();
        var resolver = new LiveThemeResolver(
            EnvironmentWithOverridePath(null),
            DiscoveryWithEdhm(null, Array.Empty<EdhmEditionData>()),
            log);

        var colours = resolver.GetDiscoveredColours();

        Assert.Empty(colours);
    }
}
