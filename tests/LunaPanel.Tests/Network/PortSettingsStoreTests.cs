using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Network;

namespace LunaPanel.Tests.Network;

/// <summary>
/// Drives <see cref="PortSettingsStore"/> against real temp directories under
/// the test output (never the repo tree or the user's real profile) - same
/// discipline as <c>LunaPanel.Tests.Macros.MacroTimingSettingsStoreTests</c>,
/// which this class mirrors.
/// </summary>
public class PortSettingsStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "port-settings-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFileYet_ReturnsShippedDefault()
    {
        var store = new PortSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(PortSettings.DefaultPort, result.Port);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_NonDefaultValue()
    {
        var store = new PortSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        var saved = new PortSettings(8080);

        store.Save(saved);
        var result = store.Load();

        Assert.Equal(saved.Port, result.Port);
    }

    [Fact]
    public void Save_ThenLoad_FromAFreshStoreInstance_SurvivesARestart()
    {
        var dir = NewTempDir();
        var saved = new PortSettings(9443);
        new PortSettingsStore(dir, new CapturingDiagnosticLog()).Save(saved);

        var reopened = new PortSettingsStore(dir, new CapturingDiagnosticLog());
        var result = reopened.Load();

        Assert.Equal(saved.Port, result.Port);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefault_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "port-settings.json"), "{ not valid json");
        var store = new PortSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(PortSettings.DefaultPort, result.Port);
    }

    [Fact]
    public void Load_OutOfRangeValueInFile_FallsBackToDefault_RatherThanReturningIt()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "port-settings.json"), "{\"port\": 80}");
        var store = new PortSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(PortSettings.DefaultPort, result.Port);
    }

    [Fact]
    public void Load_PortAboveMax_FallsBackToDefault()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "port-settings.json"), "{\"port\": 70000}");
        var store = new PortSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(PortSettings.DefaultPort, result.Port);
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesRatherThanAppending()
    {
        var store = new PortSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new PortSettings(8080));
        store.Save(new PortSettings(9443));

        var result = store.Load();
        Assert.Equal(9443, result.Port);
    }

    [Fact]
    public void Save_RaisesSaved_CarryingTheValueJustSaved()
    {
        var store = new PortSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        var raised = new List<PortSettings>();
        store.Saved += s => raised.Add(s);

        store.Save(new PortSettings(8080));

        var only = Assert.Single(raised);
        Assert.Equal(8080, only.Port);
    }

    [Fact]
    public void Load_RaisesNothing()
    {
        var store = new PortSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(new PortSettings(8080));
        var raised = 0;
        store.Saved += _ => raised++;

        store.Load();
        store.Load();

        Assert.Equal(0, raised);
    }
}
