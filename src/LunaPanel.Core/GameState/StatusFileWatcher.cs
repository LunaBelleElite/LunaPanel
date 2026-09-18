using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.GameState;

/// <summary>
/// Watches Status.json for changes and keeps a <see cref="GameStateStore"/>
/// fresh. Never discovers the directory it watches - it is handed one, like
/// every other file-backed type under <c>LunaPanel.Core</c>.
///
/// Elite rewrites the whole file on every update rather than appending, so a
/// read can land mid-rewrite and see truncated or missing content. That is
/// routine, not exceptional: a torn read is retried a few times a short
/// delay apart, and if it never clears up, the store's last-good snapshot is
/// left exactly as it was rather than being replaced with nothing. The file
/// is opened with <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>
/// throughout, since the game holds it open and can delete/replace it out
/// from under a reader at any moment.
///
/// A <see cref="FileSystemWatcher"/> is the primary signal, debounced
/// because one write can raise several events. Watchers are known to miss
/// events occasionally, so a slow safety poll re-reads the file
/// independently and logs whenever it catches a change the watcher never
/// reported - that tells us in the field whether the watcher is actually
/// reliable, rather than us guessing.
///
/// Every delay in this type (debounce, retry, safety poll) is scheduled
/// through the injected <see cref="TimeProvider"/>, exactly like
/// <see cref="Diagnostics.DiagnosticLogWriter"/> uses one for its own
/// clock reads - here it also drives <see cref="TimeProvider.CreateTimer"/>,
/// so a test can advance a fake clock instead of waiting on the wall clock.
/// </summary>
public sealed class StatusFileWatcher : IDisposable
{
    private const string LogCategory = "Status";

    /// <summary>How long to wait after the last file-system event before actually reading the file.</summary>
    internal static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(30);

    /// <summary>Delay between successive attempts to read a torn file.</summary>
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>How many attempts a single read cycle makes before giving up and keeping the last-good snapshot.</summary>
    internal const int MaxReadAttempts = 5;

    /// <summary>How often the safety poll re-reads the file independently of the watcher.</summary>
    internal static readonly TimeSpan SafetyPollInterval = TimeSpan.FromSeconds(1);

    private readonly string _fullPath;
    private readonly GameStateStore _store;
    private readonly TimeProvider _clock;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    private readonly FileSystemWatcher _watcher;
    private readonly ITimer _pollTimer;
    private ITimer? _debounceTimer;
    private ITimer? _retryTimer;
    private bool _disposed;
    private bool _readInProgress;

    public StatusFileWatcher(string directory, string fileName, GameStateStore store, TimeProvider clock, IDiagnosticLog log)
        : this(directory, fileName, store, clock, log, enableRealFileSystemWatcher: true)
    {
    }

