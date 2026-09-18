using System.Text;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.GameState;

/// <summary>
/// Follows Elite's live journal and feeds a <see cref="JournalStateStore"/>.
/// Never discovers the directory it watches - it is handed one, exactly like
/// <see cref="StatusFileWatcher"/>, and for the same reason: nothing in
/// <c>LunaPanel.Core</c> is allowed to work out where the game keeps its
/// files. <c>LunaPanel.Server</c>'s discovery does that and injects the
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Four things make this different from <see cref="StatusFileWatcher"/>,
/// and each of them is a real hazard rather than a hypothetical.</strong>
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <strong>The filename changes.</strong> Journals are named
/// <c>Journal.&lt;timestamp&gt;.&lt;nn&gt;.log</c> and a new one appears when
/// the game restarts, so this tailer takes a <em>directory</em> and re-picks
/// the newest file on every read cycle. <see cref="StatusFileWatcher"/> binds
/// to a fixed filename and simply could not do this.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>The game holds the file open the whole session.</strong> Opened
/// with <see cref="FileShare.ReadWrite"/> | <see cref="FileShare.Delete"/>
/// throughout; anything narrower fails on every read, which in the field
/// looks identical to "the game reported nothing".
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Lines arrive progressively.</strong> A read routinely catches a
/// half-written final line. This never parses one and never skips one: the
/// read position advances only as far as the last newline actually seen, so
/// the partial tail is simply re-read when it completes moments later.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>The directory is globbed, so <c>*-CONFLICT-*</c> matters.</strong>
/// Consumer sync clients leave conflict copies in this exact directory
/// (observed on the authoring machine), and a conflict copy of a journal
/// would be read as real history. <see cref="StatusFileWatcher"/> is
/// insulated from that only by watching one fixed name - luck about its
/// shape, not a decision. This one has to exclude them deliberately.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>The startup back-scan.</strong> The newest journal is read from
/// byte zero, not from its end, because <c>LoadGame</c> - which names the
/// vessel the commander is sitting in at session start - appears once, at the
/// top. Skip it and the vessel type is unknown until they next launch, which
/// is exactly wrong for a commander who logged in already inside their SRV
/// (a real case in the commander's own journal history, not a hypothetical).
/// Replaying an entire file is safe because
/// <see cref="JournalStateStore"/>'s edges are watermark-relative: every
/// replayed event lands below any mark taken afterwards, so a back-scan
/// cannot fire a stale edge at anyone.
/// </para>
/// <para>
/// <strong>No retry chain, unlike <see cref="StatusFileWatcher"/>.</strong>
/// That type retries a torn read because Elite rewrites <c>Status.json</c>
/// wholesale, so a failed read means the store stays stale until the next
/// write. The journal is append-only and the read position is never advanced
/// past what was actually read, so a failed read costs nothing at all: the
/// next cycle picks up from the same place. Simpler, and strictly safe here
/// in a way it would not be there.
/// </para>
/// </remarks>
public sealed class JournalTailer : IDisposable
{
    private const string LogCategory = "Journal";

    /// <summary>The filename shape Elite writes, and the only one this tailer will open.</summary>
    internal const string JournalFilePattern = "Journal.*.log";

    /// <summary>
    /// Filename marker for a consumer sync client's conflict copy. Any file
    /// carrying it is skipped - see this type's remarks.
    /// </summary>
    internal const string ConflictMarker = "-CONFLICT-";

    /// <summary>How long to wait after the last file-system event before actually reading.</summary>
    internal static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(30);

    /// <summary>How often the safety poll re-reads independently of the watcher.</summary>
    internal static readonly TimeSpan SafetyPollInterval = TimeSpan.FromSeconds(1);

    private readonly string _directory;
    private readonly JournalStateStore _store;
    private readonly TimeProvider _clock;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    private readonly FileSystemWatcher _watcher;
    private readonly ITimer _pollTimer;
    private ITimer? _debounceTimer;
    private bool _disposed;
    private bool _readInProgress;

    private string? _currentPath;
    private long _position;
    private bool _sawLoadGame;
    private bool _sawContinued;

    public JournalTailer(string directory, JournalStateStore store, TimeProvider clock, IDiagnosticLog log)
        : this(directory, store, clock, log, enableRealFileSystemWatcher: true)
    {
    }

