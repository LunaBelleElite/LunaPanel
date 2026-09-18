using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Updates;

/// <summary>
/// Remembers which version last actually launched, so the app can tell -
/// from its own side, with no cooperation from the installer - that this
/// launch followed an update.
///
/// <b>Why this exists at all.</b> The self-update flow is completely silent:
/// no installer UI, no progress, nothing. Something has to confirm it
/// happened, and depending on the installer to pass a "just updated" flag
/// would be both fragile and blind to the other way a version changes - a
/// commander double-clicking a newer MSI over an older install by hand. A
/// version written here on every launch catches both paths identically.
///
/// Mirrors <c>LunaPanel.Core.Discovery.PathOverrideStore</c>'s discipline -
/// an injected directory it never discovers itself, one fixed file, and a
/// corrupt or unreadable file degrading rather than throwing - but not its
/// format: this is one version string as plain text, not JSON, because
/// that is genuinely all it is, and a file a person can read and fix by hand
/// is worth more here than a schema.
///
/// Lives in the same directory every other settings store already shares
/// (<c>LunaPanelDirectories.LayoutsDirectory</c>).
///
/// <b>Nothing here throws, including <see cref="Save"/>.</b> This runs during
/// tray startup, before any window exists for an error to be shown against,
/// and losing the ability to say "you've been updated" is never a reason to
/// stop LunaPanel from starting.
/// </summary>
public sealed class LastLaunchedVersionStore
{
    private const string LogCategory = "Updates";
    private const string FileName = "last-launched-version.txt";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public LastLaunchedVersionStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn(LogCategory, "Could not ensure the directory for the last-launched version file", ex.Message);
        }
    }

    /// <summary>
    /// The version recorded by the previous launch, or null when there is no
    /// usable record - no file, an empty or whitespace-only file, or one that
    /// could not be read at all. All three mean the same thing to the caller
    /// ("nothing to compare against"), so they are deliberately not
    /// distinguished.
    /// </summary>
    public string? Load()
    {
        lock (_gate)
        {
            return LoadUnlocked();
        }
    }

    public void Save(string version)
    {
        lock (_gate)
        {
            SaveUnlocked(version);
        }
    }

    /// <summary>
    /// The whole startup decision in one call: records that
    /// <paramref name="currentVersion"/> is running, and returns whether that
    /// is a change from what was recorded before.
    ///
    /// Returns false when there was no record at all. A first install is not
    /// an update, and telling a commander their brand new install "just
    /// updated itself" would be wrong on their very first impression of it.
    ///
    /// Read-then-write under one lock, so two launches racing cannot both
    /// read the old value and both announce the same update.
    /// </summary>
    public bool RecordLaunch(string currentVersion)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        lock (_gate)
        {
            var previous = LoadUnlocked();
            SaveUnlocked(currentVersion);

            return previous is not null
                && !string.Equals(previous, currentVersion.Trim(), StringComparison.Ordinal);
        }
    }

    private string? LoadUnlocked()
    {
        var path = MainPath();

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Trim('\0', ' ', '\t', '\r', '\n');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Warn(LogCategory, "Could not read the last-launched version file; treating it as absent", ex.Message);
            return null;
        }
    }

    private void SaveUnlocked(string version)
    {
        var path = MainPath();

        try
        {
            File.WriteAllText(path, version.Trim());
            _log.Info(LogCategory, "Recorded the launched version", version.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Warn(LogCategory, "Could not record the launched version", ex.Message);
        }
    }

    private string MainPath() => Path.Combine(_directory, FileName);
}
