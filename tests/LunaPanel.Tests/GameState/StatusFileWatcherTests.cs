using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Drives <see cref="StatusFileWatcher"/> against real temp directories and
/// real files under the test output (never the repo tree or the user's real
/// profile) - this is I/O behaviour, and the interesting failures are real
/// ones, so mocking the filesystem would prove nothing.
///
/// Determinism split in two:
/// - Debounce, retry, and safety-poll <em>timing</em> is fully virtualized
///   through a hand-written <see cref="FakeTimeProvider"/> (like
///   <c>GameStateStoreTests</c>' own copy) that also virtualizes
///   <see cref="TimeProvider.CreateTimer"/>, so no test needs to sleep for
///   real to observe a debounce window or a retry delay elapse.
/// - <em>Event delivery</em> from a real OS <see cref="FileSystemWatcher"/>
///   is not something any fake clock can control - the OS decides when to
///   notify. Every test except one drives that part through
///   <see cref="StatusFileWatcher.SimulateFileSystemEventForTesting"/> /
///   <see cref="StatusFileWatcher.SimulateSafetyPollTickForTesting"/>
///   instead, which exercise the exact same read/parse/retry logic against
///   real file content without waiting on the OS. The one exception,
///   <see cref="RealFileSystemWatcher_PicksUpARealWrite_EndToEnd"/>, uses a
///   real <see cref="FileSystemWatcher"/> end to end and a real (but
///   bounded, poll-until-true, not fixed-sleep) wait, because that wiring
///   itself has to be proven for real at least once.
/// </summary>
public class StatusFileWatcherTests
{
    private const string FileName = "Status.json";

    /// <summary>
    /// A <see cref="TimeProvider"/> whose clock only moves when
    /// <see cref="Advance"/> is called, and whose <see cref="ITimer"/>s fire
    /// synchronously, inline, from that same call.
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
            var timer = new PendingTimer(this, callback, state, period);
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

        private void Remove(PendingTimer timer)
        {
            lock (_lock) { _pending.Remove(timer); }
        }

