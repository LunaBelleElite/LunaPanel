using System.Text;
using System.Text.Json;

namespace LunaPanel.Core.Layouts;

/// <summary>
/// Parses and serializes the current-schema-version JSON shape for a
/// <see cref="Layout"/> (see <c>ref/docs/layouts.md</c> for the shape
/// itself). <see cref="Parse"/> takes already-migrated JSON text at
/// <see cref="LayoutMigrator.CurrentSchemaVersion"/> - it knows nothing
/// about older schema shapes; that is <see cref="LayoutMigrator"/>'s job,
/// kept separate so this type only ever has one shape to understand.
///
/// <see cref="Parse"/> never throws on malformed input - same discipline as
/// <c>LunaPanel.Core.GameState.StatusJsonParser</c> and
/// <c>LunaPanel.Core.Bindings.BindingsFile</c> - because a layout file is
/// runtime data from the player's machine, not shipped content.
///
/// Deliberately does not enforce semantic rules here (exactly one of
/// action/macro per slot, index range/uniqueness, label length, unknown
/// action names) - that is <see cref="LayoutValidator"/>'s job. Keeping
/// them separate means a structurally well-formed but semantically invalid
/// layout can still be parsed and inspected, rather than being an
/// unparseable blob the moment one rule is violated.
/// </summary>
public static class LayoutJson
{
    public static LayoutParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return LayoutParseResult.Fail($"Malformed layout JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return LayoutParseResult.Fail($"Malformed layout: expected a JSON object at the root, got {root.ValueKind}.");
            }

