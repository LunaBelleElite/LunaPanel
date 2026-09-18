using System.Text.Json;
using System.Text.Json.Nodes;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Loads and saves the macros a commander authored on the device, under an
/// injected directory - never discovers that directory itself, the same
/// discipline <see cref="MacroTimingSettingsStore"/>,
/// <c>LunaPanel.Core.Layouts.LayoutStore</c> and every other file-backed
/// type in Core already follow.
///
/// <b>Machine-wide, deliberately NOT keyed by device id</b> - the same
/// choice <see cref="MacroTimingSettingsStore"/> made, for a stronger
/// reason (<c>ref/docs/macro-builder.md</c>, question 2). Every paired
/// device gets a new device id on every successful pair, so a per-device
/// macro file would orphan a commander's macros on a re-pair exactly as
/// their layout does - except that layout <em>recovery</em>
/// (<c>ref/docs/layout-import.md</c>) carries the layout file and nothing
/// beside it, so a recovered layout would arrive naming macro ids whose
/// definitions were left behind, rendering <c>UnknownMacro</c> on every
/// macro slot <em>while looking like the recovery worked</em>. Machine-wide
/// means a re-pair costs nothing: the macro was never keyed to the device,
/// and every id a recovered or imported layout carries still resolves.
///
/// <b>One file per macro</b> (<c>macro-&lt;id&gt;.json</c>), not one file
/// holding all of them, so a corrupt file costs one macro rather than every
/// macro - and so the loader's "one file, one definition" shape carries
/// over unchanged from <c>LunaPanel.Server.Macros.MacroLoader</c>'s
/// embedded resources.
///
/// <b>Persistence shape follows <c>LayoutStore</c>, not
/// <see cref="MacroTimingSettingsStore"/>:</b> temp file, one kept
/// <c>.bak</c> generation, then an atomic move into place. A macro is
/// hand-authored work a commander would genuinely lose, unlike a pair of
/// timing numbers that can be retyped in seconds.
///
/// <b>A corrupt file is renamed aside, never allowed to throw.</b>
/// <see cref="LoadAll"/> goes through <see cref="MacroDefinition.TryParse"/>
/// (never <c>Parse</c>) and moves an unreadable file to
/// <c>&lt;name&gt;.corrupt-&lt;utc timestamp&gt;</c>, logging at
/// <see cref="DiagnosticLevel.Error"/> with the parse error as detail and
/// never a file path - the identical treatment <c>LayoutStore</c> gives a
/// corrupt layout. This is not defensive decoration: these definitions are
/// read on <em>every</em> panel and press request, so a single damaged file
/// throwing would take the whole panel down rather than one button.
/// </summary>
public sealed class UserMacroStore
{
    private const string LogCategory = "Macro";
    private const string FilePrefix = "macro-";
    private const string FileSuffix = ".json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public UserMacroStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Every user macro currently on disk, ordered by id so two reads of an
    /// unchanged directory always agree. Re-reads the directory on every
    /// call - the "recompute on every read" discipline
    /// <c>MacroKnowledgeBuilder</c> already follows for bindings, and the
    /// reason a macro saved a moment ago fires without restarting the host.
    /// </summary>
    public IReadOnlyList<MacroDefinition> LoadAll() => LoadAllRecords().Select(r => r.Macro).ToList();

