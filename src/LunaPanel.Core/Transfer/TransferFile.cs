using System.Text;
using System.Text.Json;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Core.Transfer;

/// <summary>
/// The file a commander exports and hands to somebody
/// (<c>ref/docs/transfer.md</c>) - written here, read here, and nowhere else.
///
/// <b>This reverses a written decision.</b>
/// <c>ref/docs/layout-import.md</c>'s 2026-09-08 ruling said "No export to a
/// file... the in-place copy satisfies both stated needs and a file format is
/// a larger commitment." That was the right call for what had been asked for
/// then, which was recovering a device's own buttons after a re-pair. It is
/// wrong for what was asked for on 2026-09-10 - <i>"I want a way to import or
/// export profiles from the windows server program"</i> - because an in-place
/// copy cannot leave this PC, and leaving this PC is the entire request. The
/// old ruling is superseded, not forgotten; see that page.
///
/// <b>Two kinds, one format.</b> A profile carries an arrangement and the
/// macros its buttons name; a macros file carries macros alone. Both are one
/// JSON object with the same three header properties, so a reader can say
/// what a file is before it tries to use it - which is the difference between
/// "this is a macro file, not a profile" and a stack trace.
///
/// <b>Nothing here writes a key, a chord or a resolved label.</b> A layout
/// stores intent (<c>ref/docs/layouts.md</c>) and so does a macro
/// (<see cref="MacroJson"/>); this type serializes both through <em>their own
/// serializers</em> rather than walking their fields, so it could not write a
/// resolved binding even if somebody wanted it to. That is what lets a
/// profile move to a machine whose Elite is bound differently and still mean
/// the same thing.
/// </summary>
public static class TransferFile
{
    /// <summary>
    /// The property that says this is a LunaPanel file at all, and which
    /// kind. Checked first and by itself: a JSON file that is simply
    /// something else gets told so, rather than failing later on a missing
    /// <c>pages</c> array.
    /// </summary>
    public const string KindProperty = "lunaPanel";

    public const string ProfileKind = "profile";
    public const string MacrosKind = "macros";

    /// <summary>
    /// Bumped only when a later build writes something this build's
    /// <see cref="Parse"/> could not read correctly. A file claiming a
    /// higher number is refused and left alone, the same deny direction
    /// <see cref="LayoutMigrator"/> takes for a too-new
    /// <c>schemaVersion</c> - guessing at a shape you do not know is how a
    /// commander's arrangement gets silently half-imported.
    ///
    /// Note the layout inside a profile carries its <em>own</em>
    /// <c>schemaVersion</c> and is migrated by <see cref="LayoutMigrator"/>
    /// exactly as a file on disk is. The two versions are independent on
    /// purpose: the envelope changing has nothing to do with the layout
    /// grammar changing.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// What an exported file is called. A double extension, so it is plain
    /// JSON to every editor and every mail client while still being
    /// recognisably ours in a downloads folder.
    /// </summary>
    public const string FileExtension = ".lunapanel.json";

    private const string FormatVersionProperty = "formatVersion";
    private const string ExportedAtProperty = "exportedAt";
    private const string SourceNameProperty = "sourceName";
    private const string LayoutProperty = "layout";
    private const string MacrosProperty = "macros";

    /// <summary>
    /// A whole arrangement plus the definitions of the macros it names.
    /// <paramref name="macros"/> is the caller's choice - this type does not
    /// go looking for them, the same way <c>LayoutStore</c> does not go
    /// looking for its own directory.
    /// </summary>
    public static string WriteProfile(
        Layout layout,
        IReadOnlyList<MacroDefinition> macros,
        string? sourceName,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(macros);

        return Write(ProfileKind, layout, macros, sourceName, exportedAt);
    }

    /// <summary>Macros with no arrangement - the other half of what was asked for.</summary>
    public static string WriteMacros(
        IReadOnlyList<MacroDefinition> macros,
        string? sourceName,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(macros);

        return Write(MacrosKind, layout: null, macros, sourceName, exportedAt);
    }

    private static string Write(
        string kind,
        Layout? layout,
        IReadOnlyList<MacroDefinition> macros,
        string? sourceName,
        DateTimeOffset exportedAt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString(KindProperty, kind);
            writer.WriteNumber(FormatVersionProperty, FormatVersion);
            writer.WriteString(ExportedAtProperty, exportedAt.ToUniversalTime().ToString("o"));

            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                writer.WriteString(SourceNameProperty, sourceName);
            }

            if (layout is not null)
            {
                writer.WritePropertyName(LayoutProperty);
                // Through LayoutJson, never field by field: the one
                // serializer that knows the grammar is the one that writes
                // it, so a layout feature added later cannot be silently
                // dropped on the way into an export.
                Embed(writer, LayoutJson.Serialize(layout));
            }

