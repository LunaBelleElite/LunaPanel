using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Loads and saves a per-device manual colour override under an injected
/// directory - never discovers that directory itself, the same discipline
/// <c>LunaPanel.Core.Layouts.LayoutStore</c> already follows (see
/// <c>ref/docs/diagnostics.md</c>'s "PathRedactor: constructed with roots,
/// never discovers them"). Deliberately mirrors <c>LayoutStore</c>'s own
/// conventions rather than inventing new ones: an absent file is first-run
/// (here: "resolve automatically"), a corrupt file is renamed aside and
/// never overwritten in place, and <see cref="Save"/> writes to a temp file
/// and keeps exactly one previous generation as <c>.bak</c> before the
/// atomic swap into place.
///
/// <b>One deliberate difference from <c>LayoutStore</c>:</b> there is no
/// semantic validation step before <see cref="Save"/> writes - a
/// <see cref="ThemeOverride"/> is always valid by construction (three
/// already-parsed <see cref="HudColor"/> values; the only validation that
/// could ever fail, "is this a real #rrggbb colour", already happened at
/// the HTTP layer via <see cref="HudColor.TryParseHex"/> before a
/// <see cref="ThemeOverride"/> ever exists to save).
/// </summary>
public sealed class ThemeOverrideStore
{
    private const string LogCategory = "Theme";

    private readonly string _directory;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    public ThemeOverrideStore(string directory, IDiagnosticLog log)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Directory.CreateDirectory(_directory);
    }

    public ThemeOverrideLoadResult Load(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            if (!File.Exists(mainPath))
            {
                return ThemeOverrideLoadResult.NotFound();
            }

            var text = File.ReadAllText(mainPath);
            var parsed = ThemeOverrideJson.Parse(text);
            if (!parsed.Success)
            {
                RenameAsideAsCorrupt(mainPath);
                _log.Error(LogCategory, "Theme override file was corrupt and has been moved aside; falling back to automatic resolution", parsed.Error);
                return ThemeOverrideLoadResult.Corrupt();
            }

            return ThemeOverrideLoadResult.Loaded(parsed.Override!);
        }
    }

    public void Save(string deviceId, ThemeOverride overrideValue)
    {
        lock (_gate)
        {
            var json = ThemeOverrideJson.Serialize(overrideValue);
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

            _log.Info(LogCategory, "Saved manual theme colour override", $"deviceId={deviceId}");
        }
    }

    /// <summary>
    /// Clears a device's stored override, returning it to automatic
    /// resolution - never to whatever the override happened to be resolving
    /// to before it was cleared. The cleared override is kept as
    /// <c>.bak</c> (same one-generation-back convention as <see cref="Save"/>)
    /// rather than destroyed outright. Returns <see langword="false"/> (and
    /// changes nothing) when no override was stored.
    /// </summary>
    public bool Clear(string deviceId)
    {
        lock (_gate)
        {
            var mainPath = MainPath(deviceId);
            if (!File.Exists(mainPath))
            {
                return false;
            }

            var bakPath = mainPath + ".bak";
            File.Move(mainPath, bakPath, overwrite: true);
            _log.Info(LogCategory, "Cleared manual theme colour override; reverted to automatic resolution", $"deviceId={deviceId}");
            return true;
        }
    }

    private void RenameAsideAsCorrupt(string mainPath)
    {
        var corruptPath = $"{mainPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        File.Move(mainPath, corruptPath, overwrite: true);
    }

    private string MainPath(string deviceId) => Path.Combine(_directory, $"theme-override-{deviceId}.json");
}
