using System.Xml;
using System.Xml.Linq;

namespace LunaPanel.Core.Bindings;

/// <summary>
/// Parses the raw text content of a player's Elite Dangerous <c>.binds</c>
/// file. This type never discovers the file itself - it takes already-read
/// XML text, matching <c>LunaPanel.Core.GameState.StatusJsonParser</c>'s own
/// discipline: never throw, always hand back a result the caller can log.
/// </summary>
/// <param name="PresetName">The root <c>&lt;Root PresetName="..."&gt;</c> attribute, or <see langword="null"/> if absent.</param>
/// <param name="MajorVersion">The root's <c>MajorVersion</c> attribute, or <see langword="null"/> if absent or not an integer.</param>
/// <param name="MinorVersion">The root's <c>MinorVersion</c> attribute, or <see langword="null"/> if absent or not an integer.</param>
/// <param name="Elements">Every bindable action found, in file order. Non-binding root children (e.g. <c>&lt;KeyboardLayout&gt;</c>) are not included. When an action name repeats, only the first occurrence is kept - matching the game's own loader, which logs "Only the first will be used."</param>
public sealed record BindingsFile(
    string? PresetName,
    int? MajorVersion,
    int? MinorVersion,
    IReadOnlyList<BindingElement> Elements)
{
    /// <summary>
    /// Parses <paramref name="xml"/> into a <see cref="BindingsFile"/>.
    /// Never throws: malformed or truncated XML (the file can be read
    /// mid-write, same hazard as <c>Status.json</c>) produces a failed
    /// <see cref="BindingsParseResult"/> instead.
    /// </summary>
    public static BindingsParseResult Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            return BindingsParseResult.Fail($"Malformed .binds XML: {ex.Message}");
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Root")
        {
            return BindingsParseResult.Fail("Malformed .binds XML: expected a root <Root> element.");
        }

        var presetName = (string?)root.Attribute("PresetName");
        var majorVersion = ParseIntAttribute(root, "MajorVersion");
        var minorVersion = ParseIntAttribute(root, "MinorVersion");

        var elements = new List<BindingElement>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in root.Elements())
        {
            var name = child.Name.LocalName;

            // The game's own loader keeps the first occurrence of a
            // duplicated element name and discards the rest (its
            // BindingLoadingErrors.log states "Only the first will be
            // used.") - matched here rather than letting a later duplicate
            // silently overwrite the first.
            if (!seenNames.Add(name))
            {
                continue;
            }

            var primary = ParseSlot(child.Element("Primary"));
            var secondary = ParseSlot(child.Element("Secondary"));

            // Non-binding root children (e.g. <KeyboardLayout>en-US</KeyboardLayout>,
            // <MouseXMode>) carry neither slot and are not actions at all.
            if (primary is null && secondary is null)
            {
                continue;
            }

            elements.Add(new BindingElement(name, primary, secondary));
        }

        return BindingsParseResult.Ok(new BindingsFile(presetName, majorVersion, minorVersion, elements));
    }

    private static BindingSlot? ParseSlot(XElement? slotElement)
    {
        if (slotElement is null)
        {
            return null;
        }

        var device = (string?)slotElement.Attribute("Device") ?? "";
        var key = (string?)slotElement.Attribute("Key") ?? "";
        var modifiers = slotElement.Elements("Modifier")
            .Select(m => new BoundKey((string?)m.Attribute("Device") ?? "", (string?)m.Attribute("Key") ?? ""))
            .ToList();

        return new BindingSlot(device, key, modifiers);
    }

    private static int? ParseIntAttribute(XElement element, string attributeName)
    {
        var raw = (string?)element.Attribute(attributeName);
        return raw is not null && int.TryParse(raw, out var value) ? value : null;
    }
}
