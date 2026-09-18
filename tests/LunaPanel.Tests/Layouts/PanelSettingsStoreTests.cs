using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="PanelSettingsStore"/> against real temp directories
/// under the test output (never the repo tree or the user's real profile) -
/// same discipline as <c>LunaPanel.Tests.Theme.ThemeOverrideStoreTests</c>.
/// </summary>
public class PanelSettingsStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "panel-settings-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFileForDevice_ReturnsDefault_MergeExpandOn()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.True(result.MergeExpand);
    }

    [Fact]
    public void Save_MergeExpandFalse_ThenLoad_RoundTrips()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: false, ShowMacroStepResults: true));
        var result = store.Load("device-a");

        Assert.False(result.MergeExpand);
    }

    [Fact]
    public void Save_MergeExpandTrue_ThenLoad_RoundTrips()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: true, ShowMacroStepResults: true));
        var result = store.Load("device-a");

        Assert.True(result.MergeExpand);
    }

    [Fact]
    public void Save_IsPerDevice_OneDevicesSettingDoesNotAffectAnother()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: false, ShowMacroStepResults: true));

        Assert.True(store.Load("device-b").MergeExpand);
        Assert.False(store.Load("device-a").MergeExpand);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefault_RatherThanThrowing()
    {
        var dir = NewTempDir();
        var mainPath = Path.Combine(dir, "panel-settings-device-a.json");
        File.WriteAllText(mainPath, "{ not valid json");
        var store = new PanelSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.True(result.MergeExpand);
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesRatherThanAppending()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: false, ShowMacroStepResults: true));
        store.Save("device-a", new PanelSettings(MergeExpand: true, ShowMacroStepResults: true));

        Assert.True(store.Load("device-a").MergeExpand);
    }

    [Fact]
    public void Load_NoFileForDevice_ReturnsDefault_ShowMacroStepResultsOff()
    {
        // [2026-09-17] Default flipped: a device with no settings file yet
        // no longer opens the per-step result sheet on every macro press.
        // Distinct from Load_OldFileWithNoShowMacroStepResultsKey below,
        // which deliberately keeps defaulting an EXISTING file's missing key
        // to true - a device already configured before this setting existed
        // keeps behaving as it always did.
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.False(result.ShowMacroStepResults);
    }

    [Fact]
    public void Save_ShowMacroStepResultsFalse_ThenLoad_RoundTrips()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: true, ShowMacroStepResults: false));
        var result = store.Load("device-a");

        Assert.False(result.ShowMacroStepResults);
    }

    [Fact]
    public void Save_ShowMacroStepResultsTrue_ThenLoad_RoundTrips()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: true, ShowMacroStepResults: true));
        var result = store.Load("device-a");

        Assert.True(result.ShowMacroStepResults);
    }

    /// <summary>
    /// A settings file saved before this field existed - <c>mergeExpand</c>
    /// only, no <c>showMacroStepResults</c> key at all - must still load
    /// with the new field defaulting to <see langword="true"/>, or every
    /// device that saved a settings file before this feature shipped would
    /// silently lose the step-result sheet on next load. **Deliberately
    /// unaffected by the 2026-09-17 default flip** (see
    /// <c>Load_NoFileForDevice_ReturnsDefault_ShowMacroStepResultsOff</c>
    /// above): a device already configured before this setting existed was
    /// always showing results, so this preserves that device's actual
    /// experience rather than applying the new default retroactively to a
    /// file that predates it.
    /// </summary>
    [Fact]
    public void Load_OldFileWithNoShowMacroStepResultsKey_DefaultsThatFieldToTrue()
    {
        var dir = NewTempDir();
        var mainPath = Path.Combine(dir, "panel-settings-device-a.json");
        File.WriteAllText(mainPath, "{\"mergeExpand\":false}");
        var store = new PanelSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.False(result.MergeExpand);
        Assert.True(result.ShowMacroStepResults);
    }

    [Fact]
    public void Default_AutoSwitchEnabled_IsTrue()
    {
        Assert.True(PanelSettings.Default.AutoSwitchEnabled);
    }

    [Fact]
    public void Load_NoFileForDevice_ReturnsDefault_AutoSwitchEnabledOn()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.True(result.AutoSwitchEnabled);
    }

    [Fact]
    public void Save_AutoSwitchEnabledFalse_ThenLoad_RoundTrips()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: true, ShowMacroStepResults: true, AutoSwitchEnabled: false));
        var result = store.Load("device-a");

        Assert.False(result.AutoSwitchEnabled);
    }

    [Fact]
    public void Save_AutoSwitchEnabledTrue_ThenLoad_RoundTrips()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: true, ShowMacroStepResults: true, AutoSwitchEnabled: true));
        var result = store.Load("device-a");

        Assert.True(result.AutoSwitchEnabled);
    }

    // Independence from the other two fields: flipping AutoSwitchEnabled
    // does not disturb MergeExpand/ShowMacroStepResults, and vice versa -
    // the three are separate JSON keys, not a packed bitfield.
    [Fact]
    public void Save_AutoSwitchEnabledFalse_DoesNotDisturbOtherFields()
    {
        var store = new PanelSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save("device-a", new PanelSettings(MergeExpand: false, ShowMacroStepResults: true, AutoSwitchEnabled: false));
        var result = store.Load("device-a");

        Assert.False(result.MergeExpand);
        Assert.True(result.ShowMacroStepResults);
        Assert.False(result.AutoSwitchEnabled);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefault_AutoSwitchEnabledOn()
    {
        var dir = NewTempDir();
        var mainPath = Path.Combine(dir, "panel-settings-device-a.json");
        File.WriteAllText(mainPath, "{ not valid json");
        var store = new PanelSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.True(result.AutoSwitchEnabled);
    }

    /// <summary>
    /// A settings file saved before this field existed - no
    /// <c>autoSwitchEnabled</c> key at all - must still load with the new
    /// field defaulting to <see langword="true"/>, matching
    /// <see cref="PanelSettings.Default"/> exactly, deliberately NOT copied
    /// from <c>ShowMacroStepResults</c>'s own old-file-defaults-to-true
    /// quirk above (which predates this field and doesn't match
    /// <see cref="PanelSettings.Default"/> either).
    /// </summary>
    [Fact]
    public void Load_OldFileWithNoAutoSwitchEnabledKey_DefaultsThatFieldToTrue()
    {
        var dir = NewTempDir();
        var mainPath = Path.Combine(dir, "panel-settings-device-a.json");
        File.WriteAllText(mainPath, "{\"mergeExpand\":false,\"showMacroStepResults\":false}");
        var store = new PanelSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.False(result.MergeExpand);
        Assert.False(result.ShowMacroStepResults);
        Assert.True(result.AutoSwitchEnabled);
    }
}