    /// <summary>
    /// The real constructor. <paramref name="enableRealFileSystemWatcher"/>
    /// exists only for <c>JournalTailerTests</c>, for exactly the reason
    /// <see cref="StatusFileWatcher"/>'s equivalent overload does: a real
    /// <see cref="FileSystemWatcher"/> delivers events on its own background
    /// thread at OS timing, and a test driving the debounce and poll logic
    /// deterministically off a fake clock cannot tolerate a second, real,
    /// concurrent trigger racing it. Production always goes through the
    /// public constructor, which leaves this <see langword="true"/>.
    /// </summary>
    internal JournalTailer(string directory, JournalStateStore store, TimeProvider clock, IDiagnosticLog log, bool enableRealFileSystemWatcher)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        _watcher = new FileSystemWatcher(directory, JournalFilePattern)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName
        };
        _watcher.Changed += OnWatcherEvent;
        _watcher.Created += OnWatcherEvent;
        _watcher.Renamed += OnWatcherEvent;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = enableRealFileSystemWatcher;

        _pollTimer = _clock.CreateTimer(_ => OnSafetyPollTick(), null, SafetyPollInterval, SafetyPollInterval);

        _log.Info(LogCategory, "Journal tailer started");

        // The back-scan. An absent journal here just means the game has never
        // run on this machine - not an error. isBackScan: true here is what
        // Fix 4 (2026-09-07) hangs the whole "seed the tab tracker from
        // LoadGame" feature on - see JournalStateStore.Changed's remarks for
        // why this needs to be an explicit parameter rather than left to the
        // accident of when a caller subscribes.
        BeginReadCycle(isBackScan: true);
        ReportBackScanOutcome();
    }

    /// <summary>The journal file currently being followed, or <see langword="null"/> if none has been found yet.</summary>
    public string? CurrentJournalPath
    {
        get { lock (_gate) { return _currentPath; } }
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => ScheduleDebouncedRead();

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        _log.Warn(LogCategory, "Journal file watcher reported an error", e.GetException()?.Message);

    private void ScheduleDebouncedRead()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = _clock.CreateTimer(_ => BeginReadCycle(), null, DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSafetyPollTick() => BeginReadCycle();

    /// <param name="isBackScan">
    /// <see langword="true"/> only for the one call the constructor makes.
    /// Every other caller (the debounced watcher callback, the safety poll)
    /// leaves this <see langword="false"/> - see
    /// <see cref="JournalStateStore.Changed"/>'s remarks for why this needs
    /// to be a real parameter threaded from the single place that knows,
    /// rather than a field flipped for a window of time.
    /// </param>
    private void BeginReadCycle(bool isBackScan = false)
    {
        lock (_gate)
        {
            if (_disposed || _readInProgress)
            {
                return;
            }

            _readInProgress = true;
        }

        try
        {
            ReadCycle(isBackScan);
        }
        finally
        {
            lock (_gate) { _readInProgress = false; }
        }
    }

    private void ReadCycle(bool isBackScan)
    {
        var newest = ResolveNewestJournal();
        if (newest is null)
        {
            return;
        }

        string? outgoing = null;
        lock (_gate)
        {
            if (_currentPath is null)
            {
                _currentPath = newest;
                _position = 0;
            }
            else if (!string.Equals(_currentPath, newest, StringComparison.OrdinalIgnoreCase))
            {
                outgoing = _currentPath;
            }
        }

        if (outgoing is not null)
        {
            // Finish the outgoing file before switching. The last lines
            // written to it are as real as any other, and a game restart is
            // precisely a moment a consumer cares about. (A rollover can
            // never happen on the back-scan's own call - _currentPath starts
            // null - but isBackScan is threaded through regardless, rather
            // than hard-coding false, so this stays correct if that ever
            // changes.)
            Drain(outgoing, isBackScan);

            lock (_gate)
            {
                _currentPath = newest;
                _position = 0;
            }

            _log.Info(LogCategory, $"Journal rolled over to {Path.GetFileName(newest)}");
        }

        Drain(newest, isBackScan);
    }

    /// <summary>
    /// Picks the newest journal in the directory. Ordered by <em>write
    /// time</em>, not by filename: Elite has used two filename formats
    /// (<c>Journal.&lt;yyMMddHHmmss&gt;.NN.log</c> before 2022,
    /// <c>Journal.&lt;yyyy-MM-dd&gt;THHmmss.NN.log</c> since), and they do
    /// not sort against each other - <c>"210801120000"</c> is
    /// lexicographically greater than <c>"2026-01-01T120000"</c>, so a
    /// commander with pre-2022 journals still in the folder would have a
    /// name-ordered tailer bind to a years-old file. Filename is the
    /// tiebreak only.
    /// </summary>
    private string? ResolveNewestJournal()
    {
        try
        {
            return Directory.EnumerateFiles(_directory, JournalFilePattern)
                .Where(p => Path.GetFileName(p).IndexOf(ConflictMarker, StringComparison.OrdinalIgnoreCase) < 0)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException ex)
        {
            _log.Debug(LogCategory, "Could not list the journal directory", ex.Message);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn(LogCategory, "Access denied listing the journal directory", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads everything appended to <paramref name="path"/> since the last
    /// read, and advances the position only as far as the last complete line.
    /// </summary>
    private void Drain(string path, bool isBackScan)
    {
        byte[] buffer;
        long startedAt;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            lock (_gate)
            {
                if (stream.Length < _position)
                {
                    // The file got shorter than we had already read. Whatever
                    // happened to it, our position is meaningless now.
                    _log.Debug(LogCategory, $"{Path.GetFileName(path)} shrank below the read position; re-reading from the start");
                    _position = 0;
                }

                startedAt = _position;
            }

            var pending = stream.Length - startedAt;
            if (pending <= 0)
            {
                return;
            }

            buffer = new byte[pending];
            stream.Seek(startedAt, SeekOrigin.Begin);
            stream.ReadExactly(buffer, 0, buffer.Length);
        }
        catch (IOException ex)
        {
            // Costs nothing: the position was not advanced, so the next cycle
            // reads the same bytes again. See this type's remarks for why
            // there is no retry chain.
            _log.Debug(LogCategory, $"Could not read {Path.GetFileName(path)}", ex.Message);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warn(LogCategory, $"Access denied reading {Path.GetFileName(path)}", ex.Message);
            return;
        }

        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n');
        if (lastNewline < 0)
        {
            // Nothing complete yet - the whole of what we read is a
            // half-written line. Leave the position where it was so the line
            // is picked up whole once the game finishes writing it.
            return;
        }

        var completeByteCount = lastNewline + 1;
        var text = Encoding.UTF8.GetString(buffer, 0, completeByteCount);
        if (startedAt == 0)
        {
            text = text.TrimStart('\uFEFF');
        }

        lock (_gate)
        {
            _position = startedAt + completeByteCount;
        }

        foreach (var line in text.Split('\n'))
        {
            RecordLine(line.TrimEnd('\r'), isBackScan);
        }
    }

    private void RecordLine(string line, bool isBackScan)
    {
        var parsed = JournalLineParser.Parse(line);
        if (parsed is null)
        {
            return;
        }

        if (string.Equals(parsed.EventName, "LoadGame", StringComparison.OrdinalIgnoreCase))
        {
            _sawLoadGame = true;
        }
        else if (string.Equals(parsed.EventName, "Continued", StringComparison.OrdinalIgnoreCase))
        {
            // O24: this file's journal continues from an earlier part (or,
            // read here, is about to continue into a later one). Either way
            // it is the reason no LoadGame will be found at this file's top,
            // so say that explicitly now rather than let it present as an
            // unexplained "no LoadGame found" warning - see
            // ReportBackScanOutcome, which skips its generic warning once
            // this has fired.
            _sawContinued = true;
            _log.Info(LogCategory, $"{Path.GetFileName(_currentPath)} continues a multi-part journal (Continued seen); vessel type stays unknown until the next LaunchVessel or LaunchSRV");
        }

        _store.Record(parsed, isBackScan);
    }

    /// <summary>
    /// Says out loud, once, whether the back-scan found the anchor. A journal
    /// that continues an earlier one (Frontier writes a <c>Continued</c>
    /// event and starts a new part) has no <c>LoadGame</c> at its top, so the
    /// vessel type stays unknown until the next <c>LaunchVessel</c> or
    /// <c>LaunchSRV</c>. That is a real gap, and this is what makes it
    /// visible in the field instead of presenting as "the wrong page keeps
    /// showing".
    ///
    /// [2026-09-10: this used to say "LaunchVessel or DockSRV". <c>DockSRV</c>
    /// names the vessel the commander has just left and now clears the
    /// current vessel rather than setting it, so it never ends this gap -
    /// see <see cref="JournalStateStore.CurrentVesselType"/>.]
    ///
    /// [2026-09-12, O24: <c>Continued</c> is now a recognised event
    /// (<see cref="JournalVocabulary"/>), and <c>RecordLine</c> already logs
    /// an explicit Info line the moment it is seen, naming the seam by cause
    /// rather than leaving it to this method's generic warning. So when
    /// <c>_sawContinued</c> is true, that Info line has already said why
    /// there is no LoadGame, and the generic Warn below is skipped rather
    /// than doubled up on top of it.]
    /// </summary>
    private void ReportBackScanOutcome()
    {
        if (CurrentJournalPath is null)
        {
            _log.Info(LogCategory, "No journal file found yet - the game may simply never have run here");
            return;
        }

        if (!_sawLoadGame && !_sawContinued)
        {
            _log.Warn(LogCategory, "Journal back-scan found no LoadGame; vessel type is unknown until the next LaunchVessel or LaunchSRV");
        }
    }

    /// <summary>Simulates a raw file-system notification, as <c>StatusFileWatcher</c>'s equivalent hook does.</summary>
    internal void SimulateFileSystemEventForTesting() => ScheduleDebouncedRead();

    /// <summary>Simulates the safety poll's periodic tick firing.</summary>
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
            _pollTimer.Dispose();
        }

        _log.Info(LogCategory, "Journal tailer stopped");
    }
}
