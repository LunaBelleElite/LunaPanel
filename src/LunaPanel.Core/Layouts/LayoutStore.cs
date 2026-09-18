using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Loads and saves per-device layouts under an injected directory - never
/// discovers that directory itself, matching every other file-backed type
/// under <c>LunaPanel.Core</c> (see <c>ref/docs/diagnostics.md</c>'s
/// "PathRedactor: constructed with roots, never discovers them" and
/// <c>LunaPanel.Core.Pairing.DeviceRegistry</c>, which takes its own state
/// path the same way).
///
/// <list type="bullet">
/// <item>An absent file is first-run, not an error.</item>
/// <item>A corrupt file (unreadable JSON, or JSON with no readable
/// <c>schemaVersion</c>, or JSON that is valid but doesn't parse into a
/// layout even after migration) is renamed aside to
/// <c>&lt;name&gt;.corrupt-&lt;utc timestamp&gt;</c> - never overwritten in
/// place - and logged at <see cref="DiagnosticLevel.Error"/> with only an
/// exception type name or parse-error text as detail, never a file path
/// (these logs are expected to be pasted into public bug reports).</item>
/// <item>A file whose <c>schemaVersion</c> is newer than this build
/// understands is refused and left byte-for-byte intact - see
/// <see cref="LayoutMigrator"/>.</item>
/// <item><see cref="Save"/> writes to a temp file, keeps exactly one
/// previous generation as <c>.bak</c>, then atomically swaps the temp file
/// into place.</item>
/// </list>
///
/// Deliberately does not run <see cref="LayoutValidator"/> on load: there is
/// no supported path that hand-edits a layout file (see
/// <c>ref/docs/design-decisions.md</c>'s "In-app editor, no file editing, no
/// restart"), so a structurally invalid-but-parseable file isn't a case
/// this store handles specially. <see cref="Save"/> is where validation
/// happens, via <see cref="LayoutValidator.ValidateForSave"/> - a bad layout
/// is refused before it ever reaches disk.
/// </summary>
public sealed class LayoutStore
{
    private const string LogCategory = "Layout";
    private const string FilePrefix = "layout-";
    private const string FileSuffix = ".json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public LayoutStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Raised, with the device id, after this store has actually changed a
    /// device's layout file on disk - a successful <see cref="Save"/> or a
    /// <see cref="RestorePrevious"/> that had something to restore. Never
    /// raised for a refused save, which changed nothing.
    ///
    /// <b>This exists because "there is no second way to write a layout
    /// file" is what makes one event enough.</b> Every mutation route
    /// (assign, label, long-press, latch, clear, template change, import,
    /// reset) goes through this class, so a subscriber that wants to know
    /// "did this device's layout change" needs exactly one subscription and
    /// cannot be bypassed by a route added later.
    ///
    /// The one subscriber today is <c>GET /api/panel/live</c>, which loads
    /// its layout once at connect and would otherwise keep computing lit
    /// state from the arrangement the device had when the stream opened -
    /// see <c>ref/docs/lit-state.md</c>'s "The glow that never arrived".
    /// Raised OUTSIDE this class's own
    /// lock, so a handler cannot deadlock against a save happening on
    /// another thread, and on whichever thread did the writing (an ASP.NET
    /// request thread in production).
    /// </summary>
    public event Action<string>? Saved;

