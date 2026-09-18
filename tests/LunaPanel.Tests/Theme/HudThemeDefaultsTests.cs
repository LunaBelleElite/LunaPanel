using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins <see cref="HudThemeDefaults.ApplyMatrix(ColorMatrix)"/> - the
/// multiply that turns any of the three matrix-shaped resolution steps
/// into a single representative colour.
/// </summary>
public class HudThemeDefaultsTests
{
    [Fact]
    public void ApplyMatrix_Identity_ReproducesStockOrangeExactly()
    {
        var identity = new ColorMatrix(new[] { 1.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 });

        var result = HudThemeDefaults.ApplyMatrix(identity);

        Assert.Equal(HudThemeDefaults.StockOrange, result);
        Assert.Equal(new HudColor(0xFF, 0x80, 0x00), result);
    }

    [Fact]
    public void ApplyMatrix_CustomMatrix_MatchesIndependentlyComputedBytes()
    {
        // Same matrix as Fixtures/graphics/guicolour-custom.xml. Expected
        // bytes computed independently (Python, IEEE754 double arithmetic,
        // round-half-away-from-zero):
        //   R: 0.45*1 + 0.35*(128/255) + 0.20*0 = 0.625686... -> 160 = 0xA0
        //   G: 0.10*1 + 0.70*(128/255) + 0.20*0 = 0.451372... -> 115 = 0x73
        //   B: 0.05*1 + 0.15*(128/255) + 0.80*0 = 0.125294... ->  32 = 0x20
        var matrix = new ColorMatrix(new[] { 0.45, 0.35, 0.20 }, new[] { 0.10, 0.70, 0.20 }, new[] { 0.05, 0.15, 0.80 });

        var result = HudThemeDefaults.ApplyMatrix(matrix);

        Assert.Equal(new HudColor(0xA0, 0x73, 0x20), result);
    }

    [Fact]
    public void ApplyMatrix_RowWeightsSumAboveOne_ClampsRatherThanOverflowing()
    {
        // A row whose weights sum to > 1 against the native vector must
        // clamp to full brightness (255), not wrap or throw.
        var matrix = new ColorMatrix(new[] { 2.0, 0.0, 0.0 }, new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 });

        var result = HudThemeDefaults.ApplyMatrix(matrix);

        Assert.Equal((byte)0xFF, result.R);
    }
}
