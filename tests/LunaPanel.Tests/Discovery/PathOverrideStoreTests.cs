using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Drives <see cref="PathOverrideStore"/> against real temp directories under
/// the test output (never the repo tree or the user's real profile) - same
/// discipline as <c>LunaPanel.Tests.Network.PortSettingsStoreTests</c>, which
/// this class mirrors.
/// </summary>
public class PathOverrideStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "path-override-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFileYet_ReturnsBothFieldsNull()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Null(result.EliteInstallPath);
        Assert.Null(result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Load_NoFileYet_EliteSetupAcknowledgedDefaultsFalse()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.False(result.EliteSetupAcknowledged);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToBothNull_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "path-overrides.json"), "{ not valid json");
        var log = new CapturingDiagnosticLog();
        var store = new PathOverrideStore(dir, log);

        var result = store.Load();

        Assert.Null(result.EliteInstallPath);
        Assert.Null(result.EdhmSettingsJsonPath);
        Assert.Contains(log.Events, e => e.Message.Contains("corrupt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_CorruptFile_EliteSetupAcknowledgedFallsBackToFalse_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "path-overrides.json"), "{ not valid json");
        var store = new PathOverrideStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.False(result.EliteSetupAcknowledged);
    }

    [Fact]
    public void Save_ThenLoad_EliteSetupAcknowledgedRoundTrips_IndependentlyOfPaths()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new PathOverrideSettings(null, null, EliteSetupAcknowledged: true));
        var result = store.Load();

        Assert.True(result.EliteSetupAcknowledged);
        Assert.Null(result.EliteInstallPath);
        Assert.Null(result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Save_ThenLoad_EliteSetupAcknowledgedFalse_RoundTrips()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new PathOverrideSettings(@"C:\Games\Elite", null, EliteSetupAcknowledged: false));
        var result = store.Load();

        Assert.False(result.EliteSetupAcknowledged);
        Assert.Equal(@"C:\Games\Elite", result.EliteInstallPath);
    }

    [Fact]
    public void Save_ThenLoad_EliteSetupAcknowledged_FromAFreshStoreInstance_SurvivesARestart()
    {
        var dir = NewTempDir();
        new PathOverrideStore(dir, new CapturingDiagnosticLog())
            .Save(new PathOverrideSettings(null, null, EliteSetupAcknowledged: true));

        var reopened = new PathOverrideStore(dir, new CapturingDiagnosticLog());
        var result = reopened.Load();

        Assert.True(result.EliteSetupAcknowledged);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_BothFieldsIndependently()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());
        var saved = new PathOverrideSettings(@"C:\Games\Elite", @"C:\Users\Cmdr\AppData\Local\EDHM-UI-V3\resources\data\Settings.json");

        store.Save(saved);
        var result = store.Load();

        Assert.Equal(saved.EliteInstallPath, result.EliteInstallPath);
        Assert.Equal(saved.EdhmSettingsJsonPath, result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Save_OnlyEliteInstallPath_LeavesEdhmSettingsJsonPathNull()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new PathOverrideSettings(@"C:\Games\Elite", null));
        var result = store.Load();

        Assert.Equal(@"C:\Games\Elite", result.EliteInstallPath);
        Assert.Null(result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Save_OnlyEdhmSettingsJsonPath_LeavesEliteInstallPathNull()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new PathOverrideSettings(null, @"C:\EDHM\Settings.json"));
        var result = store.Load();

        Assert.Null(result.EliteInstallPath);
        Assert.Equal(@"C:\EDHM\Settings.json", result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Save_ThenLoad_FromAFreshStoreInstance_SurvivesARestart()
    {
        var dir = NewTempDir();
        var saved = new PathOverrideSettings(@"C:\Games\Elite", @"C:\EDHM\Settings.json");
        new PathOverrideStore(dir, new CapturingDiagnosticLog()).Save(saved);

        var reopened = new PathOverrideStore(dir, new CapturingDiagnosticLog());
        var result = reopened.Load();

        Assert.Equal(saved.EliteInstallPath, result.EliteInstallPath);
        Assert.Equal(saved.EdhmSettingsJsonPath, result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesRatherThanAppending()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new PathOverrideSettings(@"C:\First", null));
        store.Save(new PathOverrideSettings(@"C:\Second", @"C:\EDHM\Settings.json"));

        var result = store.Load();
        Assert.Equal(@"C:\Second", result.EliteInstallPath);
        Assert.Equal(@"C:\EDHM\Settings.json", result.EdhmSettingsJsonPath);
    }

    [Fact]
    public void Save_RaisesSaved_CarryingTheValueJustSaved()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());
        var raised = new List<PathOverrideSettings>();
        store.Saved += s => raised.Add(s);

        store.Save(new PathOverrideSettings(@"C:\Games\Elite", null));

        var only = Assert.Single(raised);
        Assert.Equal(@"C:\Games\Elite", only.EliteInstallPath);
    }

    [Fact]
    public void Load_RaisesNothing()
    {
        var store = new PathOverrideStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(new PathOverrideSettings(@"C:\Games\Elite", null));
        var raised = 0;
        store.Saved += _ => raised++;

        store.Load();
        store.Load();

        Assert.Equal(0, raised);
    }
}
