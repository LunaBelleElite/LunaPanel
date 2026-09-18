using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Drives <see cref="ThemeFileWatcher"/> against real temp files (never the
/// repo tree or the user's real profile) - same shape as
/// <c>BindsFileWatcherTests</c>, adapted for a watched DIRECTORY rather than
/// one fixed file.
///
/// Determinism split exactly like <c>BindsFileWatcherTests</c>: the debounce
/// window is fully virtualized through a hand-written
/// <see cref="FakeTimeProvider"/>, and every test but one drives event
/// delivery through <see cref="ThemeFileWatcher.SimulateFileSystemEventForTesting"/>
/// rather than a real, OS-timed <see cref="FileSystemWatcher"/>. The one
/// exception, <see cref="RealFileSystemWatcher_PicksUpARealWrite_EndToEnd"/>,
/// proves the real wiring for real, with a bounded poll-until-true wait
/// rather than a fixed sleep.
/// </summary>
public class ThemeFileWatcherTests
{
    /// <summary>A <see cref="TimeProvider"/> whose clock only moves when <see cref="Advance"/> is called, and whose <see cref="ITimer"/>s fire synchronously, inline, from that same call.</summary>
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

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "theme-file-watcher", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Builds a <see cref="PathDiscoveryResult"/> whose <c>Edhm</c> selects
    /// exactly one edition, at <paramref name="iniDirectory"/>, with
    /// <c>ActiveInstance</c> naming it directly - this file only needs a
    /// selection result to exist, not to re-derive one (same convention as
    /// <c>LiveThemeResolverTests.DiscoveryWithEdhm</c>).
    /// </summary>
    private static PathDiscoveryResult DiscoveryWithIniDirectory(string? iniDirectory)
    {
        var editions = iniDirectory is not null
            ? new[] { new EdhmEditionData("ODYSS", iniDirectory, null, null) }
            : Array.Empty<EdhmEditionData>();

        return new PathDiscoveryResult(
            EliteInstallations: Array.Empty<EliteInstallation>(),
            Bindings: new BindingsDiscoveryResult(null, null, Array.Empty<string>()),
            BindingsSelection: new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null),
            Edhm: new EdhmDiscoveryResult(iniDirectory is not null, @"C:\fake\EDHM_UI", iniDirectory is not null ? "ODYSS" : null, editions),
            LunaPanelDirectories: new LunaPanelDirectoryLayout(@"C:\fake\Logs", @"C:\fake\Layouts", @"C:\fake\Pairing\device-registry.json"),
            StatusJson: new StatusJsonDiscoveryResult(null));
    }

    [Fact]
    public void Constructor_NoEdhmEditionAtAll_DoesNotThrow_AndIsNotActive()
    {
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        ThemeFileWatcher? watcher = null;
        var exception = Record.Exception(() =>
            watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(null), clock, log, enableRealFileSystemWatcher: false));
        using var disposable = watcher;

        Assert.Null(exception);
        Assert.False(watcher!.IsActive);
    }

    [Fact]
    public void Constructor_SelectedIniDirectoryDoesNotExist_DoesNotThrow_AndIsNotActive_LogsWarning()
    {
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var iniDirectory = Path.Combine(NewTempDir(), "does-not-exist-ini-dir");

        ThemeFileWatcher? watcher = null;
        var exception = Record.Exception(() =>
            watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), clock, log, enableRealFileSystemWatcher: false));
        using var disposable = watcher;

        Assert.Null(exception);
        Assert.False(watcher!.IsActive);
        // The specific message the pre-flight directory check produces, not
        // merely "some Warn happened" - the FileSystemWatcher constructor
        // would ALSO throw (and be caught) for a missing directory with no
        // pre-flight check at all, logging a different message under the
        // same category and level, which would let that check be deleted
        // with this assertion still green.
        Assert.Contains(log.Events, e => e.Category == "Theme" && e.Message.Contains("directory does not exist"));
    }

    [Fact]
    public void Constructor_ValidIniDirectory_IsActive_LogsInfo()
    {
        var iniDirectory = NewTempDir();
        File.WriteAllText(Path.Combine(iniDirectory, "Advanced.ini"), "[Section]\nKey=Value\n");
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), clock, log, enableRealFileSystemWatcher: false);

        Assert.True(watcher.IsActive);
        Assert.Contains(log.Events, e => e.Category == "Theme" && e.Message.Contains("started"));
    }

    [Fact]
    public void SimulatedFileEvent_RaisesChanged_OnlyAfterTheDebounceWindowElapses()
    {
        var iniDirectory = NewTempDir();
        File.WriteAllText(Path.Combine(iniDirectory, "Advanced.ini"), "[Section]\nKey=Value\n");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        var raiseCount = 0;
        watcher.Changed += () => raiseCount++;

        watcher.SimulateFileSystemEventForTesting();
        Assert.Equal(0, raiseCount); // not yet - the debounce window has not elapsed

        clock.Advance(ThemeFileWatcher.DebounceWindow - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, raiseCount); // still short of the window

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, raiseCount);
    }

    [Fact]
    public void SimulatedBurstOfEvents_CollapsesIntoExactlyOneChangedRaise()
    {
        var iniDirectory = NewTempDir();
        File.WriteAllText(Path.Combine(iniDirectory, "Advanced.ini"), "[Section]\nKey=Value\n");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        var raiseCount = 0;
        watcher.Changed += () => raiseCount++;

        watcher.SimulateFileSystemEventForTesting();
        watcher.SimulateFileSystemEventForTesting();
        watcher.SimulateFileSystemEventForTesting();

        clock.Advance(ThemeFileWatcher.DebounceWindow);

        Assert.Equal(1, raiseCount);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndDoesNotThrow()
    {
        var iniDirectory = NewTempDir();
        File.WriteAllText(Path.Combine(iniDirectory, "Advanced.ini"), "[Section]\nKey=Value\n");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        var first = Record.Exception(() => watcher.Dispose());
        var second = Record.Exception(() => watcher.Dispose());

        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public void Dispose_StopsFurtherRaises_EvenWithAPendingDebounceTimer()
    {
        var iniDirectory = NewTempDir();
        File.WriteAllText(Path.Combine(iniDirectory, "Advanced.ini"), "[Section]\nKey=Value\n");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), clock, new CapturingDiagnosticLog(), enableRealFileSystemWatcher: false);

        var raiseCount = 0;
        watcher.Changed += () => raiseCount++;

        watcher.SimulateFileSystemEventForTesting(); // schedules the debounce timer
        watcher.Dispose();

        var advanceException = Record.Exception(() => clock.Advance(ThemeFileWatcher.DebounceWindow * 10));

        Assert.Null(advanceException);
        Assert.Equal(0, raiseCount);
    }

    [Fact]
    public void Dispose_WhenNeverActivated_DoesNotThrow_AndDoesNotLogStopped()
    {
        var log = new CapturingDiagnosticLog();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(null), clock, log, enableRealFileSystemWatcher: false);

        var exception = Record.Exception(() => watcher.Dispose());

        Assert.Null(exception);
        Assert.DoesNotContain(log.Events, e => e.Message.Contains("stopped"));
    }

    /// <summary>
    /// The one test in this file that does not simulate anything: a real
    /// <see cref="FileSystemWatcher"/>, watching a real write, on the real
    /// system clock - same shape as
    /// <c>BindsFileWatcherTests.RealFileSystemWatcher_PicksUpARealWrite_EndToEnd</c>.
    /// Bounded (5s) and poll-until-true, not a fixed sleep, so it fails fast
    /// and honestly if the wiring breaks instead of racing a guessed
    /// duration.
    /// </summary>
    [Fact]
    public async Task RealFileSystemWatcher_PicksUpARealWrite_EndToEnd()
    {
        var iniDirectory = NewTempDir();
        var path = Path.Combine(iniDirectory, "Advanced.ini");
        File.WriteAllText(path, "[Section]\nKey=Value\n");
        using var watcher = new ThemeFileWatcher(DiscoveryWithIniDirectory(iniDirectory), TimeProvider.System, new CapturingDiagnosticLog());
        Assert.True(watcher.IsActive);

        var raised = false;
        watcher.Changed += () => raised = true;

        File.WriteAllText(path, "[Section]\nKey=ChangedValue\n");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!raised && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(raised);
    }
}
