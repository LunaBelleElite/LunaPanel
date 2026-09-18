using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins that the tray auto-shows the pairing code only when the pairing
/// window is already open at startup (first run) - see
/// <see cref="TrayStartupBehavior"/>'s own remarks for why a WinExe app with
/// no console needs this.
/// </summary>
public class TrayStartupBehaviorTests
{
    [Fact]
    public void PairingWindowOpenAtStartup_ShowsPairingCode()
    {
        Assert.True(TrayStartupBehavior.ShouldShowPairingCodeOnStartup(isPairingWindowOpenAtStartup: true));
    }

    [Fact]
    public void PairingWindowClosedAtStartup_DoesNotShowPairingCode()
    {
        Assert.False(TrayStartupBehavior.ShouldShowPairingCodeOnStartup(isPairingWindowOpenAtStartup: false));
    }
}
