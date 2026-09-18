namespace LunaPanel.Core.Theme;

/// <summary>
/// The built-in, fixed palette offered by the settings gear's "completely
/// override colours" control (colour chain step 3, <c>ref/docs/theme.md</c>)
/// - a manual override that is <b>not derived from EDHM at all</b>, so it
/// works regardless of whether EDHM is installed, discoverable, or
/// resolvable. Every swatch is chosen to be legible on a near-black ground
/// at arm's length in a dark room: bright, saturated HUD colours, not muted
/// or mid-tone web colours. Judgement calls, not measurements - same
/// standing as <c>HudThemeResolver</c>'s ramp factors.
/// </summary>
public static class HudPalette
{
    public sealed record Swatch(string Name, HudColor Color);

    public static readonly IReadOnlyList<Swatch> Swatches = new[]
    {
        new Swatch("Elite Orange", HudThemeDefaults.StockOrange),
        new Swatch("Amber", new HudColor(0xFF, 0xB0, 0x00)),
        new Swatch("Yellow", new HudColor(0xFF, 0xEA, 0x00)),
        new Swatch("Lime", new HudColor(0xB6, 0xFF, 0x00)),
        new Swatch("Green", new HudColor(0x00, 0xFF, 0x66)),
        new Swatch("Mint", new HudColor(0x00, 0xFF, 0xC2)),
        new Swatch("Cyan", new HudColor(0x00, 0xFF, 0xFF)),
        new Swatch("Azure", new HudColor(0x00, 0xB8, 0xFF)),
        new Swatch("Blue", new HudColor(0x33, 0x78, 0xFF)),
        new Swatch("Violet", new HudColor(0x7A, 0x4D, 0xFF)),
        new Swatch("Purple", new HudColor(0xB8, 0x33, 0xFF)),
        new Swatch("Magenta", new HudColor(0xFF, 0x33, 0xCC)),
        new Swatch("Pink", new HudColor(0xFF, 0x6E, 0xC7)),
        new Swatch("Red", new HudColor(0xFF, 0x33, 0x33)),
        new Swatch("White", new HudColor(0xFF, 0xFF, 0xFF)),
        new Swatch("Light Grey", new HudColor(0xCC, 0xCC, 0xCC)),
    };

    /// <summary>
    /// A second, darker palette offered ONLY for the Background role
    /// (<c>ref/docs/theme.md</c>'s "Known limit, not fixed here: every
    /// offered background is a bright one"). <see cref="Swatches"/> above is
    /// deliberately all bright, saturated colour chosen to be legible ON a
    /// near-black ground - none of that is a reasonable background itself,
    /// which left a commander wanting a genuine dark background (e.g. a deep
    /// navy instead of near-black) with nothing between "one of my HUD
    /// colours" and "one of sixteen bright colours". These are near-black
    /// through dark grey/navy/teal/wine tones, picked to sit near the same
    /// tone as this page's own hand-picked <c>--lp-ground</c> fallback
    /// (<c>#07090c</c>) rather than arbitrary dark web colours - judgement
    /// calls, same standing as <see cref="Swatches"/>'s own.
    /// </summary>
    public static readonly IReadOnlyList<Swatch> DarkSwatches = new[]
    {
        new Swatch("Near Black", new HudColor(0x0A, 0x0A, 0x0A)),
        new Swatch("Onyx", new HudColor(0x12, 0x12, 0x12)),
        new Swatch("Charcoal", new HudColor(0x1E, 0x1E, 0x1E)),
        new Swatch("Gunmetal", new HudColor(0x2B, 0x2F, 0x36)),
        new Swatch("Slate Grey", new HudColor(0x36, 0x3B, 0x42)),
        new Swatch("Dark Navy", new HudColor(0x0D, 0x1B, 0x2A)),
        new Swatch("Midnight Blue", new HudColor(0x10, 0x13, 0x1F)),
        new Swatch("Deep Teal", new HudColor(0x0B, 0x20, 0x27)),
        new Swatch("Dark Forest", new HudColor(0x0D, 0x1F, 0x13)),
        new Swatch("Dark Wine", new HudColor(0x24, 0x08, 0x0D)),
    };
}
