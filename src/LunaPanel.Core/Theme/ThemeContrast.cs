namespace LunaPanel.Core.Theme;

/// <summary>
/// How far apart two resolved colours actually are to look at - used for
/// one thing only: naming the roles the settings gear's colour pane should
/// warn about once the commander has made one of them nearly the same as
/// the panel background (<c>ref/docs/theme.md</c>'s contrast section).
///
/// <b>This refuses nothing and blocks nothing.</b> The commander's
/// judgement on their own panel is final; every colour they pick is
/// applied exactly as picked, and this type is never consulted before a
/// save. A warning is not a block. The hazard it exists for is real and
/// has already happened once: a Lit colour close enough to the ground
/// makes a toggled-on button invisible, which reads as "the panel is
/// broken" rather than as "that colour was a mistake" - and with
/// Background choosable too, both sides of that collision can now move.
///
/// The maths is WCAG 2.x relative luminance and contrast ratio, with its
/// own constants (<c>0.03928</c>, <c>12.92</c>, <c>2.4</c>, channel
/// weights <c>0.2126/0.7152/0.0722</c>). Deliberately not shared with
/// <see cref="SrgbCodec"/>: that decodes EDHM's linear-light floats to
/// display bytes and only runs in that direction, while this needs the
/// inverse, and the two curves' constants are not identical anyway.
/// </summary>
public static class ThemeContrast
{
    /// <summary>
    /// Contrast ratios at or below this are reported by
    /// <see cref="LowContrastRoles"/>. A judgement call, not a standard:
    /// WCAG's own thresholds (4.5 for body text, 3 for large text) are
    /// about readable prose on a page, and would flag most of a HUD-themed
    /// panel as failing while telling the commander nothing they did not
    /// already choose on purpose. 1.5 is set where "I cannot see that at
    /// all" begins rather than where "that is hard to read for a long
    /// time" begins - against a black ground it starts warning below about
    /// <c>#2c2c2c</c>.
    /// </summary>
    public const double LowContrastRatio = 1.5;

    /// <summary>WCAG relative luminance: 0.0 for black, 1.0 for white.</summary>
    public static double RelativeLuminance(HudColor color) =>
        (0.2126 * ToLinear(color.R)) + (0.7152 * ToLinear(color.G)) + (0.0722 * ToLinear(color.B));

    /// <summary>
    /// WCAG contrast ratio between two colours - 1.0 for two identical
    /// colours, 21.0 for black against white. Symmetric: which colour is
    /// the lighter of the two is worked out here, not asked of the caller.
    /// </summary>
    public static double ContrastRatio(HudColor a, HudColor b)
    {
        var first = RelativeLuminance(a);
        var second = RelativeLuminance(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// The commander-facing names of whichever of the three foreground
    /// roles have come out too close to <see cref="HudTheme.Ground"/> to
    /// see - in the settings pane's own row order (Border, Text, Lit), so
    /// a caller can join the list straight into a sentence. Empty is the
    /// normal answer, and an empty list rather than <see langword="null"/>
    /// is the contract.
    ///
    /// Accent and Dim are deliberately not checked: they are derived, never
    /// commander-facing, and warning about a colour nobody can change would
    /// be noise the commander cannot act on.
    /// </summary>
    public static IReadOnlyList<string> LowContrastRoles(HudTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var roles = new List<string>(3);
        if (ContrastRatio(theme.Frame, theme.Ground) <= LowContrastRatio)
        {
            roles.Add("Border");
        }

        if (ContrastRatio(theme.Text, theme.Ground) <= LowContrastRatio)
        {
            roles.Add("Text");
        }

        if (ContrastRatio(theme.Lit, theme.Ground) <= LowContrastRatio)
        {
            roles.Add("Lit");
        }

        return roles;
    }

    private static double ToLinear(byte channel)
    {
        var v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