            if (!root.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var schemaVersion))
            {
                return LayoutParseResult.Fail("Malformed layout: missing or invalid numeric 'schemaVersion'.");
            }

            if (!root.TryGetProperty("pages", out var pagesElement) || pagesElement.ValueKind != JsonValueKind.Array)
            {
                return LayoutParseResult.Fail("Malformed layout: missing 'pages' array.");
            }

            var pages = new List<LayoutPage>();
            foreach (var pageElement in pagesElement.EnumerateArray())
            {
                if (!TryParsePage(pageElement, out var page, out var error))
                {
                    return LayoutParseResult.Fail(error!);
                }

                pages.Add(page!);
            }

            return LayoutParseResult.Ok(new Layout(schemaVersion, pages));
        }
    }

    public static string Serialize(Layout layout)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", layout.SchemaVersion);
            writer.WriteStartArray("pages");
            foreach (var page in layout.Pages)
            {
                WritePage(writer, page);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePage(Utf8JsonWriter writer, LayoutPage page)
    {
        writer.WriteStartObject();
        writer.WriteString("name", page.Name);
        writer.WriteString("templateId", page.TemplateId);

        // [2026-09-16] Folders. Both omitted when absent, the same convention
        // showWhen and latch/hold already follow here - so every layout
        // written before folders existed still round-trips byte-identical,
        // and "no id" stays one spelling rather than two (absent vs null).
        if (page.Id is not null)
        {
            writer.WriteString("id", page.Id);
        }

        if (page.IsFolderOwned)
        {
            writer.WriteBoolean("folderOwned", true);
        }

        // Omitted entirely when the page is context-free. An empty showWhen
        // and an absent one are the same statement (see LayoutPage.ShowWhen),
        // and writing both spellings is how two spellings of one meaning
        // eventually drift apart.
        if (page.ShowWhen is { Count: > 0 })
        {
            writer.WriteStartArray("showWhen");
            foreach (var token in page.ShowWhen)
            {
                writer.WriteStringValue(token);
            }

            writer.WriteEndArray();
        }

        writer.WriteStartArray("slots");
        foreach (var slot in page.Slots)
        {
            WriteSlot(writer, slot);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("parked");
        foreach (var slot in page.Parked)
        {
            WriteSlot(writer, slot);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSlot(Utf8JsonWriter writer, LayoutSlot slot)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", slot.Index);

        if (slot.Action is not null)
        {
            writer.WriteString("action", slot.Action);
        }

        if (slot.Macro is not null)
        {
            writer.WriteString("macro", slot.Macro);
        }

        if (slot.Label is not null)
        {
            writer.WriteString("label", slot.Label);
        }

        // [2026-09-16] The page this button opens, by LayoutPage.Id - never
        // an index or a name (see LayoutPage.Id for why both are unsafe to
        // store). Same omit-when-absent convention as everything else here.
        if (slot.FolderPage is not null)
        {
            writer.WriteString("folderPage", slot.FolderPage);
        }

        // Omitted entirely when false, the same way an absent showWhen and an
        // empty one are kept from being two spellings of one meaning - and
        // so a layout that predates latching round-trips byte-identical.
        if (slot.Latch)
        {
            writer.WriteBoolean("latch", true);
        }

        // Same omit-when-false convention as latch above, and for the same
        // reason: a layout written before the hold gesture existed - or any
        // slot that never asked for it - round-trips byte-identical.
        if (slot.Hold)
        {
            writer.WriteBoolean("hold", true);
        }

        if (slot.LongPress is not null)
        {
            writer.WriteStartObject("longPress");
            if (slot.LongPress.Action is not null)
            {
                writer.WriteString("action", slot.LongPress.Action);
            }

            if (slot.LongPress.Macro is not null)
            {
                writer.WriteString("macro", slot.LongPress.Macro);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static bool TryParsePage(JsonElement pageElement, out LayoutPage? page, out string? error)
    {
        page = null;

        if (pageElement.ValueKind != JsonValueKind.Object)
        {
            error = $"Malformed layout: a page entry is not a JSON object (got {pageElement.ValueKind}).";
            return false;
        }

        if (!TryGetRequiredString(pageElement, "name", out var name, out error))
        {
            error = $"Malformed layout: a page {error}";
            return false;
        }

        if (!TryGetRequiredString(pageElement, "templateId", out var templateId, out error))
        {
            error = $"Page '{name}': {error}";
            return false;
        }

        if (!TryParseSlots(pageElement, name, "slots", out var slots, out error))
        {
            return false;
        }

        if (!TryParseSlots(pageElement, name, "parked", out var parked, out error))
        {
            return false;
        }

        if (!TryParseShowWhen(pageElement, name, out var showWhen, out error))
        {
            return false;
        }

        if (!TryGetOptionalString(pageElement, "id", out var id, out error))
        {
            error = $"Page '{name}': {error}";
            return false;
        }

        if (!TryGetOptionalBool(pageElement, "folderOwned", out var folderOwned, out error))
        {
            error = $"Page '{name}': {error}";
            return false;
        }

        page = new LayoutPage(name, templateId, slots!, parked!, showWhen, id, folderOwned);
        error = null;
        return true;
    }

    /// <summary>
    /// A page's declared vessel context (<c>ref/docs/vessel-context.md</c>).
    /// Absent stays <see langword="null"/> - "context-free", which every
    /// layout written before this feature existed is - rather than becoming
    /// an empty list. Malformation is reported, not tolerated: unlike a
    /// missing optional array, a <c>showWhen</c> the commander clearly meant
    /// to write and got wrong should say so rather than silently making
    /// their page context-free forever.
    /// </summary>
    private static bool TryParseShowWhen(JsonElement pageElement, string pageName, out IReadOnlyList<string>? showWhen, out string? error)
    {
        showWhen = null;
        error = null;

        if (!pageElement.TryGetProperty("showWhen", out var arrayElement))
        {
            return true;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            error = $"Page '{pageName}': 'showWhen' is not an array.";
            return false;
        }

        var tokens = new List<string>();
        foreach (var tokenElement in arrayElement.EnumerateArray())
        {
            if (tokenElement.ValueKind != JsonValueKind.String)
            {
                error = $"Page '{pageName}': 'showWhen' contains an entry that is not a string.";
                return false;
            }

            tokens.Add(tokenElement.GetString()!);
        }

        showWhen = tokens;
        return true;
    }

    private static bool TryParseSlots(JsonElement pageElement, string pageName, string propertyName, out IReadOnlyList<LayoutSlot>? slots, out string? error)
    {
        slots = Array.Empty<LayoutSlot>();
        error = null;

        if (!pageElement.TryGetProperty(propertyName, out var arrayElement))
        {
            // Absent is lenient - treated as an empty list, not an error.
            return true;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            error = $"Page '{pageName}': '{propertyName}' is not an array.";
            return false;
        }

        var list = new List<LayoutSlot>();
        foreach (var slotElement in arrayElement.EnumerateArray())
        {
            if (!TryParseSlot(slotElement, pageName, propertyName, out var slot, out error))
            {
                return false;
            }

            list.Add(slot!);
        }

        slots = list;
        return true;
    }

    private static bool TryParseSlot(JsonElement slotElement, string pageName, string context, out LayoutSlot? slot, out string? error)
    {
        slot = null;

        if (slotElement.ValueKind != JsonValueKind.Object)
        {
            error = $"Page '{pageName}', {context}: a slot entry is not a JSON object.";
            return false;
        }

        if (!slotElement.TryGetProperty("index", out var indexElement)
            || indexElement.ValueKind != JsonValueKind.Number
            || !indexElement.TryGetInt32(out var index))
        {
            error = $"Page '{pageName}', {context}: a slot is missing a valid integer 'index'.";
            return false;
        }

        if (!TryGetOptionalString(slotElement, "action", out var action, out error))
        {
            error = $"Page '{pageName}', slot {index}: {error}";
            return false;
        }

        if (!TryGetOptionalString(slotElement, "macro", out var macro, out error))
        {
            error = $"Page '{pageName}', slot {index}: {error}";
            return false;
        }

        if (!TryGetOptionalString(slotElement, "label", out var label, out error))
        {
            error = $"Page '{pageName}', slot {index}: {error}";
            return false;
        }

        if (!TryGetOptionalString(slotElement, "folderPage", out var folderPage, out error))
        {
            error = $"Page '{pageName}', slot {index}: {error}";
            return false;
        }

        var latch = false;
        if (slotElement.TryGetProperty("latch", out var latchElement))
        {
            if (latchElement.ValueKind != JsonValueKind.True && latchElement.ValueKind != JsonValueKind.False)
            {
                error = $"Page '{pageName}', slot {index}: 'latch' is not a boolean.";
                return false;
            }

            latch = latchElement.GetBoolean();
        }

        var hold = false;
        if (slotElement.TryGetProperty("hold", out var holdElement))
        {
            if (holdElement.ValueKind != JsonValueKind.True && holdElement.ValueKind != JsonValueKind.False)
            {
                error = $"Page '{pageName}', slot {index}: 'hold' is not a boolean.";
                return false;
            }

            hold = holdElement.GetBoolean();
        }

        LongPressAction? longPress = null;
        if (slotElement.TryGetProperty("longPress", out var longPressElement))
        {
            if (longPressElement.ValueKind != JsonValueKind.Object)
            {
                error = $"Page '{pageName}', slot {index}: 'longPress' is not a JSON object.";
                return false;
            }

            if (!TryGetOptionalString(longPressElement, "action", out var lpAction, out error))
            {
                error = $"Page '{pageName}', slot {index} long-press: {error}";
                return false;
            }

            if (!TryGetOptionalString(longPressElement, "macro", out var lpMacro, out error))
            {
                error = $"Page '{pageName}', slot {index} long-press: {error}";
                return false;
            }

            longPress = new LongPressAction(lpAction, lpMacro);
        }

        slot = new LayoutSlot(index, action, macro, label, longPress, latch, hold, folderPage);
        error = null;
        return true;
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string value, out string? error)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            error = $"is missing a string '{propertyName}' property.";
            return false;
        }

        var s = property.GetString();
        if (string.IsNullOrEmpty(s))
        {
            error = $"has an empty '{propertyName}' property.";
            return false;
        }

        value = s;
        error = null;
        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, out string? value, out string? error)
    {
        value = null;
        error = null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"'{propertyName}' is not a string.";
            return false;
        }

        value = property.GetString();
        return true;
    }

    /// <summary>
    /// [2026-09-16] The boolean twin of <see cref="TryGetOptionalString"/>,
    /// for <c>folderOwned</c>. Absent is <see langword="false"/> (every page
    /// written before folders existed); present-but-not-a-boolean is
    /// REPORTED rather than tolerated, matching the existing inline
    /// <c>latch</c>/<c>hold</c> checks - a page silently read as "not a
    /// folder's interior" would reappear in the tab bar, which looks like a
    /// mystery page rather than like a malformed file.
    /// </summary>
    private static bool TryGetOptionalBool(JsonElement element, string propertyName, out bool value, out string? error)
    {
        value = false;
        error = null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)
        {
            error = $"'{propertyName}' is not a boolean.";
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
