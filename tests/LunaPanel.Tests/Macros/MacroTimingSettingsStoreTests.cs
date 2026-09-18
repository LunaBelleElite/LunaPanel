using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Drives <see cref="MacroTimingSettingsStore"/> against real temp
/// directories under the test output (never the repo tree or the user's
/// real profile) - same discipline as
/// <c>LunaPanel.Tests.Layouts.PanelSettingsStoreTests</c>. The one thing this
/// store does differently from every sibling store: <see cref="MacroTimingSettingsStore.Load"/>/
/// <see cref="MacroTimingSettingsStore.Save"/> take NO device id at all, so
/// "shared across devices" here just means "the same file, read twice" -
/// there is no second identity to even attempt to keep separate.
/// </summary>
public class MacroTimingSettingsStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "macro-timing-settings-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_NoFileYet_ReturnsShippedDefaults()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, result.HoldDuration);
        Assert.Equal(MacroTimingDefaults.InterPressGap, result.InterPressGap);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_NonDefaultValues()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        var saved = new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180));

        store.Save(saved);
        var result = store.Load();

        Assert.Equal(saved.HoldDuration, result.HoldDuration);
        Assert.Equal(saved.InterPressGap, result.InterPressGap);
    }

    /// <summary>
    /// "Stored values survive a restart" - a fresh store instance pointed at
    /// the same directory sees what an earlier instance saved, exactly the
    /// way a real process restart would.
    /// </summary>
    [Fact]
    public void Save_ThenLoad_FromAFreshStoreInstance_SurvivesARestart()
    {
        var dir = NewTempDir();
        var saved = new MacroTimingSettings(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(220));
        new MacroTimingSettingsStore(dir, new CapturingDiagnosticLog()).Save(saved);

        var reopened = new MacroTimingSettingsStore(dir, new CapturingDiagnosticLog());
        var result = reopened.Load();

        Assert.Equal(saved.HoldDuration, result.HoldDuration);
        Assert.Equal(saved.InterPressGap, result.InterPressGap);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults_RatherThanThrowing()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "macro-timing.json"), "{ not valid json");
        var store = new MacroTimingSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, result.HoldDuration);
        Assert.Equal(MacroTimingDefaults.InterPressGap, result.InterPressGap);
    }

    /// <summary>
    /// Defense in depth (this store's own remarks on <see cref="MacroTimingSettingsStore.Save"/>):
    /// range validation is the endpoint's job, but a hand-edited file with an
    /// out-of-range value must not be handed back as though it were valid.
    /// </summary>
    [Fact]
    public void Load_OutOfRangeValueInFile_FallsBackToDefaults_RatherThanReturningIt()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "macro-timing.json"), "{\"holdMs\": 999999, \"interPressGapMs\": 100}");
        var store = new MacroTimingSettingsStore(dir, new CapturingDiagnosticLog());

        var result = store.Load();

        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, result.HoldDuration);
        Assert.Equal(MacroTimingDefaults.InterPressGap, result.InterPressGap);
    }

    [Fact]
    public void Save_ThenSaveAgain_OverwritesRatherThanAppending()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(150)));
        store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(250)));

        var result = store.Load();
        Assert.Equal(TimeSpan.FromMilliseconds(300), result.HoldDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(250), result.InterPressGap);
    }

    // -----------------------------------------------------------------
    // Saved (2026-09-10) - the event GET /api/panel/live subscribes to so a
    // timing change made on the PC reaches an already-connected tablet
    // without the commander touching it (ref/docs/macro-timing.md's "Getting
    // there from the PC, and getting it to the tablet"). Deliberately the
    // same shape as LayoutStore.Saved, which the live channel already uses.
    // -----------------------------------------------------------------

    /// <summary>
    /// The event carries the settings that were just written, rather than
    /// signalling "something changed" and leaving the subscriber to go and
    /// read - which is what makes a pushed value impossible to serve from a
    /// stale cache.
    /// </summary>
    [Fact]
    public void Save_RaisesSaved_CarryingTheValuesJustSaved()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        var raised = new List<MacroTimingSettings>();
        store.Saved += s => raised.Add(s);

        store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180)));

        var only = Assert.Single(raised);
        Assert.Equal(TimeSpan.FromMilliseconds(275), only.HoldDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(180), only.InterPressGap);
    }

    /// <summary>
    /// Raised after the file is actually on disk, never before: a subscriber
    /// that reads the store back from inside its own handler must not see the
    /// previous value. Asserted from inside the handler rather than after
    /// Save returns, because after Save returns the ordering claim is no
    /// longer observable at all.
    /// </summary>
    [Fact]
    public void Saved_IsRaisedAfterTheFileIsWritten_SoAHandlerReadingBackSeesTheNewValue()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        MacroTimingSettings? readBackInsideHandler = null;
        store.Saved += _ => readBackInsideHandler = store.Load();

        store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180)));

        Assert.NotNull(readBackInsideHandler);
        Assert.Equal(TimeSpan.FromMilliseconds(275), readBackInsideHandler!.HoldDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(180), readBackInsideHandler.InterPressGap);
    }

    /// <summary>
    /// Raised OUTSIDE this store's own lock, the same discipline
    /// <c>LayoutStore.Saved</c> records: a handler must not be able to
    /// deadlock a save happening on another thread. Deterministic in the
    /// failing direction - a raise made while the lock is held can never let
    /// the second thread's Save complete before the handler returns, so the
    /// wait below cannot pass by luck.
    /// </summary>
    [Fact]
    public void Saved_IsRaisedOutsideTheLock_SoAnotherThreadCanSaveWhileAHandlerRuns()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        var reentered = false;
        var otherThreadFinished = false;
        store.Saved += _ =>
        {
            // Only the first raise re-enters - the nested Save below raises
            // Saved too, and without this the handler would recurse forever.
            if (reentered)
            {
                return;
            }

            reentered = true;
            otherThreadFinished = Task.Run(() =>
                store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(300))))
                .Wait(TimeSpan.FromSeconds(5));
        };

        store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180)));

        Assert.True(reentered);
        Assert.True(otherThreadFinished);
    }

    /// <summary>
    /// Reading is not a change. A live channel that pushed on every Load
    /// would push on every macro run (MacroRunner reads this store per run),
    /// which is a flood rather than a feature.
    /// </summary>
    [Fact]
    public void Load_RaisesNothing()
    {
        var store = new MacroTimingSettingsStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180)));
        var raised = 0;
        store.Saved += _ => raised++;

        store.Load();
        store.Load();

        Assert.Equal(0, raised);
    }
}