            writer.WriteStartArray(MacrosProperty);
            foreach (var macro in macros)
            {
                Embed(writer, MacroJson.Serialize(macro));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Embed(Utf8JsonWriter writer, string json)
    {
        using var document = JsonDocument.Parse(json);
        document.RootElement.WriteTo(writer);
    }

    /// <summary>
    /// Reads a file somebody was handed. <b>Never throws</b>, and never
    /// half-reads: either the whole file is understood or the result is a
    /// refusal carrying one plain sentence. Every caller therefore knows,
    /// before it has written anything at all, whether the import can happen.
    ///
    /// Each macro goes through <see cref="MacroDefinition.TryParse"/> and
    /// the layout through <see cref="LayoutMigrator"/> then
    /// <see cref="LayoutJson.Parse"/> - the same two readers the live stores
    /// use. There is no second grammar here, so this cannot accept a macro
    /// the runner would choke on or a layout the panel could not draw.
    /// </summary>
    public static TransferParseResult Parse(string? text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text ?? string.Empty);
        }
        catch (JsonException)
        {
            // The message is deliberately not the parser's: it names a line
            // and column in a file the commander did not write, which reads
            // as a LunaPanel error rather than as "that is the wrong file".
            return TransferParseResult.Fail("That file is not readable as JSON. Pick the file LunaPanel exported.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return TransferParseResult.Fail("That file is not a LunaPanel export.");
            }

            if (!root.TryGetProperty(KindProperty, out var kindElement) ||
                kindElement.ValueKind != JsonValueKind.String)
            {
                return TransferParseResult.Fail("That file is not a LunaPanel export.");
            }

            var kind = kindElement.GetString();
            if (kind != ProfileKind && kind != MacrosKind)
            {
                return TransferParseResult.Fail("That file is not a LunaPanel export.");
            }

            if (!root.TryGetProperty(FormatVersionProperty, out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var formatVersion))
            {
                return TransferParseResult.Fail("That file is not a LunaPanel export.");
            }

            if (formatVersion > FormatVersion)
            {
                return TransferParseResult.Fail(
                    "That file was made by a newer version of LunaPanel than this one. Update LunaPanel and try again.");
            }

            var sourceName = ReadSourceName(root);

            if (!TryReadMacros(root, out var macros, out var macroError))
            {
                return TransferParseResult.Fail(macroError!);
            }

            if (kind == MacrosKind)
            {
                return TransferParseResult.MacrosOnly(macros!, sourceName);
            }

            if (!root.TryGetProperty(LayoutProperty, out var layoutElement) ||
                layoutElement.ValueKind != JsonValueKind.Object)
            {
                return TransferParseResult.Fail("That profile has no buttons in it.");
            }

            var migration = LayoutMigrator.Migrate(layoutElement.GetRawText());
            switch (migration.Outcome)
            {
                case LayoutMigrationOutcome.Malformed:
                    return TransferParseResult.Fail("The buttons in that profile could not be read.");

                case LayoutMigrationOutcome.TooNew:
                    return TransferParseResult.Fail(
                        "That profile was made by a newer version of LunaPanel than this one. Update LunaPanel and try again.");

                default:
                    var parsed = LayoutJson.Parse(migration.MigratedJson!);
                    return parsed.Success
                        ? TransferParseResult.Profile(parsed.Layout!, macros!, sourceName)
                        : TransferParseResult.Fail("The buttons in that profile could not be read.");
            }
        }
    }

    /// <summary>
    /// Display only, and treated as hostile input because it is: a string
    /// from a file, shown on a screen. Run through the same
    /// <see cref="DeviceNaming.SanitizeTypedName"/> a commander-typed device
    /// name goes through, so there is one rule for "a name a human wrote"
    /// rather than a second, looser one here.
    /// </summary>
    private static string? ReadSourceName(JsonElement root)
    {
        if (!root.TryGetProperty(SourceNameProperty, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var sanitized = DeviceNaming.SanitizeTypedName(element.GetString() ?? string.Empty);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static bool TryReadMacros(JsonElement root, out IReadOnlyList<MacroDefinition>? macros, out string? error)
    {
        macros = null;
        error = null;

        if (!root.TryGetProperty(MacrosProperty, out var arrayElement))
        {
            // Absent is empty, not broken: a profile whose buttons name only
            // shipped macros genuinely carries none, and refusing that file
            // would refuse the commonest export there is.
            macros = Array.Empty<MacroDefinition>();
            return true;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            error = "The macros in that file could not be read.";
            return false;
        }

        var read = new List<MacroDefinition>();
        foreach (var macroElement in arrayElement.EnumerateArray())
        {
            var parsed = MacroDefinition.TryParse(macroElement.GetRawText());
            if (!parsed.Success)
            {
                error = "The macros in that file could not be read.";
                return false;
            }

            read.Add(parsed.Macro!);
        }

        macros = read;
        return true;
    }

    /// <summary>
    /// What the browser saves the download as. The label is whatever the
    /// commander already calls the thing being exported - a device name, a
    /// macro name - reduced to characters every filesystem accepts, because
    /// a device called <c>Luna's phone / spare</c> is a perfectly good device
    /// name and a terrible path component.
    /// </summary>
    public static string FileNameFor(TransferKind kind, string? label, DateTimeOffset exportedAt)
    {
        var prefix = kind == TransferKind.Profile ? "lunapanel-profile" : "lunapanel-macros";
        var safeLabel = SanitizeForFileName(label);
        var stamp = exportedAt.ToUniversalTime().ToString("yyyyMMdd");

        return safeLabel.Length == 0
            ? $"{prefix}-{stamp}{FileExtension}"
            : $"{prefix}-{safeLabel}-{stamp}{FileExtension}";
    }

    private static string SanitizeForFileName(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var c in label)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
