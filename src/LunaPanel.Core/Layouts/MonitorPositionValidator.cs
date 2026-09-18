namespace LunaPanel.Core.Layouts;

/// <summary>
/// Whether a saved window position still falls on a monitor that actually
/// exists right now - deliberately framework-agnostic (plain ints, no
/// <c>System.Drawing</c>/WinForms dependency), the same split
/// <c>LunaPanel.Server.Hosting.PortResolution</c> and
/// <c>LunaPanel.Server.Http.HostRequest.ResolveAccessPort</c> already draw
/// between pure logic and the real environment that supplies its inputs
/// (<c>StatusForm</c>, the only place with WinForms <c>Screen</c> access,
/// converts <c>Screen.AllScreens</c> into the tuples this takes).
///
/// Checks only the window's top-left corner - enough to answer "does this
/// monitor still exist", the actual failure mode a laptop-plus-dock setup
/// produces when a monitor disappears while the window is hidden.
/// Deliberately does not try to guarantee the whole window is on-screen;
/// a partial-offscreen drag is a different problem, out of scope here.
/// </summary>
public static class MonitorPositionValidator
{
    public static bool IsOnLiveScreen(
        int x,
        int y,
        IReadOnlyList<(int Left, int Top, int Width, int Height)> screens)
    {
        foreach (var screen in screens)
        {
            if (x >= screen.Left && x <= screen.Left + screen.Width
                && y >= screen.Top && y <= screen.Top + screen.Height)
            {
                return true;
            }
        }

        return false;
    }
}
