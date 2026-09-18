using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Theme;

/// <summary>
/// Watches the WHOLE ini directory <see cref="LiveThemeResolver.ReadInputs"/>
/// reads through for the active EDHM-UI-V3 edition -
/// <see cref="EdhmDiscoveryResult.SelectActiveEdition"/>'s
/// <c>IniDirectory</c>, resolved once at construction from the same
/// discovery result <see cref="LiveThemeResolver"/> itself was built from -
/// and raises <see cref="Changed"/> whenever any file in it is rewritten, so
/// an already-open live channel (<c>GET /api/panel/live</c>) can tell its
/// device "your theme changed, re-fetch" instead of an EDHM colour edit
/// staying invisible until some unrelated event happens to trigger a
/// re-fetch.
///
/// <b>A whole directory, unlike <see cref="Bindings.BindsFileWatcher"/>'s one
/// fixed file</b>: a theme is not one file but several
/// (<c>ThemeSettings.json</c>, <c>XML-Profile.ini</c>, and whatever other
/// <c>*.ini</c> files <see cref="LiveThemeResolver.ReadInputs"/> enumerates),
/// and a real theme edit can touch any of them - so this watches the
/// directory itself, with no filename filter, rather than trying to name
/// every file that could matter.
///
/// <b>What this fixes, and what it deliberately does not.</b> A colour edit
/// made to the SAME edition this server already selected now reaches an open
/// device live. Switching which EDITION is active in EDHM (Odyssey vs.
/// Horizons), or EDHM being installed for the first time after this server
/// already started, is UNCHANGED by this type and stays exactly as invisible
/// as <see cref="LiveThemeResolver"/>'s own remarks already document for a
/// startup-frozen discovery result - only a restart re-selects an edition.
///
/// Raises a bare signal, never the theme content itself: the live channel
/// already has a correct, fully-annotated re-fetch path (<c>GET /api/panel</c>,
/// which the client's <c>loadPanel()</c> already calls, and <c>GET
/// /api/theme</c> via <c>loadTheme()</c> for the settings gear) that
/// threading theme content into the live payload would only duplicate for no
/// gain - same reasoning as <see cref="Bindings.BindsFileWatcher"/>'s own
/// remarks.
///
/// Debounced the same way <see cref="Bindings.BindsFileWatcher"/> is, and for
/// the same reason: a theme edit is a rare, discrete, commander-initiated
/// action with nothing time-critical riding on the signal arriving within
/// tens of milliseconds, so the same <see cref="DebounceWindow"/> value is
/// reused rather than re-derived.
///
/// Never throws: no EDHM install, no resolvable edition, a missing/vanished
/// ini directory, or a watcher that fails to start all degrade to "no live
/// theme-change notifications" rather than a crash - the same "a layout
/// can't break, only degrade" contract <see cref="LiveThemeResolver"/>
/// already keeps for the read side. This type never reads any file's
/// content at all, so a torn read mid-rewrite is not a concern here - the
/// actual read (and whatever it makes of a mid-rewrite state) happens later,
/// when the client's own re-fetch reaches <see cref="LiveThemeResolver"/>.
/// </summary>
public sealed class ThemeFileWatcher : IDisposable
{
    private const string LogCategory = "Theme";

    /// <summary>
    /// How long to wait after the last file-system event before raising
    /// <see cref="Changed"/>. Reused verbatim from
    /// <see cref="Bindings.BindsFileWatcher.DebounceWindow"/> - see that
    /// constant's own remarks for why 250ms fits a rare, discrete,
    /// commander-initiated edit; the same reasoning applies to a theme edit
    /// unchanged.
    ///
    /// Public, not <see langword="internal"/>, for the same reason as
    /// <see cref="Bindings.BindsFileWatcher.DebounceWindow"/> - see its
    /// remarks.
    /// </summary>
    public static readonly TimeSpan DebounceWindow = Bindings.BindsFileWatcher.DebounceWindow;

    private readonly TimeProvider _clock;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    private readonly FileSystemWatcher? _watcher;
    private ITimer? _debounceTimer;
    private bool _disposed;

    /// <summary>Raised once, debounced, after any file in the watched theme directory changes. Carries no payload - see the class remarks.</summary>
    public event Action? Changed;

    /// <summary>Whether a real (or, in a test, a simulated) watcher was actually started. <see langword="false"/> whenever no resolvable edition/directory was discovered, or starting the watcher failed.</summary>
    public bool IsActive => _watcher is not null;

    public ThemeFileWatcher(PathDiscoveryResult discovery, TimeProvider clock, IDiagnosticLog log)
        : this(discovery, clock, log, enableRealFileSystemWatcher: true)
    {
    }

    /// <summary>
    /// The real constructor. <paramref name="enableRealFileSystemWatcher"/>
    /// exists only for tests, exactly like
    /// <see cref="Bindings.BindsFileWatcher"/>'s own equivalent constructor -
    /// see its remarks for why. Production always goes through the
    /// 3-argument constructor above, which leaves this <see langword="true"/>.
    /// Public rather than <see langword="internal"/> for the same reason as
    /// <see cref="DebounceWindow"/> - see its remarks.
    /// </summary>
    public ThemeFileWatcher(PathDiscoveryResult discovery, TimeProvider clock, IDiagnosticLog log, bool enableRealFileSystemWatcher)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        // The exact same selection LiveThemeResolver.ReadInputs uses - see
        // EdhmDiscoveryResult.SelectActiveEdition's own remarks for why this
        // is a shared method rather than two independently-written copies.
        var edition = discovery.Edhm.SelectActiveEdition();
        var directory = edition?.IniDirectory;

        if (string.IsNullOrEmpty(directory))
        {
            _log.Info(LogCategory, "No resolvable EDHM edition at startup; live theme-change notifications are unavailable until a restart");
            return;
        }

        if (!Directory.Exists(directory))
        {
            _log.Warn(LogCategory, "Selected EDHM edition's ini directory does not exist; live theme-change notifications are unavailable", directory);
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory)
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
            _log.Warn(LogCategory, "Theme file watcher could not be started; live theme-change notifications are unavailable", ex.Message);
            return;
        }

        _log.Info(LogCategory, "Theme watcher started");
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => ScheduleDebouncedRaise();

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        _log.Warn(LogCategory, "Theme file watcher reported an error", e.GetException()?.Message);

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
    /// <see cref="Bindings.BindsFileWatcher.SimulateFileSystemEventForTesting"/>.
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
            _log.Info(LogCategory, "Theme watcher stopped");
        }
    }
}
