using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Loads and saves <see cref="MacroTimingSettings"/> under an injected
/// directory - never discovers that directory itself, the same discipline
/// <see cref="LunaPanel.Core.Layouts.PanelSettingsStore"/> and
/// <c>LunaPanel.Core.Theme.ThemeOverrideStore</c> already follow.
///
/// <b>Deliberately NOT keyed by device id</b> - the one difference from every
/// sibling store in <c>LunaPanel.Core.Layouts</c>/<c>LunaPanel.Core.Theme</c>,
/// all of which write one file per device. <c>ref/docs/macro-timing.md</c>'s
/// "Scope: the PC, not the device" is the reasoning: input pacing is a
/// property of this machine and this copy of Elite, identical whichever
/// phone is driving it, so a per-device file here would mean pairing a
/// second device and finding macros unreliable again for a reason nothing on
/// screen could explain. One fixed file, <c>macro-timing.json</c>, shared by
/// every paired device - <see cref="Load"/> and <see cref="Save"/> take no
/// device id at all.
///
/// Same persistence shape as <c>PanelSettingsStore</c> (temp file, atomic
/// move into place) rather than <c>LayoutStore</c>'s kept <c>.bak</c>
/// generation - there is nothing here a commander could lose by an
/// interrupted write the way a hand-arranged layout could be; falling back to
/// <see cref="MacroTimingSettings.Default"/> is always a safe, reversible
/// recovery. An absent, corrupt, or out-of-range file quietly resolves to
/// that default rather than throwing.
///
/// Lives in the same directory <c>LayoutStore</c>/<c>ThemeOverrideStore</c>/
/// <c>PanelSettingsStore</c>/<c>OrphanMarkerStore</c> already share
/// (<c>LunaPanelDirectories.LayoutsDirectory</c>) - reusing it rather than
/// inventing a new directory just for one more small settings file.
/// </summary>
public sealed class MacroTimingSettingsStore
{
    private const string LogCategory = "Macro";
    private const string FileName = "macro-timing.json";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public MacroTimingSettingsStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Raised, carrying the settings just written, after <see cref="Save"/>
    /// has actually changed <c>macro-timing.json</c> on disk. Never raised by
    /// <see cref="Load"/> - reading is not a change, and
    /// <c>MacroRunner</c> loads this store on every macro run.
    ///
    /// <b>This exists because a timing change made on the PC has to reach a
    /// tablet that is already connected</b>, without the commander picking
    /// the tablet up (<c>ref/docs/macro-timing.md</c>'s "Getting there from
    /// the PC, and getting it to the tablet"). <c>GET /api/panel/live</c> is
    /// the one subscriber, and it pushes <b>this event's own argument</b>
    /// rather than anything it holds from connect time - the exact staleness
    /// that made a latched button never light
    /// (<c>ref/docs/lit-state.md</c>'s "The glow that never arrived").
    ///
    /// The same shape as <c>LayoutStore.Saved</c>, including where it is
    /// raised from: <b>outside this class's own lock</b>, so a handler cannot
    /// deadlock against a save happening on another thread, and on whichever
    /// thread did the writing (an ASP.NET request thread in production).
    /// There is no second way to write this file, so one event is enough.
    /// </summary>
    public event Action<MacroTimingSettings>? Saved;

    public MacroTimingSettings Load()
    {
        lock (_gate)
        {
            var mainPath = MainPath();
            if (!File.Exists(mainPath))
            {
                return MacroTimingSettings.Default;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(mainPath));
                var root = document.RootElement;

                var holdMs = root.TryGetProperty("holdMs", out var holdElement) && holdElement.ValueKind == JsonValueKind.Number
                    ? holdElement.GetInt32()
                    : (int)MacroTimingDefaults.DefaultHoldDuration.TotalMilliseconds;

                var gapMs = root.TryGetProperty("interPressGapMs", out var gapElement) && gapElement.ValueKind == JsonValueKind.Number
                    ? gapElement.GetInt32()
                    : (int)MacroTimingDefaults.InterPressGap.TotalMilliseconds;

                if (!MacroTimingSettings.IsValid(holdMs, gapMs))
                {
                    _log.Error(
                        LogCategory,
                        "Macro timing settings file held an out-of-range value; falling back to defaults",
                        $"holdMs={holdMs}, interPressGapMs={gapMs}");
                    return MacroTimingSettings.Default;
                }

                return new MacroTimingSettings(TimeSpan.FromMilliseconds(holdMs), TimeSpan.FromMilliseconds(gapMs));
            }
            catch (JsonException ex)
            {
                _log.Error(LogCategory, "Macro timing settings file was corrupt; falling back to defaults", ex.Message);
                return MacroTimingSettings.Default;
            }
        }
    }

    /// <summary>
    /// Persists <paramref name="settings"/> unconditionally - range
    /// validation is the caller's job
    /// (<c>LunaPanel.Server.Http.MacroTimingEndpoint.TryParse</c>), the same
    /// split <c>LayoutStore.Save</c> uses against <c>LayoutValidator</c>
    /// rather than duplicating the check here. <see cref="Load"/> still
    /// guards against an out-of-range value reaching this store some other
    /// way (a hand-edited file), as defense in depth.
    /// </summary>
    public void Save(MacroTimingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("holdMs", (int)settings.HoldDuration.TotalMilliseconds);
                writer.WriteNumber("interPressGapMs", (int)settings.InterPressGap.TotalMilliseconds);
                writer.WriteEndObject();
            }

            var mainPath = MainPath();
            var tempPath = mainPath + ".tmp";
            File.WriteAllBytes(tempPath, stream.ToArray());
            File.Move(tempPath, mainPath, overwrite: true);

            _log.Info(
                LogCategory,
                "Saved macro timing settings",
                $"holdMs={(int)settings.HoldDuration.TotalMilliseconds}, interPressGapMs={(int)settings.InterPressGap.TotalMilliseconds}");
        }

        Saved?.Invoke(settings);
    }

    private string MainPath() => Path.Combine(_directory, FileName);
}
