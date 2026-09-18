using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Drives <see cref="JournalTailer"/> against real temp directories and real
/// files under the test output - never the repo tree, and never the
/// commander's own profile. Same determinism split as
/// <c>StatusFileWatcherTests</c>, and for the same reason it was forced
/// there: every test but one disables the real
/// <see cref="FileSystemWatcher"/> and drives the debounce/poll logic through
/// the <c>SimulateXForTesting</c> hooks against a hand-written
/// <c>FakeTimeProvider</c>, because a real watcher firing on its own
/// background thread mid-read desynchronises a fake clock. The one exception
/// proves the real wiring end to end.
///
/// What this tailer must survive, all of it real: the filename changes when
/// the game restarts, the game holds the file open while writing it, a read
/// can catch a half-written final line, and a consumer sync client leaves
/// <c>*-CONFLICT-*</c> copies in the very directory being globbed.
/// </summary>
public class JournalTailerTests
{
    /// <summary>
    /// A <see cref="TimeProvider"/> whose clock only moves when
    /// <see cref="Advance"/> is called and whose <see cref="ITimer"/>s fire
    /// synchronously, inline, from that same call. A copy of
    /// <c>StatusFileWatcherTests</c>' own, deliberately - see that file's
    /// remarks for why these tests each keep their own rather than sharing
    /// one mutable helper.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _utcNow;
        private readonly List<PendingTimer> _pending = new();

