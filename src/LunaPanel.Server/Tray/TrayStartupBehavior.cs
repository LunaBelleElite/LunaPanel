namespace LunaPanel.Server.Tray;

/// <summary>
/// Whether the "Add a device" pairing-code window should show itself the
/// moment the tray starts, rather than waiting for a menu click.
///
/// True only when <see cref="LunaPanel.Core.Pairing.DeviceRegistry.IsPairingWindowOpen"/>
/// is already <see langword="true"/> at startup - which only happens on
/// first run, when no device is registered yet
/// (<c>ref/docs/pairing-and-devices.md</c>'s "first run is the exception").
///
/// This matters specifically because <c>LunaPanel.Tray</c> is a
/// <c>WinExe</c> app with no attached console: <see cref="LunaPanel.Server.Hosting.ServerHostBuilder"/>'s
/// own startup banner writes the first-run code through
/// <c>Console.WriteLine</c>, which has nowhere to be seen when launched this
/// way. Without this, that first-run code - the one pairing needs to work at
/// all - would be invisible in scrollback that, run this way, does not exist.
/// </summary>
public static class TrayStartupBehavior
{
    public static bool ShouldShowPairingCodeOnStartup(bool isPairingWindowOpenAtStartup) =>
        isPairingWindowOpenAtStartup;
}