    /// <summary>
    /// The real constructor. <paramref name="enableRealFileSystemWatcher"/>
    /// exists only for <c>StatusFileWatcherTests</c>: a real
    /// <see cref="FileSystemWatcher"/> delivers events on its own background
    /// thread at real, unpredictable OS timing, and a test driving the
    /// debounce/retry/poll logic deterministically through the
    /// <c>SimulateXForTesting</c> hooks and a fake clock cannot tolerate a
    /// second, real, concurrent trigger racing against it - that combination
    /// was tried first and was genuinely flaky (a real event would
    /// occasionally land mid-retry-chain and desynchronize it). Production
    /// always goes through the public constructor above, which leaves this
    /// <see langword="true"/>.
    /// </summary>
    internal StatusFileWatcher(string directory, string fileName, GameStateStore store, TimeProvider clock, IDiagnosticLog log, bool enableRealFileSystemWatcher)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fileName);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _fullPath = Path.Combine(directory, fileName);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };
        _watcher.Changed += OnWatcherEvent;
        _watcher.Created += OnWatcherEvent;
        _watcher.Renamed += OnWatcherEvent;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = enableRealFileSystemWatcher;

        _pollTimer = _clock.CreateTimer(_ => OnSafetyPollTick(), null, SafetyPollInterval, SafetyPollInterval);

        _log.Info(LogCategory, "Status watcher started");

        // An absent file here just means the game isn't running yet - not an error.
        BeginReadCycle(fromPoll: false);
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => ScheduleDebouncedRead();

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        _log.Warn(LogCategory, "Status.json file watcher reported an error", e.GetException()?.Message);

    private void ScheduleDebouncedRead()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = _clock.CreateTimer(_ => BeginReadCycle(fromPoll: false), null, DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSafetyPollTick()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        BeginReadCycle(fromPoll: true);
    }

    private void BeginReadCycle(bool fromPoll)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // A cycle is already in flight (most likely retrying a torn
            // read) - let it finish rather than starting a second one over
            // the same file. If content keeps changing, the safety poll
            // will pick up anything this skip missed.
            if (_readInProgress)
            {
                return;
            }

            _readInProgress = true;
        }

        AttemptRead(0, fromPoll);
    }

    private void AttemptRead(int attemptNumber, bool fromPoll)
    {
        string text;
        try
        {
            using var stream = new FileStream(_fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            RetryOrGiveUp(attemptNumber, fromPoll, "Could not open Status.json", ex.Message);
            return;
        }

        var result = StatusJsonParser.Parse(text);
        if (!result.Success)
        {
            RetryOrGiveUp(attemptNumber, fromPoll, "Failed to parse Status.json", result.Error);
            return;
        }

        // A concurrent Dispose could have landed while the file I/O above
        // was in flight (e.g. a real FileSystemWatcher event handled on a
        // background thread) - re-check before committing, so a disposed
        // watcher can never push one more update after Dispose returns.
        lock (_gate)
        {
            if (_disposed)
            {
                FinishReadCycle();
                return;
            }
        }

        var previous = _store.Current;
        _store.UpdateSnapshot(result.Snapshot);

        if (fromPoll && !Equals(previous, result.Snapshot))
        {
            _log.Warn(LogCategory, "Safety poll caught a change the file watcher did not report");
        }

        if (attemptNumber > 0)
        {
            _log.Info(LogCategory, $"Recovered a torn read after {attemptNumber + 1} attempt(s)");
        }

        FinishReadCycle();
    }

    private void RetryOrGiveUp(int attemptNumber, bool fromPoll, string message, string? detail)
    {
        if (attemptNumber + 1 >= MaxReadAttempts)
        {
            _log.Warn(LogCategory, $"{message}; giving up after {MaxReadAttempts} attempts, keeping last-good snapshot", detail);
            FinishReadCycle();
            return;
        }

        _log.Debug(LogCategory, $"{message}; retrying ({attemptNumber + 2}/{MaxReadAttempts})", detail);

        lock (_gate)
        {
            if (_disposed)
            {
                FinishReadCycle();
                return;
            }

            _retryTimer?.Dispose();
            _retryTimer = _clock.CreateTimer(_ => AttemptRead(attemptNumber + 1, fromPoll), null, RetryDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void FinishReadCycle()
    {
        lock (_gate) { _readInProgress = false; }
    }

    /// <summary>
    /// Simulates a raw file-system notification exactly as
    /// <see cref="FileSystemWatcher"/> would deliver one, without depending
    /// on real OS event-delivery timing. Real <see cref="FileSystemWatcher"/>
    /// wiring is exercised too (see <c>StatusFileWatcherTests</c>'s one
    /// genuine end-to-end test), but every other test drives the debounce,
    /// retry, and torn-read logic through this hook against real files on
    /// disk, so those pins depend only on the injected <see cref="TimeProvider"/>
    /// and never on how fast the OS happens to notice a write.
    /// </summary>
    internal void SimulateFileSystemEventForTesting() => ScheduleDebouncedRead();

    /// <summary>
    /// Simulates the safety poll's periodic tick firing, without waiting for
    /// <see cref="SafetyPollInterval"/> of real (or advanced) time to pass.
    /// </summary>
    internal void SimulateSafetyPollTickForTesting() => OnSafetyPollTick();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherEvent;
            _watcher.Created -= OnWatcherEvent;
            _watcher.Renamed -= OnWatcherEvent;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _debounceTimer?.Dispose();
            _retryTimer?.Dispose();
            _pollTimer.Dispose();
        }

        _log.Info(LogCategory, "Status watcher stopped");
    }
}
