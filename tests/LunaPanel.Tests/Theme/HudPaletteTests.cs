using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins the built-in override palette's basic shape - roughly a dozen to
/// sixteen distinct, named, non-black/non-white-only swatches (the brief:
/// "roughly 12-16 colours spanning the spectrum"). Not a test of "is each
/// colour pretty" - that is a judgement call, not something a test can hold
/// to a claim (see <c>ref/docs/theme.md</c>).
/// </summary>
public class HudPaletteTests
{
    [Fact]
    public void Swatches_CountIsWithinTheSpecifiedRange()
    {
        Assert.InRange(HudPalette.Swatches.Count, 12, 16);
    }

    [Fact]
    public void Swatches_EveryNameIsNonEmptyAndUnique()
    {
        var names = HudPalette.Swatches.Select(s => s.Name).ToList();

        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Swatches_EveryColourIsUnique()
    {
        var colors = HudPalette.Swatches.Select(s => s.Color).ToList();

        Assert.Equal(colors.Count, colors.Distinct().Count());
    }

    [Fact]
    public void Swatches_IncludesEliteStockOrange()
    {
        Assert.Contains(HudPalette.Swatches, s => s.Color == HudThemeDefaults.StockOrange);
    }
}
