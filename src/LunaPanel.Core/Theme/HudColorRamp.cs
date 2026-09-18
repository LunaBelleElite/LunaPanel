namespace LunaPanel.Core.Theme;

/// <summary>
/// Brightness-ramp derivation used whenever a resolver has only one
/// representative HUD colour (a matrix step, or an EDHM title that didn't
/// resolve) and needs to fill in a role that wants a distinctly lighter,
/// darker, or near-black variant of it. Deliberately simple per-channel
/// linear interpolation - see <c>ref/docs/theme.md</c> for why these three
/// operations (not a HSL lighten/darken) were chosen.
/// </summary>
public static class HudColorRamp
{
    /// <summary>Blends every channel a fraction <paramref name="t"/> of the way toward white.</summary>
    public static HudColor Lighten(HudColor color, double t) => new(
        Blend(color.R, t),
        Blend(color.G, t),
        Blend(color.B, t));

    /// <summary>Scales every channel down by a fraction <paramref name="t"/> toward black.</summary>
    public static HudColor Darken(HudColor color, double t) => new(
        Scale(color.R, 1.0 - t),
        Scale(color.G, 1.0 - t),
        Scale(color.B, 1.0 - t));

    /// <summary>Scales every channel to exactly <paramref name="keepFraction"/> of its value - the near-black ground tint.</summary>
    public static HudColor TintNearBlack(HudColor color, double keepFraction) => new(
        Scale(color.R, keepFraction),
        Scale(color.G, keepFraction),
        Scale(color.B, keepFraction));

    private static byte Blend(byte from, double t) =>
        (byte)Math.Round(from + ((255 - from) * t), MidpointRounding.AwayFromZero);

    private static byte Scale(byte value, double factor) =>
        (byte)Math.Round(value * factor, MidpointRounding.AwayFromZero);
}
