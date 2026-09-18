using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Bindings;

/// <summary>
/// Watches the one bindings file <see cref="LiveBindingsReader"/> currently
/// reads through - <see cref="DiscoveryResultHolder.Current"/>'s
/// <c>BindingsSelection.SelectedFilePath</c>, read once at construction, the
/// same way <see cref="Core.GameState.StatusFileWatcher"/> is handed a fixed
/// directory/filename rather than re-discovering one on every tick - and
/// raises <see cref="Changed"/> whenever Elite rewrites it, so an
/// already-open live channel (<c>GET /api/panel/live</c>) can tell its
/// device "your bindings changed, re-fetch" instead of a rebind staying
/// invisible until the panel reloads.
///
/// <b>What this fixes, and what it deliberately does not.</b> A rebind made
/// in Elite to the SAME file this server already selected now reaches an
/// open device live. Switching PRESET in Elite, or creating a preset that
/// did not exist when the server (and this watcher) started, is UNCHANGED by
/// this type and stays exactly as invisible as <see cref="LiveBindingsReader"/>'s
/// own remarks already document - watching one selected path cannot detect a
/// DIFFERENT path becoming the one that should be watched; only a restart or
/// an explicit "Refresh bindings" (which re-runs the whole discovery sweep
/// and would need a fresh watcher pointed at whatever it finds) closes that
/// gap, and this task deliberately does not attempt to.
///
/// Raises a bare signal, never the bindings content itself: the live channel
/// already has a correct, fully-annotated re-fetch path (<c>GET /api/panel</c>,
/// which the client's <c>loadPanel()</c> already calls) that threading
/// bindings into the live payload would only duplicate for no gain.
///
/// Debounced the same way <see cref="Core.GameState.StatusFileWatcher"/> is -
/// a single logical rewrite can raise more than one raw
/// <see cref="FileSystemWatcher"/> event - but with a longer window (see
/// <see cref="DebounceWindow"/>): unlike Status.json, which a HUD element
/// depends on many times a second, a bindings rewrite is a rare, discrete,
/// commander-initiated action with nothing time-critical riding on the
/// signal arriving within tens of milliseconds, so trading a little latency
/// for more comfortably collapsing a burst of writes into one push costs
/// nothing perceptible.
///
/// Never throws: an absent/undiscovered path, a watcher that fails to start,
/// or the file disappearing mid-run all degrade to "no live rebind
/// notifications" rather than a crash - the same "a layout can't break, only
/// degrade" contract <see cref="LiveBindingsReader"/> already keeps for the
/// read side. Unlike that reader, this type never reads the file's content
/// at all, so a torn read mid-rewrite is not a concern here - the actual
/// read (and whatever it makes of a mid-rewrite state) happens later, when
/// the client's own re-fetch reaches <see cref="LiveBindingsReader"/>.
/// </summary>
public sealed class BindsFileWatcher : IDisposable
{
    private const string LogCategory = "Binds";

    /// <summary>
    /// How long to wait after the last file-system event before raising
    /// <see cref="Changed"/>. See the class remarks for why this is longer
    /// than <see cref="Core.GameState.StatusFileWatcher.DebounceWindow"/>.
    ///
    /// <b>Public, not <see langword="internal"/>,</b> unlike
    /// <see cref="Core.GameState.StatusFileWatcher"/>'s equivalent constants -
    /// <c>LunaPanel.Server</c> (unlike <c>LunaPanel.Core</c>) has no
    /// <c>InternalsVisibleTo</c> grant to the test assembly, and adding one
    /// for this one type's test hooks would widen the whole assembly's
    /// internal surface for a need four small members already cover.
    /// </summary>
    public static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _clock;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    private readonly FileSystemWatcher? _watcher;
    private ITimer? _debounceTimer;
    private bool _disposed;

    /// <summary>Raised once, debounced, after the watched file changes. Carries no payload - see the class remarks.</summary>
    public event Action? Changed;

    /// <summary>Whether a real (or, in a test, a simulated) watcher was actually started. <see langword="false"/> whenever no path was discovered, or starting the watcher failed.</summary>
    public bool IsActive => _watcher is not null;

    public BindsFileWatcher(DiscoveryResultHolder discovery, TimeProvider clock, IDiagnosticLog log)
        : this(discovery, clock, log, enableRealFileSystemWatcher: true)
    {
    }

    /// <summary>
    /// The real constructor. <paramref name="enableRealFileSystemWatcher"/>
    /// exists only for tests, exactly like
    /// <see cref="Core.GameState.StatusFileWatcher"/>'s own (there,
    /// <see langword="internal"/>) constructor - a real
    /// <see cref="FileSystemWatcher"/> delivers events on its own background
    /// thread at real OS timing, which a test driving the debounce
    /// deterministically through <see cref="SimulateFileSystemEventForTesting"/>
    /// and a fake clock cannot tolerate racing against. Production always
    /// goes through the 3-argument constructor above, which leaves this
    /// <see langword="true"/>. Public rather than <see langword="internal"/>
    /// for the same reason as <see cref="DebounceWindow"/> - see its remarks.
    /// </summary>
    public BindsFileWatcher(DiscoveryResultHolder discovery, TimeProvider clock, IDiagnosticLog log, bool enableRealFileSystemWatcher)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        var path = discovery.Current.BindingsSelection.SelectedFilePath;
        if (path is null)
        {
            _log.Info(LogCategory, "No bindings file selected at startup; live rebind notifications are unavailable until a restart or an explicit refresh");
            return;
        }

        string? directory;
        string? fileName;
        try
        {
            directory = Path.GetDirectoryName(path);
            fileName = Path.GetFileName(path);
        }
        catch (ArgumentException ex)
        {
            _log.Warn(LogCategory, "Selected bindings file path could not be watched", ex.Message);
            return;
        }

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory))
        {
            _log.Warn(LogCategory, "Selected bindings file's directory does not exist; live rebind notifications are unavailable", directory);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };
            watcher.Changed += OnWatcherEvent;
            watcher.Created += OnWatcherEvent;
            watcher.Renamed += OnWatcherEvent;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = enableRealFileSystemWatcher;
            _watcher = watcher;
        }
        catch (Exception ex)
        {
            _log.Warn(LogCategory, "Bindings file watcher could not be started; live rebind notifications are unavailable", ex.Message);
            return;
        }

        _log.Info(LogCategory, "Binds watcher started");
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => ScheduleDebouncedRaise();

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        _log.Warn(LogCategory, "Bindings file watcher reported an error", e.GetException()?.Message);

    private void ScheduleDebouncedRaise()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = _clock.CreateTimer(_ => RaiseChanged(), null, DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void RaiseChanged()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Simulates a raw file-system notification exactly as
    /// <see cref="FileSystemWatcher"/> would deliver one, without depending
    /// on real OS event-delivery timing - same purpose as
    /// <see cref="Core.GameState.StatusFileWatcher.SimulateFileSystemEventForTesting"/>.
    /// Public rather than <see langword="internal"/> for the same reason as
    /// <see cref="DebounceWindow"/> - see its remarks.
    /// </summary>
    public void SimulateFileSystemEventForTesting() => ScheduleDebouncedRaise();

    public void Dispose()
    {
        bool wasActive;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            wasActive = _watcher is not null;

            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnWatcherEvent;
                _watcher.Created -= OnWatcherEvent;
                _watcher.Renamed -= OnWatcherEvent;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
            }

            _debounceTimer?.Dispose();
        }

        if (wasActive)
        {
            _log.Info(LogCategory, "Binds watcher stopped");
        }
    }
}