    public LayoutLoadResult Load(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            if (!File.Exists(mainPath))
            {
                return LayoutLoadResult.NotFound();
            }

            var text = File.ReadAllText(mainPath);
            var migration = LayoutMigrator.Migrate(text);

            switch (migration.Outcome)
            {
                case LayoutMigrationOutcome.Malformed:
                    RenameAsideAsCorrupt(mainPath);
                    _log.Error(LogCategory, "Layout file was corrupt and has been moved aside", migration.ErrorDetail);
                    return LayoutLoadResult.Corrupt();

                case LayoutMigrationOutcome.TooNew:
                    _log.Error(
                        LogCategory,
                        "Layout file's schema version is newer than this build supports; refused and left untouched",
                        $"fileSchemaVersion={migration.FileSchemaVersion}");
                    return LayoutLoadResult.TooNewSchema();

                default:
                    var parseResult = LayoutJson.Parse(migration.MigratedJson!);
                    if (!parseResult.Success)
                    {
                        RenameAsideAsCorrupt(mainPath);
                        _log.Error(LogCategory, "Layout file was corrupt and has been moved aside", parseResult.Error);
                        return LayoutLoadResult.Corrupt();
                    }

                    if (migration.Outcome == LayoutMigrationOutcome.Migrated)
                    {
                        _log.Info(LogCategory, "Migrated layout to current schema version", $"fromVersion={migration.FileSchemaVersion}");
                    }

                    _log.Info(LogCategory, "Loaded layout", $"deviceId={deviceId}");
                    return LayoutLoadResult.Loaded(parseResult.Layout!);
            }
        }
    }

    public LayoutSaveResult Save(string deviceId, Layout layout, IReadOnlySet<string> knownActionNames)
    {
        // A refused save returns from inside the lock and raises nothing;
        // a real one falls out of it and raises Saved below. The event is
        // deliberately not raised while this store's own gate is held - see
        // its remarks.
        lock (_gate)
        {
            var validation = LayoutValidator.ValidateForSave(layout, knownActionNames);
            if (!validation.IsValid)
            {
                _log.Warn(LogCategory, "Refused to save an invalid layout", string.Join("; ", validation.Errors));
                return LayoutSaveResult.Invalid(validation.Errors);
            }

            var json = LayoutJson.Serialize(layout);
            var mainPath = MainPath(deviceId);
            var tempPath = mainPath + ".tmp";
            var bakPath = mainPath + ".bak";

            File.WriteAllText(tempPath, json);

            if (File.Exists(mainPath))
            {
                // Keep exactly one previous generation: the file that was
                // main until now becomes the new .bak, replacing whatever
                // .bak held before.
                File.Move(mainPath, bakPath, overwrite: true);
            }

            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(LogCategory, "Saved layout", $"deviceId={deviceId}");
        }

        Saved?.Invoke(deviceId);
        return LayoutSaveResult.Saved();
    }

    /// <summary>
    /// Swaps the current main file and its <c>.bak</c> - one operation,
    /// always available. Returns <see langword="false"/> (and changes
    /// nothing) when there is no <c>.bak</c> to restore.
    /// </summary>
    public bool RestorePrevious(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            var bakPath = mainPath + ".bak";

            if (!File.Exists(bakPath))
            {
                _log.Warn(LogCategory, "No previous generation to restore", $"deviceId={deviceId}");
                return false;
            }

            var swapPath = mainPath + ".restoring-tmp";
            var hadMain = File.Exists(mainPath);

            if (hadMain)
            {
                File.Move(mainPath, swapPath, overwrite: true);
            }

            File.Move(bakPath, mainPath, overwrite: true);

            if (hadMain)
            {
                File.Move(swapPath, bakPath, overwrite: true);
            }

            _log.Info(LogCategory, "Restored previous layout generation", $"deviceId={deviceId}");
        }

        Saved?.Invoke(deviceId);
        return true;
    }

    /// <summary>
    /// Every device id that currently has a layout file in this store's
    /// directory, in no guaranteed order. Backs the import list
    /// (<c>ref/docs/layout-import.md</c>): the layouts no live device owns
    /// are exactly these ids minus the registry's own.
    ///
    /// Filtering is done on the enumerated names rather than by handing a
    /// <c>layout-*.json</c> pattern to the filesystem, because Windows'
    /// pattern matching also matches 8.3 short names and would let
    /// <c>layout-X.json.bak</c>, <c>.tmp</c> and <c>.corrupt-...</c>
    /// generations - all of which this store writes itself, right beside the
    /// real files - through under some conditions. An import list that
    /// offered a device's own <c>.bak</c> as a separate device would be a
    /// confusing failure to diagnose from the symptom.
    /// </summary>
    public IReadOnlyList<string> ListDeviceIds()
    {
        lock (_gate)
        {
            var ids = new List<string>();

            foreach (var path in Directory.EnumerateFiles(_directory))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(FilePrefix, StringComparison.Ordinal) ||
                    !name.EndsWith(FileSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var id = name[FilePrefix.Length..^FileSuffix.Length];
                if (id.Length > 0)
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
    }

    /// <summary>
    /// Removes <c>layout-&lt;deviceId&gt;.json</c> and its <c>.bak</c>
    /// generation, if present - the delete half of the import/recovery
    /// chooser (<c>ref/docs/layout-import.md</c>), which can adopt an orphan
    /// but until now had no way to discard one. Best-effort per file, like
    /// <see cref="Save"/>'s own IO discipline: a delete that removes the main
    /// file but not its <c>.bak</c> (or vice versa) still reports what it
    /// could do rather than throwing.
    ///
    /// Returns whether the primary file actually existed, so a caller can
    /// tell "there was an orphan here and it is gone" apart from "there was
    /// nothing to delete" - the same shape <see cref="RestorePrevious"/>
    /// already uses for the same reason.
    /// </summary>
    public bool Delete(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            var bakPath = mainPath + ".bak";
            var hadMain = File.Exists(mainPath);

            if (hadMain)
            {
                File.Delete(mainPath);
            }

            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }

            if (hadMain)
            {
                _log.Info(LogCategory, "Deleted layout", $"deviceId={deviceId}");
            }

            return hadMain;
        }
    }

    private void RenameAsideAsCorrupt(string mainPath)
    {
        var corruptPath = $"{mainPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(mainPath, corruptPath, overwrite: true);
    }

    private string MainPath(string deviceId) => Path.Combine(_directory, $"{FilePrefix}{deviceId}{FileSuffix}");
}
