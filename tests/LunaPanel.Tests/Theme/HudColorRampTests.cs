using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Pins <see cref="HudColorRamp"/>'s three derivation operations directly,
/// against literal values computed independently (not by calling the
/// production function a second time - see the design guidance against
/// identity assertions).
/// </summary>
public class HudColorRampTests
{
    [Fact]
    public void Lighten_HalfwayToWhite_MatchesIndependentlyComputedBytes()
    {
        // (100,150,200) lightened 50% toward white:
        // R: 100 + (255-100)*0.5 = 177.5 -> 178 (round-half-away-from-zero)
        // G: 150 + (255-150)*0.5 = 202.5 -> 203
        // B: 200 + (255-200)*0.5 = 227.5 -> 228
        var result = HudColorRamp.Lighten(new HudColor(100, 150, 200), 0.5);
        Assert.Equal(new HudColor(178, 203, 228), result);
    }

    [Fact]
    public void Darken_Half_MatchesIndependentlyComputedBytes()
    {
        // (100,150,200) darkened by 50%: exact halves, no rounding ambiguity.
        var result = HudColorRamp.Darken(new HudColor(100, 150, 200), 0.5);
        Assert.Equal(new HudColor(50, 75, 100), result);
    }

    [Fact]
    public void TintNearBlack_KeepsOnlyGivenFraction()
    {
        // (200,100,50) kept at 10%: exact tenths, no rounding ambiguity.
        var result = HudColorRamp.TintNearBlack(new HudColor(200, 100, 50), 0.1);
        Assert.Equal(new HudColor(20, 10, 5), result);
    }

    [Fact]
    public void Lighten_ZeroFraction_ReturnsColorUnchanged()
    {
        var color = new HudColor(12, 34, 56);
        Assert.Equal(color, HudColorRamp.Lighten(color, 0.0));
    }

    [Fact]
    public void Darken_ZeroFraction_ReturnsColorUnchanged()
    {
        var color = new HudColor(12, 34, 56);
        Assert.Equal(color, HudColorRamp.Darken(color, 0.0));
    }
}
