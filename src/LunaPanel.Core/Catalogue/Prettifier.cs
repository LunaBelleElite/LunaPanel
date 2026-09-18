using System.Text.RegularExpressions;

namespace LunaPanel.Core.Catalogue;

/// <summary>
/// Turns a raw Frontier binding element name (PascalCase, occasionally with
/// an underscore-separated suffix like <c>_Buggy</c>) into a readable
/// fallback label for actions the curated catalogue doesn't cover - e.g.
/// <c>CamZoomIn</c> becomes <c>"Cam Zoom In"</c>. Known acronyms are kept
/// together and rendered fully uppercase rather than split letter-by-letter
/// or title-cased into something like "Ui".
/// </summary>
public static partial class Prettifier
{
    private static readonly HashSet<string> Acronyms = new(
        new[] { "UI", "FSS", "SAA", "SRV", "FSD", "SCO", "HUD", "DSS" },
        StringComparer.OrdinalIgnoreCase);

    // Splits a PascalCase/camelCase run into words while keeping acronym
    // runs (consecutive uppercase letters) together as one word - e.g.
    // "HUDMode" splits to "HUD" + "Mode", not "H" + "U" + "D" + "Mode".
    [GeneratedRegex(
        @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[A-Za-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])")]
    private static partial Regex WordBoundary();

    public static string Prettify(string elementName)
    {
        if (string.IsNullOrEmpty(elementName))
        {
            return elementName;
        }

        var words = new List<string>();
        foreach (var segment in elementName.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var part in WordBoundary().Split(segment))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                words.Add(Acronyms.Contains(part) ? part.ToUpperInvariant() : Capitalize(part));
            }
        }

        return string.Join(' ', words);
    }

    private static string Capitalize(string word) =>
        word.Length == 0
            ? word
            : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
}
