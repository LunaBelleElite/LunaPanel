using LunaPanel.Core.Macros;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="MacroTimingEndpoint"/>'s pure logic directly - the
/// settings gear's Timing pane, kept free of any ASP.NET type, same
/// discipline as <see cref="ThemeEndpoint"/>.
/// </summary>
public class MacroTimingEndpointTests
{
    [Fact]
    public void BuildResponse_ReportsCurrentValuesAndTheMeasuredMinimums()
    {
        var settings = new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180));

        var response = MacroTimingEndpoint.BuildResponse(settings);

        Assert.Equal(275, response.HoldMs);
        Assert.Equal(180, response.InterPressGapMs);
        // Read from the same constants the settings pane's warning check is
        // supposed to be driven by - never a hand-typed "100" here either.
        Assert.Equal((int)MacroTimingDefaults.MeasuredMinimumHoldDuration.TotalMilliseconds, response.HoldMinMs);
        Assert.Equal((int)MacroTimingDefaults.MeasuredMinimumInterPressGap.TotalMilliseconds, response.InterPressGapMinMs);
    }

    [Fact]
    public void TryParse_ValidRequest_Succeeds_AndProducesTheExactSettings()
    {
        var ok = MacroTimingEndpoint.TryParse(new MacroTimingEndpoint.Request(200, 120), out var settings, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromMilliseconds(200), settings.HoldDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(120), settings.InterPressGap);
    }

    [Fact]
    public void TryParse_NullRequest_Fails()
    {
        var ok = MacroTimingEndpoint.TryParse(null, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(150, 0)]
    [InlineData(-1, 100)]
    [InlineData(150, -1)]
    [InlineData(MacroTimingSettings.MaxMs + 1, 100)]
    [InlineData(150, MacroTimingSettings.MaxMs + 1)]
    public void TryParse_OutOfRangeEitherField_Fails_AndNamesNoInvalidSettingsToTheCaller(int holdMs, int gapMs)
    {
        var ok = MacroTimingEndpoint.TryParse(new MacroTimingEndpoint.Request(holdMs, gapMs), out var settings, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        // The out parameter still gets a value on refusal (out parameters
        // must be assigned), but it must never be the out-of-range request -
        // the whole point of refusing is that this value is never persisted.
        Assert.Equal(MacroTimingSettings.Default, settings);
    }

    [Fact]
    public void TryParse_BelowMeasuredMinimum_ButWithinHardBounds_Succeeds()
    {
        // The soft, allowed-with-a-warning case - refusing this would
        // contradict ref/docs/macro-timing.md's "the cliff": "allow it,
        // label it, and never let a commander arrive there without being
        // told." The warning itself is the client's job, not this method's.
        var ok = MacroTimingEndpoint.TryParse(new MacroTimingEndpoint.Request(1, 1), out var settings, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromMilliseconds(1), settings.HoldDuration);
    }
}
