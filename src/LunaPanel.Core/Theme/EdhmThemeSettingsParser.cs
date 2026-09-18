using System.Text.Json;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Parses the raw text content of EDHM-UI's <c>ThemeSettings.json</c> down
/// to the flat list of <see cref="EdhmColorElement"/>s a
/// <c>HudThemeResolver</c> can search - never discovers the file itself,
/// takes already-read text, never throws.
/// </summary>
public static class EdhmThemeSettingsParser
{
    /// <summary>
    /// Returns every <c>Color</c>-typed element across all <c>ui_groups</c>,
    /// in document order, or <see langword="null"/> if the JSON is
    /// malformed, not an object, or has no usable <c>ui_groups</c> array at
    /// all - signalling the whole file is unusable, distinct from a valid
    /// file that simply has no colours (which returns an empty list).
    ///
    /// Tolerates the one edge case a real theme file has: a <c>ui_groups</c>
    /// entry whose <c>"Elements"</c> is JSON <c>null</c> rather than an
    /// array (a naive iteration over that property throws; this treats it
    /// the same as an empty/absent list and moves on to the next group).
    /// </summary>
    public static IReadOnlyList<EdhmColorElement>? Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("ui_groups", out var uiGroups) || uiGroups.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var results = new List<EdhmColorElement>();

            foreach (var group in uiGroups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // "Elements": null is a real, committed edge case (see
                // Fixtures/edhm/ThemeSettings.sample.json's Group_Reserved) -
                // ValueKind.Array is required explicitly so ValueKind.Null
                // (and any other non-array shape) is skipped rather than
                // iterated.
                if (!group.TryGetProperty("Elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var element in elements.EnumerateArray())
                {
                    var parsed = TryParseElement(element);
                    if (parsed is not null)
                    {
                        results.Add(parsed);
                    }
                }
            }

            return results;
        }
    }

    private static EdhmColorElement? TryParseElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var valueType = GetString(element, "ValueType");
        if (!string.Equals(valueType, "Color", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = GetString(element, "Title");
        var file = GetString(element, "File");
        var key = GetString(element, "Key");

        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(file) || string.IsNullOrEmpty(key))
        {
            return null;
        }

        var keys = key.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return keys.Length < 3 ? null : new EdhmColorElement(title, file, keys);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
