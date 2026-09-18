namespace LunaPanel.Core.Theme;

/// <summary>
/// The two decode primitives EDHM-UI's own colour storage needs.
///
/// EDHM stores each colour component as a <b>linear-light</b> float in an
/// INI <c>[Constants]</c> section (e.g. <c>x77 = 0.6023</c>). Turning that
/// into the byte a screen actually displays means applying the standard
/// sRGB transfer curve, not just multiplying by 255 - a linear value is not
/// itself a display value.
///
/// Separately, <c>ThemeSettings.json</c> also stores each colour a second
/// way, as a signed 32-bit ARGB integer. That second encoding is a plain
/// bit-packed byte quad (standard ARGB order), nothing to do with the sRGB
/// curve above - it's decoded here only because it lives in the same file
/// format EDHM readers deal with, not because it shares any math with the
/// linear-light decode.
/// </summary>
public static class SrgbCodec
{
    /// <summary>
    /// Applies the sRGB transfer curve to a linear-light component and
    /// returns the display byte: <c>v &lt;= 0.0031308 ? 12.92*v : 1.055 *
    /// pow(v, 1/2.4) - 0.055</c>, clamped to [0,1] both before and after,
    /// then scaled to a byte with round-half-away-from-zero.
    ///
    /// Pinned against two points measured against a real EDHM-UI install:
    /// see this project's own <c>SrgbCodecTests</c>, and
    /// <c>tests/LunaPanel.Tests/FixtureIntegrityTests.cs</c>'s
    /// <c>SrgbEncode_ReferencePair_MatchesRealEdhmMeasurement</c>, which
    /// keeps the same two data points (<c>0.3005</c> -&gt; <c>0x95</c>,
    /// <c>0.9647</c> -&gt; <c>0xFB</c>) against its own local copy of this
    /// formula.
    /// </summary>
    public static byte DecodeLinearToByte(double linear)
    {
        var clampedLinear = Math.Clamp(linear, 0.0, 1.0);

        var encoded = clampedLinear <= 0.0031308
            ? 12.92 * clampedLinear
            : (1.055 * Math.Pow(clampedLinear, 1.0 / 2.4)) - 0.055;

        encoded = Math.Clamp(encoded, 0.0, 1.0);

        return (byte)Math.Round(encoded * 255.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Unpacks a signed 32-bit ARGB integer (as stored in <c>ThemeSettings.json</c>'s
    /// own <c>Value</c> field) into its four byte components. Standard bit
    /// packing: alpha in the top byte, down to blue in the bottom byte -
    /// the sign only matters for how .NET happens to print the number, not
    /// for the bits themselves, which is why masking with <c>0xFFFFFFFF</c>
    /// (via an unsigned reinterpret) recovers them correctly regardless of
    /// whether the stored int reads as negative.
    ///
    /// This is a cross-check only: EDHM's own stored <c>Value</c> can be
    /// stale relative to the live INI <c>[Constants]</c> value it was
    /// generated from, so a theme resolver must not treat this decode as
    /// authoritative - see <c>ref/docs/theme.md</c>.
    /// </summary>
    public static (byte A, byte R, byte G, byte B) DecodeArgbInt(int argb)
    {
        var bits = unchecked((uint)argb);
        var a = (byte)((bits >> 24) & 0xFF);
        var r = (byte)((bits >> 16) & 0xFF);
        var g = (byte)((bits >> 8) & 0xFF);
        var b = (byte)(bits & 0xFF);
        return (a, r, g, b);
    }
}
