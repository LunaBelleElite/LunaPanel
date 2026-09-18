using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins the tray status window's "closing minimises rather than quits"
/// behaviour (<c>ref/docs/pairing-and-devices.md</c>'s "What this means for
/// the tray") as a pure decision, independent of any WinForms type - see
/// <see cref="WindowCloseTrigger"/>'s own remarks for why.
/// </summary>
public class TrayCloseDecisionTests
{
    [Fact]
    public void UserClickedClose_Minimizes_DoesNotClose()
    {
        Assert.True(TrayCloseDecision.ShouldMinimizeInsteadOfClosing(WindowCloseTrigger.UserClickedClose));
    }

    [Fact]
    public void ApplicationExiting_ClosesForReal_DoesNotMinimize()
    {
        Assert.False(TrayCloseDecision.ShouldMinimizeInsteadOfClosing(WindowCloseTrigger.ApplicationExiting));
    }
}