        private sealed class PendingTimer : ITimer
        {
            private readonly FakeTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public PendingTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan period)
            {
                _owner = owner;
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

            public void Dispose()
            {
                Disposed = true;
                _owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>Captures every <see cref="DiagnosticEvent"/> written to it, for assertions.</summary>
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "gamestate", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ValidJson(uint flags) =>
        $"{{ \"timestamp\":\"2026-09-05T00:00:00Z\", \"event\":\"Status\", \"Flags\":{flags}, \"Odyssey\":true }}";

    private const string TornJson = "{ \"timestamp\":\"2026-09-05T00:00:00Z\", \"event\":\"Stat";

    [Fact]
    public void Constructor_ValidFileAlreadyPresent_MakesSnapshotAvailableImmediately()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, FileName), ValidJson(5));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);

        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        Assert.NotNull(store.Current);
        Assert.Equal(5u, store.Current!.Flags);
    }

    [Fact]
    public void Constructor_AbsentFile_DoesNotThrow_AndCurrentStaysNull()
    {
        var dir = NewTempDir(); // no file written
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);

        StatusFileWatcher? watcher = null;
        var exception = Record.Exception(() => watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false));
        using var disposable = watcher;

        Assert.Null(exception);
        Assert.Null(store.Current);
    }

    [Fact]
    public void SimulatedFileEvent_ValidFile_UpdatesTheSnapshot()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);
        Assert.Null(store.Current); // absent at construction

        File.WriteAllText(path, ValidJson(9));
        watcher.SimulateFileSystemEventForTesting();
        clock.Advance(StatusFileWatcher.DebounceWindow);

        Assert.Equal(9u, store.Current!.Flags);
    }

    [Fact]
    public void HalfWrittenFile_ThenCompletedBeforeRetriesExhaust_LeavesLastGoodIntact_ThenRecovers()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var log = new CapturingDiagnosticLog();
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, log, enableRealFileSystemWatcher: false);
        Assert.Equal(1u, store.Current!.Flags);

        var changedValues = new List<StatusSnapshot?>();
        store.Changed += s => changedValues.Add(s);

        // The game is mid-rewrite: the file is truncated.
        File.WriteAllText(path, TornJson);
        watcher.SimulateFileSystemEventForTesting();
        clock.Advance(StatusFileWatcher.DebounceWindow); // attempt 1 of 5: fails, retry scheduled

        Assert.Equal(1u, store.Current!.Flags); // last-good intact, never null or empty
        Assert.Empty(changedValues);

        // The rewrite completes before the retry window runs out.
        File.WriteAllText(path, ValidJson(2));
        clock.Advance(StatusFileWatcher.RetryDelay); // attempt 2 of 5: succeeds

        Assert.Equal(2u, store.Current!.Flags);
        Assert.Single(changedValues);
        Assert.All(changedValues, Assert.NotNull);
        Assert.Contains(log.Events, e => e.Category == "Status" && e.Message.Contains("Recovered a torn read"));
    }

    [Fact]
    public void TornRead_ExhaustsAllRetries_KeepsLastGoodSnapshot_AndLogsGivingUp()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var log = new CapturingDiagnosticLog();
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, log, enableRealFileSystemWatcher: false);
        Assert.Equal(1u, store.Current!.Flags);

        File.WriteAllText(path, TornJson);
        watcher.SimulateFileSystemEventForTesting();
        clock.Advance(StatusFileWatcher.DebounceWindow); // attempt 1 of 5

        for (var attempt = 2; attempt <= StatusFileWatcher.MaxReadAttempts; attempt++)
        {
            Assert.Equal(1u, store.Current!.Flags); // last-good intact through every retry
            clock.Advance(StatusFileWatcher.RetryDelay);
        }

        Assert.Equal(1u, store.Current!.Flags);
        Assert.Contains(log.Events, e =>
            e.Category == "Status" && e.Level == DiagnosticLevel.Warn && e.Message.Contains("giving up"));
    }

    [Fact]
    public void SafetyPoll_PicksUpAChange_TheWatcherWasNeverToldAbout()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var log = new CapturingDiagnosticLog();
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, log, enableRealFileSystemWatcher: false);
        Assert.Equal(1u, store.Current!.Flags);

        // The file changes, but no event is ever simulated for it - the
        // watcher is treated as having missed it entirely.
        File.WriteAllText(path, ValidJson(2));

        watcher.SimulateSafetyPollTickForTesting();

        Assert.Equal(2u, store.Current!.Flags);
        Assert.Contains(log.Events, e =>
            e.Category == "Status" && e.Message.Contains("Safety poll caught a change"));
    }

    [Fact]
    public void SafetyPoll_NoChange_LogsNothingAboutACaughtChange()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var log = new CapturingDiagnosticLog();
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, log, enableRealFileSystemWatcher: false);

        watcher.SimulateSafetyPollTickForTesting();

        Assert.DoesNotContain(log.Events, e => e.Message.Contains("Safety poll caught a change"));
    }

    [Fact]
    public void WatcherTriggeredChange_DoesNotLogAsASafetyPollCatch()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var log = new CapturingDiagnosticLog();
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, log, enableRealFileSystemWatcher: false);

        File.WriteAllText(path, ValidJson(2));
        watcher.SimulateFileSystemEventForTesting();
        clock.Advance(StatusFileWatcher.DebounceWindow);

        Assert.Equal(2u, store.Current!.Flags);
        Assert.DoesNotContain(log.Events, e => e.Message.Contains("Safety poll caught a change"));
    }

    [Fact]
    public void SimulatedBurstOfEvents_CollapsesIntoExactlyOneSnapshotUpdate()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        using var watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);
        Assert.Equal(1u, store.Current!.Flags);

        var changeCount = 0;
        store.Changed += _ => changeCount++;

        File.WriteAllText(path, ValidJson(2));
        watcher.SimulateFileSystemEventForTesting();
        File.WriteAllText(path, ValidJson(3));
        watcher.SimulateFileSystemEventForTesting();
        File.WriteAllText(path, ValidJson(4));
        watcher.SimulateFileSystemEventForTesting();

        clock.Advance(StatusFileWatcher.DebounceWindow);

        Assert.Equal(1, changeCount);
        Assert.Equal(4u, store.Current!.Flags);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndDoesNotThrow()
    {
        var dir = NewTempDir();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        var first = Record.Exception(() => watcher.Dispose());
        var second = Record.Exception(() => watcher.Dispose());

        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public void Dispose_DuringAnInFlightRetryCycle_DoesNotThrow_AndStopsFurtherActivity()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        File.WriteAllText(path, TornJson);
        watcher.SimulateFileSystemEventForTesting();
        clock.Advance(StatusFileWatcher.DebounceWindow); // fails, a retry is now pending

        var disposeException = Record.Exception(() => watcher.Dispose());
        Assert.Null(disposeException);

        // The pending retry (and anything after it) must never fire post-Dispose.
        var advanceException = Record.Exception(() => clock.Advance(StatusFileWatcher.RetryDelay * 10));
        Assert.Null(advanceException);
        Assert.Equal(1u, store.Current!.Flags);
    }

    [Fact]
    public void Dispose_StopsTheSafetyPoll()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var watcher = new StatusFileWatcher(dir, FileName, store, clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        watcher.Dispose();

        File.WriteAllText(path, ValidJson(2));
        clock.Advance(TimeSpan.FromTicks(StatusFileWatcher.SafetyPollInterval.Ticks * 3));

        Assert.Equal(1u, store.Current!.Flags); // the real periodic poll timer never fired again
    }

    /// <summary>
    /// The one test in this file that does not simulate anything: a real
    /// <see cref="FileSystemWatcher"/>, watching a real write, on the real
    /// system clock. OS event-delivery latency cannot be virtualized, so
    /// this waits for real - bounded (5s) and poll-until-true rather than a
    /// fixed sleep, so it fails fast and honestly if the wiring breaks
    /// instead of racing a guessed duration.
    /// </summary>
    [Fact]
    public async Task RealFileSystemWatcher_PicksUpARealWrite_EndToEnd()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, FileName);
        File.WriteAllText(path, ValidJson(1));
        var store = new GameStateStore(TimeProvider.System);
        using var watcher = new StatusFileWatcher(dir, FileName, store, TimeProvider.System, new CapturingDiagnosticLog());
        Assert.Equal(1u, store.Current!.Flags);

        File.WriteAllText(path, ValidJson(2));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (store.Current?.Flags != 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2u, store.Current!.Flags);
    }
}
