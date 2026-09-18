using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace LunaPanel.Core.Theme;

/// <summary>
/// Parses the <c>&lt;GUIColour&gt;&lt;Default&gt;</c> 3x3 matrix shared by
/// Elite's own <c>GraphicsConfigurationOverride.xml</c> (step 3 - a
/// player-set override, often absent or empty - see
/// <c>Fixtures/graphics/override-empty.xml</c>) and
/// <c>GraphicsConfiguration.xml</c> (step 4 - always present, the identity
/// matrix when the player hasn't touched HUD colours at all). Both files
/// share this exact shape, so one parser serves both steps.
/// </summary>
public static class GraphicsConfigMatrixParser
{
    /// <summary>
    /// Returns the parsed matrix, or <see langword="null"/> if the XML is
    /// malformed, has no <c>&lt;GUIColour&gt;&lt;Default&gt;</c> element at
    /// all (the common "override file exists but is empty" case), or any
    /// of the three rows isn't exactly three comma-separated numbers.
    /// Never throws.
    /// </summary>
    public static ColorMatrix? ParseGuiColourDefault(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        var defaultElement = document.Root?.Element("GUIColour")?.Element("Default");
        if (defaultElement is null)
        {
            return null;
        }

        var red = ParseRow(defaultElement.Element("MatrixRed")?.Value);
        var green = ParseRow(defaultElement.Element("MatrixGreen")?.Value);
        var blue = ParseRow(defaultElement.Element("MatrixBlue")?.Value);

        return red is null || green is null || blue is null
            ? null
            : new ColorMatrix(red, green, blue);
    }

    private static double[]? ParseRow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return null;
        }

        var row = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out row[i]))
            {
                return null;
            }
        }

        return row;
    }
}
