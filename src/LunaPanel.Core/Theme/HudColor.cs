using System.Globalization;

namespace LunaPanel.Core.Theme;

/// <summary>
/// A resolved, display-ready RGB colour - always already decoded to plain
/// 8-bit sRGB display bytes. Nothing downstream of this type ever needs to
/// know whether the original source was an EDHM linear-light float, a
/// signed ARGB int, or a Frontier colour-matrix multiply; by the time a
/// <see cref="HudColor"/> exists, all of that is behind it.
/// </summary>
public readonly record struct HudColor(byte R, byte G, byte B)
{
    /// <summary>Lowercase <c>#rrggbb</c> form, the shape the CSS renderer emits.</summary>
    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";

    /// <summary>
    /// Parses a <c>#rrggbb</c> or bare <c>rrggbb</c> hex string (case
    /// insensitive). Never throws - a manual colour override arrives as
    /// untrusted client input (<see cref="ThemeOverrideJson"/>, the settings
    /// gear's <c>POST /api/theme</c> body), so the whole point of this
    /// method is that a bad string is just a <see langword="false"/> return,
    /// not a caught exception.
    /// </summary>
    public static bool TryParseHex(string? hex, out HudColor color)
    {
        color = default;
        if (hex is null)
        {
            return false;
        }

        var span = hex.AsSpan().Trim();
        if (span.Length == 7 && span[0] == '#')
        {
            span = span[1..];
        }
        else if (span.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = new HudColor(r, g, b);
        return true;
    }
}
