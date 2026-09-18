using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Drives <see cref="ThemeContrast"/> - the non-blocking "these two
/// colours are nearly the same" check the settings gear's colour pane
/// shows a warning from once Background became choosable (2026-09-10,
/// <c>ref/docs/theme.md</c>'s contrast section).
///
/// <b>This never refuses a colour and there is deliberately no test that
/// it does.</b> The commander's judgement on their own panel is final; a
/// warning is not a block. What is pinned here is only which roles get
/// named in the warning.
/// </summary>
public class ThemeContrastTests
{
    private static HudTheme ThemeWith(HudColor ground, HudColor frame, HudColor text, HudColor lit) => new(
        ground,
        frame,
        text,
        new HudColor(0, 0, 0),
        lit,
        new HudColor(0, 0, 0),
        HudThemeSource.Override,
        Array.Empty<string>());

    [Fact]
    public void ContrastRatio_BlackAgainstWhite_IsTheFullTwentyOneToOne()
    {
        var ratio = ThemeContrast.ContrastRatio(new HudColor(0, 0, 0), new HudColor(255, 255, 255));

        Assert.Equal(21.0, ratio, 2);
    }

    [Fact]
    public void ContrastRatio_AColourAgainstItself_IsExactlyOne()
    {
        var ratio = ThemeContrast.ContrastRatio(new HudColor(0x33, 0x78, 0xFF), new HudColor(0x33, 0x78, 0xFF));

        Assert.Equal(1.0, ratio, 6);
    }

    [Fact]
    public void ContrastRatio_IsSymmetric_RegardlessOfWhichColourIsLighter()
    {
        var dark = new HudColor(0x14, 0x0C, 0x0E);
        var light = new HudColor(0x95, 0xFB, 0xFF);

        Assert.Equal(ThemeContrast.ContrastRatio(dark, light), ThemeContrast.ContrastRatio(light, dark), 6);
    }

    [Fact]
    public void RelativeLuminance_TheTwoEndpoints_AreZeroAndOne()
    {
        Assert.Equal(0.0, ThemeContrast.RelativeLuminance(new HudColor(0, 0, 0)), 6);
        Assert.Equal(1.0, ThemeContrast.RelativeLuminance(new HudColor(255, 255, 255)), 6);
    }

    [Fact]
    public void RelativeLuminance_PureGreen_CarriesFarMoreWeightThanPureBlue()
    {
        // The WCAG channel weights (0.2126/0.7152/0.0722), asserted as
        // literals rather than by re-running the formula.
        Assert.Equal(0.7152, ThemeContrast.RelativeLuminance(new HudColor(0, 255, 0)), 4);
        Assert.Equal(0.0722, ThemeContrast.RelativeLuminance(new HudColor(0, 0, 255)), 4);
        Assert.Equal(0.2126, ThemeContrast.RelativeLuminance(new HudColor(255, 0, 0)), 4);
    }

    [Fact]
    public void LowContrastRoles_LitEqualToTheBackground_NamesLitOnly()
    {
        // The exact collision that cost an evening: a Lit colour equal to
        // the ground makes a toggled-on button invisible.
        var ground = new HudColor(0x14, 0x0C, 0x0E);
        var theme = ThemeWith(ground, new HudColor(0x95, 0xFB, 0xFF), new HudColor(0xFE, 0x91, 0xB3), ground);

        var roles = ThemeContrast.LowContrastRoles(theme);

        Assert.Equal(new[] { "Lit" }, roles);
    }

    [Fact]
    public void LowContrastRoles_ThreeReadableRoles_NamesNothing()
    {
        var theme = ThemeWith(
            new HudColor(0x14, 0x0C, 0x0E),
            new HudColor(0x95, 0xFB, 0xFF),
            new HudColor(0xFE, 0x91, 0xB3),
            new HudColor(0xFF, 0xFF, 0xFF));

        Assert.Empty(ThemeContrast.LowContrastRoles(theme));
    }

    [Fact]
    public void LowContrastRoles_TwoRolesColliding_NamesBoth_InPaneOrder()
    {
        // Border and Lit both lost against a bright background; Text
        // survives. The order is the settings pane's own row order, not
        // discovery order - so a caller can concatenate the list straight
        // into a sentence.
        var ground = new HudColor(0xFF, 0xFF, 0xFF);
        var theme = ThemeWith(ground, new HudColor(0xFE, 0xFE, 0xFE), new HudColor(0x00, 0x00, 0x00), new HudColor(0xFF, 0xFF, 0xFF));

        var roles = ThemeContrast.LowContrastRoles(theme);

        Assert.Equal(new[] { "Border", "Lit" }, roles);
    }

    [Fact]
    public void LowContrastRoles_TextOnly_NamesTextOnly()
    {
        // Proves the three checks are separately wired rather than one
        // check standing in for all three - a router mutated to always
        // report "Lit" would pass the Lit case above and fail here.
        var ground = new HudColor(0x00, 0x00, 0x00);
        var theme = ThemeWith(ground, new HudColor(0xFF, 0xFF, 0xFF), new HudColor(0x0A, 0x0A, 0x0A), new HudColor(0xFF, 0xFF, 0xFF));

        var roles = ThemeContrast.LowContrastRoles(theme);

        Assert.Equal(new[] { "Text" }, roles);
    }

    [Fact]
    public void LowContrastRoles_JustEitherSideOfTheThreshold_IsReportedOnlyBelowIt()
    {
        // The tightest straddle there is: against black, #2c2c2c is
        // 1.5037:1 and #2b2b2b is 1.4832:1 - one byte apart, one either
        // side of the 1.5 judgement call ref/docs/theme.md documents. Pins
        // that the boundary is where it says it is, and that no neighbour
        // value quietly moved it.
        var ground = new HudColor(0x00, 0x00, 0x00);

        var justAbove = ThemeWith(ground, new HudColor(0x2C, 0x2C, 0x2C), new HudColor(0xFF, 0xFF, 0xFF), new HudColor(0xFF, 0xFF, 0xFF));
        var justBelow = ThemeWith(ground, new HudColor(0x2B, 0x2B, 0x2B), new HudColor(0xFF, 0xFF, 0xFF), new HudColor(0xFF, 0xFF, 0xFF));

        Assert.Empty(ThemeContrast.LowContrastRoles(justAbove));
        Assert.Equal(new[] { "Border" }, ThemeContrast.LowContrastRoles(justBelow));
    }
}
