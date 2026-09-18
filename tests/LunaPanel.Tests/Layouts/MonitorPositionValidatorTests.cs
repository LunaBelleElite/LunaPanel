using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="MonitorPositionValidator.IsOnLiveScreen"/> - pure function,
/// no display needed. The disconnected-monitor case (a saved point outside
/// every current screen) is the actual bug this whole feature exists to fix,
/// so it gets its own explicit case rather than being inferred from the
/// others.
/// </summary>
public class MonitorPositionValidatorTests
{
    private static readonly (int Left, int Top, int Width, int Height) PrimaryScreen = (0, 0, 1920, 1080);
    private static readonly (int Left, int Top, int Width, int Height) SecondScreen = (1920, 0, 1920, 1080);

    [Fact]
    public void PointInsideSingleScreen_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen };

        Assert.True(MonitorPositionValidator.IsOnLiveScreen(100, 100, screens));
    }

    [Fact]
    public void PointInsideSecondOfTwoScreens_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen, SecondScreen };

        Assert.True(MonitorPositionValidator.IsOnLiveScreen(2500, 200, screens));
    }

    [Fact]
    public void PointOutsideEveryScreen_ReturnsFalse()
    {
        var screens = new[] { PrimaryScreen };

        Assert.False(MonitorPositionValidator.IsOnLiveScreen(3000, 3000, screens));
    }

    [Fact]
    public void PointExactlyOnAScreenEdge_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen };

        Assert.True(MonitorPositionValidator.IsOnLiveScreen(1920, 1080, screens));
    }

    [Fact]
    public void EmptyScreensList_ReturnsFalse()
    {
        Assert.False(MonitorPositionValidator.IsOnLiveScreen(0, 0, Array.Empty<(int, int, int, int)>()));
    }
}
