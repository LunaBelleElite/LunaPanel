using System.Globalization;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Parses the <c>[Constants]</c>-style body EDHM-UI's own INI files use
/// (<c>Advanced.ini</c>, <c>Startup-Profile.ini</c>, <c>XML-Profile.ini</c>,
/// and any other <c>File</c> a <c>ThemeSettings.json</c> element might
/// name). This type never discovers a file itself - it takes already-read
/// text, same discipline as <c>LunaPanel.Core.Bindings.BindingsFile.Parse</c>
/// and <c>LunaPanel.Core.GameState.StatusJsonParser</c>: never throw,
/// tolerate anything malformed by simply not producing the entries it
/// can't parse.
/// </summary>
public static class IniConstants
{
    /// <summary>
    /// Parses every <c>key = value</c> line into a case-insensitive lookup.
    /// Section headers (<c>[Constants]</c>), <c>;</c> comments, and blank
    /// lines are skipped. A line that isn't a parseable <c>key = numeric
    /// value</c> pair is silently skipped rather than failing the whole
    /// parse - this is deliberately as tolerant as
    /// <c>FixtureIntegrityTests</c>'s own local INI reader, since a
    /// theme resolver falling through a step because one unrelated line in
    /// a large file didn't parse would be far more surprising than useful.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Parse(string iniContent)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in iniContent.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('['))
            {
                continue;
            }

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                result[key] = value;
            }
        }

        return result;
    }
}