    /// <summary>
    /// Every user macro currently on disk, with whatever copy-to-edit
    /// provenance (<see cref="UserMacroRecord.SourceMacroId"/>/
    /// <see cref="UserMacroRecord.SourceStepsHash"/>) it was saved with -
    /// the shape the macro builder's staleness line needs
    /// (<c>ref/docs/macro-builder.md</c>, question 3). Otherwise identical to
    /// <see cref="LoadAll"/>, which this now projects from.
    /// </summary>
    public IReadOnlyList<UserMacroRecord> LoadAllRecords()
    {
        lock (_gate)
        {
            var macros = new List<UserMacroRecord>();

            // Filtered on the enumerated names rather than by handing a
            // "macro-*.json" pattern to the filesystem, for the reason
            // LayoutStore.ListDeviceIds' own remarks give: Windows' pattern
            // matching also matches 8.3 short names, and would let the
            // .bak/.tmp/.corrupt-... generations this store writes right
            // beside the real files through under some conditions. A .bak
            // offered as a second macro would be a confusing thing to
            // diagnose from a duplicated row in the picker.
            foreach (var path in Directory.EnumerateFiles(_directory).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (!name.StartsWith(FilePrefix, StringComparison.Ordinal) ||
                    !name.EndsWith(FileSuffix, StringComparison.Ordinal) ||
                    name.Length == FilePrefix.Length + FileSuffix.Length)
                {
                    continue;
                }

                var record = ReadOne(path);
                if (record is not null)
                {
                    macros.Add(record);
                }
            }

            return macros.OrderBy(r => r.Macro.Id, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// One macro by id, or <see langword="null"/> when there is no such
    /// file (or it was corrupt, in which case it is renamed aside and
    /// logged exactly as <see cref="LoadAll"/> would).
    /// </summary>
    public MacroDefinition? Load(string id) => LoadRecord(id)?.Macro;

    /// <summary>
    /// One macro by id, with its copy-to-edit provenance - the single-id
    /// counterpart to <see cref="LoadAllRecords"/>. <see langword="null"/>
    /// under the same conditions <see cref="Load"/> is.
    /// </summary>
    public UserMacroRecord? LoadRecord(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_gate)
        {
            var path = MainPath(id);
            return File.Exists(path) ? ReadOne(path) : null;
        }
    }

    /// <summary>
    /// Writes <paramref name="macro"/> to <c>macro-&lt;id&gt;.json</c>,
    /// keeping the previous generation as <c>.bak</c>.
    ///
    /// Persists unconditionally: the macro is already a parsed
    /// <see cref="MacroDefinition"/>, so the grammar has been checked by the
    /// one parser that checks it, and there is nothing further for this
    /// store to second-guess. <b>In particular it does not judge whether the
    /// macro will work</b> - an unbound action, a long <c>repeat</c>, a
    /// contextual toggle pressed twice: all are the commander's to author
    /// (<c>.claude-memory/never-gate-a-macro.md</c>, and
    /// <c>ref/docs/macro-builder.md</c>'s question 4).
    ///
    /// <paramref name="sourceMacroId"/>/<paramref name="sourceStepsHash"/>
    /// are the copy-to-edit provenance a fresh copy carries
    /// (<c>ref/docs/macro-builder.md</c>, question 3) - both
    /// <see langword="null"/> for a macro authored from scratch, or for an
    /// ordinary edit that has no provenance of its own to record. They are
    /// written as two extra top-level properties beside the grammar's own
    /// <c>id</c>/<c>name</c>/<c>steps</c>, never inside
    /// <see cref="MacroJson"/>'s output: <see cref="MacroDefinition.Parse"/>
    /// ignores properties it does not recognise, so a shipped macro (which
    /// never carries these) and a from-scratch user macro round-trip exactly
    /// as before, and there is still exactly one grammar for what a macro
    /// <em>is</em>.
    /// </summary>
    public void Save(MacroDefinition macro, string? sourceMacroId = null, string? sourceStepsHash = null)
    {
        ArgumentNullException.ThrowIfNull(macro);

        lock (_gate)
        {
            var json = WithSourceInfo(MacroJson.Serialize(macro), sourceMacroId, sourceStepsHash);
            var mainPath = MainPath(macro.Id);
            var tempPath = mainPath + ".tmp";
            var bakPath = mainPath + ".bak";

            File.WriteAllText(tempPath, json);

            if (File.Exists(mainPath))
            {
                // Keep exactly one previous generation, same as LayoutStore:
                // the file that was main until now becomes the new .bak,
                // replacing whatever .bak held before.
                File.Move(mainPath, bakPath, overwrite: true);
            }

            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(LogCategory, "Saved a user macro", $"macroId={macro.Id}, steps={macro.Steps.Count}");
        }
    }

    /// <summary>
    /// Deletes one user macro, returning <see langword="false"/> when there
    /// was no such file. The file becomes the <c>.bak</c> generation rather
    /// than being removed - the same one-generation rule
    /// <see cref="Save"/> follows, so a macro deleted by mistake is still on
    /// disk, and an id is minted once and never reused so a leftover
    /// <c>.bak</c> can never shadow a later macro.
    ///
    /// A slot that still names the deleted id is <b>not</b> touched, and
    /// nothing here counts references. The slot renders
    /// <c>UnknownMacro</c> and refuses at press time - exactly what the
    /// starter layout's slot 0 did for months before <c>request-docking</c>
    /// existed (<c>ref/docs/macro-builder.md</c>).
    /// </summary>
    public bool Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_gate)
        {
            var mainPath = MainPath(id);
            if (!File.Exists(mainPath))
            {
                return false;
            }

            File.Move(mainPath, mainPath + ".bak", overwrite: true);
            _log.Info(LogCategory, "Deleted a user macro", $"macroId={id}");
            return true;
        }
    }

