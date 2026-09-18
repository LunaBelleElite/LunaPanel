using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins "Option A: Windows 11 dark"'s exact palette (2026-09-07) - the user
/// chose this from three mockups, so these hex values are a decision to
/// hold in place, not an implementation detail to re-derive from a
/// screenshot.
/// </summary>
public class TrayThemeTests
{
    [Fact]
    public void ColorFor_Found_IsTheGreenConstant()
    {
        Assert.Equal("#6CCB6C", TrayTheme.FoundColor);
        Assert.Equal(TrayTheme.FoundColor, TrayTheme.ColorFor(StatusRowState.Found));
    }

    /// <summary>
    /// Not just "amber" - specifically never the found colour, and never a
    /// red: a missing EDHM install is not an error, per the spec.
    /// </summary>
    [Fact]
    public void ColorFor_NotFound_IsTheAmberConstant_NeverFoundsGreen()
    {
        Assert.Equal("#E0A03A", TrayTheme.NotFoundColor);
        Assert.Equal(TrayTheme.NotFoundColor, TrayTheme.ColorFor(StatusRowState.NotFound));
        Assert.NotEqual(TrayTheme.FoundColor, TrayTheme.ColorFor(StatusRowState.NotFound));
    }

    [Fact]
    public void ColorFor_NoState_IsThePrimaryTextColor()
    {
        Assert.Equal(TrayTheme.PrimaryText, TrayTheme.ColorFor(null));
    }
}
