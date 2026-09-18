using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins the tray status window's "closing minimises rather than quits"
/// behaviour (<c>ref/docs/pairing-and-devices.md</c>'s "What this means for
/// the tray") as a pure decision, independent of any WinForms type - see
/// <see cref="WindowCloseTrigger"/>'s own remarks for why. Since the
/// "Minimize to system tray" setting was added, this is a combination of
/// two independent conditions - all four combinations are exercised so a
/// wrong routing in either the trigger or the setting is caught.
/// </summary>
public class TrayCloseDecisionTests
{
    [Fact]
    public void UserClickedClose_AndMinimizeEnabled_Minimizes()
    {
        Assert.True(TrayCloseDecision.ShouldMinimizeInsteadOfClosing(WindowCloseTrigger.UserClickedClose, minimizeToTrayEnabled: true));
    }

    [Fact]
    public void UserClickedClose_AndMinimizeDisabled_ClosesForReal()
    {
        Assert.False(TrayCloseDecision.ShouldMinimizeInsteadOfClosing(WindowCloseTrigger.UserClickedClose, minimizeToTrayEnabled: false));
    }

    [Fact]
    public void ApplicationExiting_AndMinimizeEnabled_ClosesForReal_DoesNotMinimize()
    {
        Assert.False(TrayCloseDecision.ShouldMinimizeInsteadOfClosing(WindowCloseTrigger.ApplicationExiting, minimizeToTrayEnabled: true));
    }

    [Fact]
    public void ApplicationExiting_AndMinimizeDisabled_ClosesForReal()
    {
        Assert.False(TrayCloseDecision.ShouldMinimizeInsteadOfClosing(WindowCloseTrigger.ApplicationExiting, minimizeToTrayEnabled: false));
    }
}
