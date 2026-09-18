using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="StatusWindowPositionStore"/> against real temp
/// directories under the test output (never the repo tree or the user's real
/// profile) - same discipline as
/// <c>LunaPanel.Tests.Network.PortSettingsStoreTests</c>, which this class
/// mirrors, with one deliberate difference: <see cref="StatusWindowPositionStore.Load"/>
/// has no shipped default to fall back to, so "no file yet" and "corrupt
/// file" both assert null rather than a default value.
/// </summary>
public class StatusWindowPositionStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "status-window-position-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFileYet_ReturnsNull()
    {
        var store = new StatusWindowPositionStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = new StatusWindowPositionStore(NewTempDir(), new CapturingDiagnosticLog());
        var saved = new StatusWindowPosition(1234, -56);

        store.Save(saved);
        var result = store.Load();

        Assert.NotNull(result);
        Assert.Equal(saved.X, result!.X);
        Assert.Equal(saved.Y, result.Y);
    }

    [Fact]
    public void Save_ThenLoad_FromAFreshStoreInstance_SurvivesARestart()
    {
        var dir = NewTempDir();
        var saved = new StatusWindowPosition(200, 300);
        new StatusWindowPositionStore(dir, new CapturingDiagnosticLog()).Save(saved);

        var reopened = new StatusWindowPositionStore(dir, new CapturingDiagnosticLog());
        var result = reopened.Load();

        Assert.NotNull(result);
        Assert.Equal(saved.X, result!.X);
        Assert.Equal(saved.Y, result.Y);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "status-window-position.json"), "{ not valid json");
        var store = new StatusWindowPositionStore(dir, new CapturingDiagnosticLog());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_MissingXY_ReturnsNull_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "status-window-position.json"), "{\"foo\": 1}");
        var store = new StatusWindowPositionStore(dir, new CapturingDiagnosticLog());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesRatherThanAppending()
    {
        var store = new StatusWindowPositionStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new StatusWindowPosition(10, 10));
        store.Save(new StatusWindowPosition(99, 88));

        var result = store.Load();
        Assert.NotNull(result);
        Assert.Equal(99, result!.X);
        Assert.Equal(88, result.Y);
    }
}
