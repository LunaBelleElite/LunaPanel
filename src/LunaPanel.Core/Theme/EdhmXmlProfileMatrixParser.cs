namespace LunaPanel.Core.Theme;

/// <summary>
/// Parses EDHM-UI's own <c>XML-Profile.ini</c> - a 3x3 HUD colour matrix
/// stored the same <c>[Constants]</c> way as everything else EDHM writes
/// (<c>x150</c>/<c>y150</c>/<c>z150</c> = the red row, <c>151</c> the green
/// row, <c>152</c> the blue row). This is the step-2 fallback used when
/// per-element colours from <c>ThemeSettings.json</c> aren't usable - see
/// <c>ref/docs/theme.md</c> for the full resolution chain.
/// </summary>
public static class EdhmXmlProfileMatrixParser
{
    /// <summary>
    /// Returns the parsed matrix, or <see langword="null"/> if any of the
    /// nine required keys is missing or not numeric - never throws.
    /// </summary>
    public static ColorMatrix? Parse(string iniContent)
    {
        var constants = IniConstants.Parse(iniContent);

        if (!TryGetRow(constants, "x150", "y150", "z150", out var red) ||
            !TryGetRow(constants, "x151", "y151", "z151", out var green) ||
            !TryGetRow(constants, "x152", "y152", "z152", out var blue))
        {
            return null;
        }

        return new ColorMatrix(red, green, blue);
    }

    private static bool TryGetRow(IReadOnlyDictionary<string, double> constants, string keyA, string keyB, string keyC, out double[] row)
    {
        row = new double[3];
        return constants.TryGetValue(keyA, out row[0]) &&
               constants.TryGetValue(keyB, out row[1]) &&
               constants.TryGetValue(keyC, out row[2]);
    }
}
