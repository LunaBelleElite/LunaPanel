using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Updates;

namespace LunaPanel.Tests.Updates;

/// <summary>
/// Drives <see cref="LastLaunchedVersionStore"/> against real temp
/// directories under the test output (never the repo tree or the user's real
/// profile) - same discipline as
/// <c>LunaPanel.Tests.Discovery.PathOverrideStoreTests</c>, which this class
/// mirrors.
///
/// The thing actually being pinned is the three-way decision on startup:
/// no file at all is a first install and says nothing; a different version on
/// file means this launch followed an update and is the one case that speaks;
/// the same version is an ordinary launch. Getting that wrong either nags on
/// every single launch or never confirms an update happened at all.
/// </summary>
public class LastLaunchedVersionStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private const string FileName = "last-launched-version.txt";

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "last-launched-version-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFileYet_IsNull()
    {
        var store = new LastLaunchedVersionStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var dir = NewTempDir();
        new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog()).Save("ver-0.53.0.2-dev");

        var reopened = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        Assert.Equal("ver-0.53.0.2-dev", reopened.Load());
    }

    [Fact]
    public void Save_WritesPlainTextAtTheDocumentedFileName()
    {
        var dir = NewTempDir();

        new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog()).Save("ver-0.54.0.0");

        Assert.Equal("ver-0.54.0.0", File.ReadAllText(Path.Combine(dir, FileName)).Trim());
    }

    [Fact]
    public void Save_Twice_OverwritesRatherThanAppending()
    {
        var dir = NewTempDir();
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        store.Save("ver-0.53.0.2-dev");
        store.Save("ver-0.54.0.0");

        Assert.Equal("ver-0.54.0.0", store.Load());
    }

    /// <summary>
    /// A file holding only whitespace is indistinguishable from never having
    /// been written, and must read as "no record" rather than as a version
    /// called " " that every real version would then differ from - which
    /// would pop the update message on every launch forever.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void Load_BlankFile_IsNull(string contents)
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, FileName), contents);
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        Assert.Null(store.Load());
    }

    /// <summary>
    /// Trailing newlines are what a text editor leaves behind; a version read
    /// back with one would never equal the running version and would nag on
    /// every launch.
    /// </summary>
    [Fact]
    public void Load_TrimsSurroundingWhitespace()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, FileName), "  ver-0.53.0.2-dev\r\n");
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        Assert.Equal("ver-0.53.0.2-dev", store.Load());
    }

    [Fact]
    public void RecordLaunch_NoFileYet_SaysNothing_ButWritesTheCurrentVersion()
    {
        var dir = NewTempDir();
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        var updated = store.RecordLaunch("ver-0.53.0.2-dev");

        Assert.False(updated);
        Assert.Equal("ver-0.53.0.2-dev", store.Load());
    }

    [Fact]
    public void RecordLaunch_DifferentVersionOnFile_ReportsAnUpdate_ThenWritesTheCurrentVersion()
    {
        var dir = NewTempDir();
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());
        store.Save("ver-0.53.0.2-dev");

        var updated = store.RecordLaunch("ver-0.54.0.0");

        Assert.True(updated);
        Assert.Equal("ver-0.54.0.0", store.Load());
    }

    [Fact]
    public void RecordLaunch_SameVersionOnFile_SaysNothing()
    {
        var dir = NewTempDir();
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());
        store.Save("ver-0.53.0.2-dev");

        var updated = store.RecordLaunch("ver-0.53.0.2-dev");

        Assert.False(updated);
        Assert.Equal("ver-0.53.0.2-dev", store.Load());
    }

    /// <summary>
    /// The one that decides whether this nags: a second launch on the same
    /// build after an update must be silent, because the first one already
    /// wrote the new version.
    /// </summary>
    [Fact]
    public void RecordLaunch_Twice_SpeaksOnceOnly()
    {
        var dir = NewTempDir();
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());
        store.Save("ver-0.53.0.2-dev");

        Assert.True(store.RecordLaunch("ver-0.54.0.0"));
        Assert.False(store.RecordLaunch("ver-0.54.0.0"));
    }

    /// <summary>
    /// A version that cannot be read is treated as missing - which means a
    /// first-install-style silent launch, never a popup on the strength of a
    /// file nobody could read.
    /// </summary>
    [Fact]
    public void RecordLaunch_CorruptFile_TreatedAsMissing_SaysNothing_AndRepairsTheFile()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, FileName), "\0\0\0   \0");
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        var updated = store.RecordLaunch("ver-0.54.0.0");

        Assert.False(updated);
        Assert.Equal("ver-0.54.0.0", store.Load());
    }

    /// <summary>
    /// An unreadable directory (the file's own path taken by a directory, so
    /// every read and write against it fails) must degrade to silence rather
    /// than taking the tray down on startup - this store runs before any UI
    /// exists to show an error against.
    /// </summary>
    [Fact]
    public void RecordLaunch_WhenTheFileCannotBeReadOrWritten_NeverThrows()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, FileName));
        var log = new CapturingDiagnosticLog();
        var store = new LastLaunchedVersionStore(dir, log);

        var updated = store.RecordLaunch("ver-0.54.0.0");

        Assert.False(updated);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Save_WhenTheFileCannotBeWritten_NeverThrows()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(Path.Combine(dir, FileName));
        var store = new LastLaunchedVersionStore(dir, new CapturingDiagnosticLog());

        store.Save("ver-0.54.0.0");
    }
}
