using System.Text;
using System.Text.Json;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Parses and serializes a device's stored <see cref="ThemeOverride"/> -
/// three <c>#rrggbb</c> strings, one per commander-facing role (Border,
/// Text, Lit). <see cref="Parse"/> never throws on malformed input - same
/// discipline as <c>LunaPanel.Core.Layouts.LayoutJson</c> - because a stored
/// override is runtime data from the player's own machine, not shipped
/// content.
/// </summary>
public static class ThemeOverrideJson
{
    public static ThemeOverrideParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ThemeOverrideParseResult.Fail($"Malformed theme override JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ThemeOverrideParseResult.Fail($"Malformed theme override: expected a JSON object at the root, got {root.ValueKind}.");
            }

            if (!TryGetColor(root, "border", out var border, out var error))
            {
                return ThemeOverrideParseResult.Fail(error!);
            }

            if (!TryGetColor(root, "text", out var text, out error))
            {
                return ThemeOverrideParseResult.Fail(error!);
            }

            if (!TryGetColor(root, "lit", out var lit, out error))
            {
                return ThemeOverrideParseResult.Fail(error!);
            }

            // Background is optional - absent means "derive the ground from
            // Text", the only thing every override file written before
            // 2026-09-10 can mean. Present-but-unparseable is still a
            // failure: optional is not the same as ignored.
            HudColor? background = null;
            if (root.TryGetProperty("background", out var backgroundProperty) && backgroundProperty.ValueKind != JsonValueKind.Null)
            {
                if (!TryGetColor(root, "background", out var backgroundColor, out error))
                {
                    return ThemeOverrideParseResult.Fail(error!);
                }

                background = backgroundColor;
            }

            return ThemeOverrideParseResult.Ok(new ThemeOverride(border, text, lit, background));
        }
    }

    public static string Serialize(ThemeOverride overrideValue)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("border", overrideValue.Border.ToHex());
            writer.WriteString("text", overrideValue.Text.ToHex());
            writer.WriteString("lit", overrideValue.Lit.ToHex());
            if (overrideValue.Background is { } background)
            {
                // Written only when the commander actually chose one - a
                // derived ground is never recorded as though it were a
                // choice (ref/docs/button-naming.md's override principle).
                writer.WriteString("background", background.ToHex());
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryGetColor(JsonElement root, string propertyName, out HudColor color, out string? error)
    {
        color = default;

        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            error = $"Malformed theme override: missing or non-string '{propertyName}'.";
            return false;
        }

        if (!HudColor.TryParseHex(property.GetString(), out color))
        {
            error = $"Malformed theme override: '{propertyName}' is not a valid #rrggbb colour.";
            return false;
        }

        error = null;
        return true;
    }
}
