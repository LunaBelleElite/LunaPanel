using System.Globalization;
using System.Text.Json;

namespace LunaPanel.Core.GameState;

/// <summary>
/// Parses a single line of Elite's journal into a <see cref="JournalEvent"/>.
/// Like <see cref="StatusJsonParser"/> this never reads a file and never
/// throws - it is handed text that someone else read, and a journal line can
/// arrive torn, blank, or (after a game update) shaped in a way no version of
/// this code anticipated.
/// </summary>
/// <remarks>
/// The <c>ValueKind</c> checks here are not defensive padding: they are the
/// same trap <c>gamestate.md</c> records against <see cref="StatusJsonParser"/>.
/// <see cref="JsonElement.GetString"/> <em>throws</em>
/// <see cref="InvalidOperationException"/> when the element is not a string,
/// rather than returning <see langword="null"/>, so a line whose <c>event</c>
/// property is a number would take the exception straight out through the
/// caller. Every read below checks the kind first.
///
/// <see cref="JournalTailer"/> separately guarantees it never hands over a
/// half-written line at all. Both defences exist on purpose: the tailer's is
/// about not losing the line, this one is about not crashing on a shape we
/// did not expect.
/// </remarks>
public static class JournalLineParser
{
    /// <summary>
    /// Parses one line. Returns <see langword="null"/> - never throws - for a
    /// blank line, a line that is not a JSON object, a line that is not valid
    /// JSON at all (a torn write), or an object with no usable <c>event</c>
    /// name.
    /// </summary>
    public static JournalEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
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

            if (!root.TryGetProperty("event", out var eventElement) ||
                eventElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var eventName = eventElement.GetString();
            if (string.IsNullOrEmpty(eventName))
            {
                return null;
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    strings[property.Name] = property.Value.GetString()!;
                }
            }

            return new JournalEvent(eventName, ReadTimestamp(root), strings);
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            element.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
