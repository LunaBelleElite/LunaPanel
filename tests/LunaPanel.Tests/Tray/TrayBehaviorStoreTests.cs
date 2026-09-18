using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Drives <see cref="TrayBehaviorStore"/> against real temp directories under
/// the test output (never the repo tree or the user's real profile) - same
/// discipline as <c>LunaPanel.Tests.Network.PortSettingsStoreTests</c>, which
/// this class mirrors.
/// </summary>
public class TrayBehaviorStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "tray-behavior-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Default_MinimizeToTrayEnabled_IsTrue()
    {
        Assert.True(TrayBehaviorSettings.Default.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Load_NoFileYet_ReturnsShippedDefault()
    {
        var store = new TrayBehaviorStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.True(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_False()
    {
        var store = new TrayBehaviorStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new TrayBehaviorSettings(false));
        var result = store.Load();

        Assert.False(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Save_ThenLoad_FromAFreshStoreInstance_SurvivesARestart()
    {
        var dir = NewTempDir();
        new TrayBehaviorStore(dir, new CapturingDiagnosticLog()).Save(new TrayBehaviorSettings(false));

        var reopened = new TrayBehaviorStore(dir, new CapturingDiagnosticLog());
        var result = reopened.Load();

        Assert.False(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefault_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "tray-behavior.json"), "{ not valid json");
        var store = new TrayBehaviorStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.True(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Load_PropertyIsWrongJsonType_FallsBackToDefault_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "tray-behavior.json"), "{\"minimizeToTrayEnabled\": \"yes\"}");
        var store = new TrayBehaviorStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.True(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Load_MissingProperty_FallsBackToDefault()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "tray-behavior.json"), "{}");
        var store = new TrayBehaviorStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.True(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesRatherThanAppending()
    {
        var store = new TrayBehaviorStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new TrayBehaviorSettings(false));
        store.Save(new TrayBehaviorSettings(true));

        var result = store.Load();
        Assert.True(result.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Save_RaisesSaved_CarryingTheValueJustSaved()
    {
        var store = new TrayBehaviorStore(NewTempDir(), new CapturingDiagnosticLog());
        var raised = new List<TrayBehaviorSettings>();
        store.Saved += s => raised.Add(s);

        store.Save(new TrayBehaviorSettings(false));

        var only = Assert.Single(raised);
        Assert.False(only.MinimizeToTrayEnabled);
    }

    [Fact]
    public void Load_RaisesNothing()
    {
        var store = new TrayBehaviorStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(new TrayBehaviorSettings(false));
        var raised = 0;
        store.Saved += _ => raised++;

        store.Load();
        store.Load();

        Assert.Equal(0, raised);
    }
}