        public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock) { return _utcNow; }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PendingTimer(callback, state, period);
            lock (_lock)
            {
                timer.DueAt = dueTime <= TimeSpan.Zero ? _utcNow : _utcNow + dueTime;
                _pending.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<PendingTimer> due;
            lock (_lock)
            {
                _utcNow += delta;
                due = _pending.Where(t => !t.Disposed && t.DueAt <= _utcNow).ToList();
                foreach (var timer in due)
                {
                    if (timer.Period == Timeout.InfiniteTimeSpan || timer.Period == TimeSpan.Zero)
                    {
                        _pending.Remove(timer);
                    }
                    else
                    {
                        timer.DueAt += timer.Period;
                    }
                }
            }

            foreach (var timer in due)
            {
                timer.InvokeCallback();
            }
        }

        private sealed class PendingTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public PendingTimer(TimerCallback callback, object? state, TimeSpan period)
            {
                _callback = callback;
                _state = state;
                Period = period;
            }

            public DateTimeOffset DueAt { get; set; }
            public TimeSpan Period { get; }
            public bool Disposed { get; private set; }

            public void InvokeCallback()
            {
                if (!Disposed)
                {
                    _callback(_state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "journal", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private const string FixtureJournalName = "Journal.2026-01-01T120000.01.log";

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "journal", FixtureJournalName);

    private static string Line(string eventName, params (string Key, string Value)[] strings)
    {
        var parts = strings.Select(p => $"\"{p.Key}\":\"{p.Value}\"");
        return "{ \"timestamp\":\"2026-01-01T12:00:00Z\", \"event\":\"" + eventName + "\"" +
               (strings.Length == 0 ? string.Empty : ", " + string.Join(", ", parts)) + " }";
    }

    private static void WriteLines(string path, params string[] lines) =>
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));

    private static void Append(string path, string text) =>
        File.AppendAllText(path, text);

    private static void SetAge(string path, int minutesAgo) =>
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));

    private static JournalTailer Tail(string dir, JournalStateStore store, FakeTimeProvider clock, IDiagnosticLog log) =>
        new(dir, store, clock, log, enableRealFileSystemWatcher: false);

    private static void Poke(JournalTailer tailer, FakeTimeProvider clock)
    {
        tailer.SimulateFileSystemEventForTesting();
        clock.Advance(JournalTailer.DebounceWindow);
    }

    // -----------------------------------------------------------------
    // The startup back-scan
    // -----------------------------------------------------------------

    [Fact]
    public void Constructor_BackScansTheWholeFile_SoLoadGameAnchorsTheVesselType()
    {
        // The real case, not a hypothetical: one of the commander's last 40
        // journals opens with a DockSRV and no preceding LaunchVessel,
        // because they logged in already inside the Nomad. Without reading
        // back to LoadGame at the top of the file, the vessel type is
        // unknown until they next launch.
        //
        // [2026-09-10: more so than when this was written. DockSRV now
        // CLEARS the current vessel rather than naming one - it says which
        // vessel they got out of - so LoadGame is the only thing that can
        // name a vessel a commander is already sitting in at session start.]
        //
        // The fixture also contains a Loadout line naming "krait_mkii", so a
        // tailer that read the vessel type off any "Ship" property rather
        // than off LoadGame specifically would land on the wrong answer here
        // instead of on no answer.
        var dir = NewTempDir();
        File.Copy(FixturePath, Path.Combine(dir, FixtureJournalName));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());

        Assert.Equal("lander01", store.CurrentVesselType);
        Assert.Equal("Nomad", store.CurrentVesselTypeLocalised);
    }

    [Fact]
    public void Constructor_FollowsTheFileItBackScanned()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        File.Copy(FixturePath, path);
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());

        Assert.Equal(path, tailer.CurrentJournalPath);
    }

    [Fact]
    public void Constructor_NoJournalFilesAtAll_DoesNotThrow()
    {
        var dir = NewTempDir();
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        JournalTailer? tailer = null;
        var exception = Record.Exception(() => tailer = Tail(dir, store, clock, new CapturingDiagnosticLog()));
        using var disposable = tailer;

        Assert.Null(exception);
        Assert.Null(store.CurrentVesselType);
    }

    [Fact]
    public void BackScanFindingNoLoadGame_Warns_SoAnUnknownVesselTypeIsVisibleInTheField()
    {
        var dir = NewTempDir();
        WriteLines(Path.Combine(dir, FixtureJournalName), Line("FSDJump"));
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var tailer = Tail(dir, new JournalStateStore(), clock, log);

        Assert.Contains(log.Events, e =>
            e.Level == DiagnosticLevel.Warn && e.Message.Contains("LoadGame", StringComparison.Ordinal));
    }

    [Fact]
    public void BackScanFindingAContinued_LogsTheSeamExplicitly_InsteadOfTheGenericNoLoadGameWarning()
    {
        // O24: a journal that continues an earlier part has no LoadGame at
        // its top, same shape as BackScanFindingNoLoadGame_Warns above - but
        // here the cause is known (Continued was seen), so the seam should
        // be named explicitly rather than presenting as the same opaque
        // "found no LoadGame" warning that fires when the cause is unknown.
        var dir = NewTempDir();
        WriteLines(Path.Combine(dir, FixtureJournalName), Line("Continued"), Line("FSDJump"));
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var tailer = Tail(dir, new JournalStateStore(), clock, log);

        Assert.Contains(log.Events, e =>
            e.Message.Contains("continues a multi-part journal", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Events, e =>
            e.Level == DiagnosticLevel.Warn && e.Message.Contains("Journal back-scan found no LoadGame", StringComparison.Ordinal));
    }

    [Fact]
    public void BackScanFindingALoadGame_DoesNotWarn()
    {
        var dir = NewTempDir();
        File.Copy(FixturePath, Path.Combine(dir, FixtureJournalName));
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var tailer = Tail(dir, new JournalStateStore(), clock, log);

        Assert.DoesNotContain(log.Events, e =>
            e.Level == DiagnosticLevel.Warn && e.Message.Contains("LoadGame", StringComparison.Ordinal));
    }

    [Fact]
    public void Changed_DoesNotFireForBackScannedEvents_ButDoesForALiveOne()
    {
        // Fix 4's whole hazard, pinned directly against the mechanism rather
        // than against ServerHostBuilder's incidental subscription order: a
        // subscriber attached BEFORE the tailer is even constructed must
        // still never see a back-scanned event. If it could, a future
        // subscriber wired up earlier than PanelTabTracker's own (which today
        // only happens to be safe because it subscribes after JournalTailer's
        // constructor returns) would silently treat hours-old history as
        // something that just happened.
        var dir = NewTempDir();
        // The fixture's own LoadGame (see JournalTailerTests' other back-scan
        // tests) is exactly the "hours old at LunaPanel start" case this
        // guards against.
        File.Copy(FixturePath, Path.Combine(dir, FixtureJournalName));
        var path = Path.Combine(dir, FixtureJournalName);
        var store = new JournalStateStore();
        var seen = new List<string>();
        store.Changed += e => seen.Add(e.EventName);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        Assert.Empty(seen); // the back-scan alone must not have fired Changed

        Append(path, Line("FSDJump") + "\n");
        Poke(tailer, clock);

        Assert.Equal(new[] { "FSDJump" }, seen);
    }

    // -----------------------------------------------------------------
    // Following an append
    // -----------------------------------------------------------------

    [Fact]
    public void AnAppendedLine_IsRecorded_AfterTheDebounceWindow()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        Append(path, Line("LaunchSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad")) + "\n");
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("LaunchSRV", mark));
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void ALineIsRecordedOnlyOnce_NoMatterHowOftenTheTailerIsPoked()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());

        // Everything in the file was consumed by the back-scan. A mark taken
        // now must not see any of it again, however many times we re-read.
        var mark = store.Mark();
        Poke(tailer, clock);
        Poke(tailer, clock);
        tailer.SimulateSafetyPollTickForTesting();

        Assert.False(store.HasSeenSince("FSDJump", mark));
    }

    [Fact]
    public void TheSafetyPoll_PicksUpAnAppendWithNoFileSystemEventAtAll()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        Append(path, Line("DockingRequested") + "\n");
        tailer.SimulateSafetyPollTickForTesting();

        Assert.True(store.HasSeenSince("DockingRequested", mark));
    }

    // -----------------------------------------------------------------
    // The half-written final line
    // -----------------------------------------------------------------

    [Fact]
    public void APartiallyWrittenFinalLine_IsNeitherParsedNorSkipped()
    {
        // Both halves matter. Parsing it would feed a truncated event to the
        // store; skipping past it would lose the line forever when it
        // completes a moment later.
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        var complete = Line("LaunchSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad"));
        Append(path, complete[..25]);
        Poke(tailer, clock);

        Assert.False(store.HasSeenSince("LaunchSRV", mark));
        Assert.Null(store.CurrentVesselType);

        // ... and the rest of the line arrives.
        Append(path, complete[25..] + "\n");
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("LaunchSRV", mark));
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void ACompleteLineBeforeAPartialOne_IsRecordedImmediately_AndThePartialTailStillSurvives()
    {
        // This is the shape a live journal is almost always in: several
        // whole lines, then the one the game is part-way through writing.
        // Both halves have to hold, and they are guarded by two DIFFERENT
        // pieces of the read logic - "nothing complete at all" is a separate
        // branch from "complete lines plus a tail", and a mutation of the
        // second is invisible to a test that only exercises the first. The
        // second assertion pair below is what closes that.
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        var trailing = Line("LaunchSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad"));
        Append(path, Line("DockingRequested") + "\n" + trailing[..30]);
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("DockingRequested", mark));
        Assert.False(store.HasSeenSince("LaunchSRV", mark));

        Append(path, trailing[30..] + "\n");
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("LaunchSRV", mark));
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void AGarbageCompleteLine_DoesNotStopTheLinesAroundIt()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        WriteLines(path, Line("FSDJump"), "not json at all", Line("DockingRequested"));
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("DockingRequested", mark));
    }

    // -----------------------------------------------------------------
    // Rollover: the filename changes when the game restarts
    // -----------------------------------------------------------------

    [Fact]
    public void ANewerJournalAppearing_IsFollowed_AndTheOldFilesTailIsNotLost()
    {
        // StatusFileWatcher binds to a fixed filename; this one cannot. A new
        // journal appears whenever the game restarts, and the last lines
        // written to the outgoing file still matter.
        var dir = NewTempDir();
        var older = Path.Combine(dir, "Journal.2026-01-01T120000.01.log");
        var newer = Path.Combine(dir, "Journal.2026-01-02T120000.01.log");
        WriteLines(older, Line("FSDJump"));
        SetAge(older, minutesAgo: 10);
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        Append(older, Line("DockingRequested") + "\n");
        SetAge(older, minutesAgo: 5);
        WriteLines(newer, Line("LoadGame", ("Ship", "Lander01"), ("Ship_Localised", "Nomad")));
        SetAge(newer, minutesAgo: 1);

        Poke(tailer, clock);

        Assert.Equal(newer, tailer.CurrentJournalPath);
        Assert.True(store.HasSeenSince("DockingRequested", mark));
        Assert.True(store.HasSeenSince("LoadGame", mark));
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void TheNewestJournalIsChosenByWriteTime_NotByFilenameOrder()
    {
        // Elite has used two filename formats: Journal.<yyMMddHHmmss>.NN.log
        // and, since 2022, Journal.<yyyy-MM-dd>THHmmss.NN.log. They do not
        // sort against each other: "210801120000" is lexicographically GREATER
        // than "2026-01-01T120000", so a commander with pre-2022 journals in
        // the folder would have the tailer bind to a years-old file if it
        // ordered by name. This is the pin for that.
        var dir = NewTempDir();
        var oldFormat = Path.Combine(dir, "Journal.210801120000.01.log");
        var newFormat = Path.Combine(dir, "Journal.2026-01-01T120000.01.log");
        WriteLines(oldFormat, Line("FSDJump"));
        WriteLines(newFormat, Line("LoadGame", ("Ship", "Lander01"), ("Ship_Localised", "Nomad")));
        SetAge(oldFormat, minutesAgo: 100000);
        SetAge(newFormat, minutesAgo: 1);

        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());

        Assert.Equal(newFormat, tailer.CurrentJournalPath);
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    // -----------------------------------------------------------------
    // What else is in that directory
    // -----------------------------------------------------------------

    [Fact]
    public void AConflictCopy_IsIgnored_EvenWhenItIsTheNewestFileInTheDirectory()
    {
        // Observed on the authoring machine: a consumer sync client left
        // Status-CONFLICT-1.json and friends in this exact directory. A
        // conflict copy of a journal would be read as real history.
        var dir = NewTempDir();
        var real = Path.Combine(dir, "Journal.2026-01-01T120000.01.log");
        var conflict = Path.Combine(dir, "Journal.2026-01-01T120000.01-CONFLICT-1.log");
        WriteLines(real, Line("LoadGame", ("Ship", "Lander01"), ("Ship_Localised", "Nomad")));
        WriteLines(conflict, Line("LoadGame", ("Ship", "TestBuggy"), ("Ship_Localised", "SRV Scarab")));
        SetAge(real, minutesAgo: 5);
        SetAge(conflict, minutesAgo: 1);

        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());

        Assert.Equal(real, tailer.CurrentJournalPath);
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void OtherFilesInTheDirectory_AreNotReadAsJournals()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "Status.json"), "{ \"event\":\"Status\", \"Flags\":0 }");
        File.WriteAllText(Path.Combine(dir, "NetLog.2601011200.01.log"), Line("LoadGame", ("Ship", "Lander01")) + "\n");
        File.WriteAllText(Path.Combine(dir, "Cargo.json"), "{ \"event\":\"Cargo\" }");

        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());

        Assert.Null(tailer.CurrentJournalPath);
        Assert.Null(store.CurrentVesselType);
    }

    // -----------------------------------------------------------------
    // The game holds the file open while writing it
    // -----------------------------------------------------------------

    [Fact]
    public void AJournalHeldOpenForWritingByAnotherProcess_IsStillRead()
    {
        // Elite keeps the journal open for the whole session. A reader that
        // does not share write access gets IOException on every single read -
        // which would look exactly like "the game reported nothing".
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));

        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        var bytes = System.Text.Encoding.UTF8.GetBytes(Line("DockingRequested") + "\n");
        writer.Write(bytes, 0, bytes.Length);
        writer.Flush();
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("DockingRequested", mark));
    }

    // -----------------------------------------------------------------
    // Odd file states
    // -----------------------------------------------------------------

    [Fact]
    public void AFileThatShrinks_IsReReadFromTheStart_RatherThanThrowing()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"), Line("SupercruiseEntry"), Line("SupercruiseExit"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        WriteLines(path, Line("DockSRV", ("SRVType", "lander01")));
        Poke(tailer, clock);

        Assert.True(store.HasSeenSince("DockSRV", mark));
    }

    [Fact]
    public void Dispose_StopsFurtherReads()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tailer = Tail(dir, store, clock, new CapturingDiagnosticLog());
        var mark = store.Mark();

        tailer.Dispose();
        Append(path, Line("DockSRV", ("SRVType", "lander01")) + "\n");
        Poke(tailer, clock);
        tailer.SimulateSafetyPollTickForTesting();

        Assert.False(store.HasSeenSince("DockSRV", mark));
    }

    // -----------------------------------------------------------------
    // The one real end-to-end test
    // -----------------------------------------------------------------

    [Fact]
    public async Task RealFileSystemWatcher_PicksUpARealAppend_EndToEnd()
    {
        // Real watcher, real system clock, real file. Bounded poll-until-true
        // rather than a fixed sleep, so this is as fast as the OS allows and
        // fails rather than hangs if the wiring is broken.
        var dir = NewTempDir();
        var path = Path.Combine(dir, FixtureJournalName);
        WriteLines(path, Line("FSDJump"));
        var store = new JournalStateStore();
        using var tailer = new JournalTailer(dir, store, TimeProvider.System, new CapturingDiagnosticLog());
        var mark = store.Mark();

        Append(path, Line("LaunchSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad")) + "\n");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !store.HasSeenSince("LaunchSRV", mark))
        {
            await Task.Delay(25);
        }

        Assert.True(store.HasSeenSince("LaunchSRV", mark), "The real watcher/poll never delivered the appended line.");
        Assert.Equal("lander01", store.CurrentVesselType);
    }
}