    /// <summary>
    /// Reads and parses one file, or renames it aside and reports
    /// <see langword="null"/>. Goes through
    /// <see cref="MacroDefinition.TryParse"/>, never <c>Parse</c> - the
    /// difference is invisible while a file is healthy and is the entire
    /// point when it is not.
    /// </summary>
    private UserMacroRecord? ReadOne(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            // Not renamed aside: an unreadable file is not the same as an
            // unparseable one - it may be perfectly good content held open
            // by something else for a moment, and moving it would turn a
            // transient condition into a permanent loss.
            _log.Error(LogCategory, "A user macro file could not be read", ex.GetType().Name);
            return null;
        }

        var result = MacroDefinition.TryParse(text);
        if (!result.Success)
        {
            var corruptPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(path, corruptPath, overwrite: true);

            // Detail carries the parse error, never a file path - these logs
            // are expected to be pasted into public bug reports (ref/docs/
            // diagnostics.md), and the parse error already names the macro
            // id and step index, which is what actually helps.
            _log.Error(LogCategory, "A user macro file was corrupt and has been moved aside", result.Error);
            return null;
        }

        var (sourceMacroId, sourceStepsHash) = ReadSourceInfo(text);
        return new UserMacroRecord(result.Macro!, sourceMacroId, sourceStepsHash);
    }

    /// <summary>
    /// Adds <c>sourceMacroId</c>/<c>sourceStepsHash</c> as extra top-level
    /// properties on an already-serialized macro, or returns
    /// <paramref name="macroJson"/> unchanged when there is no source to
    /// record - a from-scratch macro's file looks exactly like it always
    /// has, byte for byte.
    /// </summary>
    private static string WithSourceInfo(string macroJson, string? sourceMacroId, string? sourceStepsHash)
    {
        if (sourceMacroId is null)
        {
            return macroJson;
        }

        var node = JsonNode.Parse(macroJson)!.AsObject();
        node["sourceMacroId"] = sourceMacroId;
        if (sourceStepsHash is not null)
        {
            node["sourceStepsHash"] = sourceStepsHash;
        }

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Reads the two extra top-level properties <see cref="WithSourceInfo"/>
    /// writes, tolerating their absence (a from-scratch macro, or one saved
    /// before this existed) and any unexpected shape - a malformed source
    /// field is not a reason to lose the macro <see cref="ReadOne"/> already
    /// confirmed parses.
    /// </summary>
    private static (string? SourceMacroId, string? SourceStepsHash) ReadSourceInfo(string macroJson)
    {
        try
        {
            using var document = JsonDocument.Parse(macroJson);
            var root = document.RootElement;
            var sourceMacroId = root.TryGetProperty("sourceMacroId", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            var sourceStepsHash = root.TryGetProperty("sourceStepsHash", out var hashElement) && hashElement.ValueKind == JsonValueKind.String
                ? hashElement.GetString()
                : null;
            return (sourceMacroId, sourceStepsHash);
        }
        catch (JsonException)
        {
            // MacroDefinition.TryParse already accepted this text, so this
            // is unreachable in practice - guarded anyway rather than
            // trusting that invariant across a future change to either
            // parser.
            return (null, null);
        }
    }

    /// <summary>
    /// An id becomes a file name component here, so one carrying a path
    /// separator or a traversal segment would reach outside this store's own
    /// directory. Ids are minted (<see cref="UserMacroIds.Mint"/>) and the
    /// endpoint checks them before they ever arrive, so this can only fire
    /// on a caller that skipped both - it throws rather than sanitizing,
    /// because silently rewriting an id to something safe would mean reading
    /// and writing different files under the same name.
    /// </summary>
    private string MainPath(string id)
    {
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{nameof(id)}' is not usable as a file name component.", nameof(id));
        }

        return Path.Combine(_directory, $"{FilePrefix}{id}{FileSuffix}");
    }
}
