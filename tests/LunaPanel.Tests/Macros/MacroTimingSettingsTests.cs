using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// <see cref="MacroTimingSettings"/>'s own pure logic - the hard refusal
/// boundary (<see cref="MacroTimingSettings.IsValid"/>) and the shipped
/// defaults it is built from. Deliberately distinct from the softer,
/// allowed-with-a-warning measured minimum
/// (<see cref="MacroTimingDefaults.MeasuredMinimumHoldDuration"/>/
/// <see cref="MacroTimingDefaults.MeasuredMinimumInterPressGap"/>), which
/// this type does not enforce at all.
/// </summary>
public class MacroTimingSettingsTests
{
    [Fact]
    public void Default_MatchesShippedMacroTimingDefaults_Exactly()
    {
        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, MacroTimingSettings.Default.HoldDuration);
        Assert.Equal(MacroTimingDefaults.InterPressGap, MacroTimingSettings.Default.InterPressGap);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(150, 100)]
    [InlineData(MacroTimingSettings.MaxMs, MacroTimingSettings.MaxMs)]
    public void IsValid_WithinBounds_ReturnsTrue(int holdMs, int gapMs)
    {
        Assert.True(MacroTimingSettings.IsValid(holdMs, gapMs));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(150, 0)]
    [InlineData(150, -5)]
    [InlineData(MacroTimingSettings.MaxMs + 1, 100)]
    [InlineData(150, MacroTimingSettings.MaxMs + 1)]
    public void IsValid_OutOfBounds_ReturnsFalse(int holdMs, int gapMs)
    {
        Assert.False(MacroTimingSettings.IsValid(holdMs, gapMs));
    }

    [Fact]
    public void IsValid_BelowMeasuredMinimum_ButAboveHardFloor_StillReturnsTrue()
    {
        // The soft, allowed-with-a-warning case (ref/docs/macro-timing.md's
        // "the cliff") - a value below the measured minimum is NOT refused
        // by this type; only MinMs/MaxMs are hard boundaries.
        Assert.True(MacroTimingSettings.IsValid(1, 1));
        Assert.True((int)MacroTimingDefaults.MeasuredMinimumHoldDuration.TotalMilliseconds > 1);
    }
}
