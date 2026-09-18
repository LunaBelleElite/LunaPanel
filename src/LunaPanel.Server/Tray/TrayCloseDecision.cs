namespace LunaPanel.Server.Tray;

/// <summary>
/// Why a window is closing, expressed independently of
/// <c>System.Windows.Forms.CloseReason</c> so this decision stays testable
/// from <c>LunaPanel.Tests</c> without that project ever referencing
/// <c>LunaPanel.Tray</c> (a Windows-only, <c>net10.0-windows</c> project -
/// see <c>ref/docs/hosting.md</c>'s tray section). The WinForms layer maps
/// its own <c>FormClosingEventArgs.CloseReason</c> onto this enum at the one
/// call site that actually has one.
/// </summary>
public enum WindowCloseTrigger
{
    /// <summary>The user clicked the window's own close control (the X, Alt+F4, the system menu's Close).</summary>
    UserClickedClose,

    /// <summary>Anything else - the app itself is exiting (the Quit menu action), or Windows is shutting down.</summary>
    ApplicationExiting,
}

/// <summary>
/// The tray's status window minimises to the tray instead of closing
/// (<c>ref/docs/pairing-and-devices.md</c>'s "What this means for the
/// tray"; the user's own framing: "the ability to put the program in the
/// background and not take up a taskbar slot") - but only when the user
/// closed it themselves, AND the commander has left "Minimize to system
/// tray" turned on (<c>AboutForm</c>'s checkbox,
/// <c>LunaPanel.Core.Tray.TrayBehaviorSettings</c>, default on). When the
/// app is actually exiting (the Quit menu action, or Windows shutting
/// down), the window must be allowed to close for real, or the process
/// could never exit - regardless of the setting, since it only governs
/// what a user's own close click does.
/// </summary>
public static class TrayCloseDecision
{
    public static bool ShouldMinimizeInsteadOfClosing(WindowCloseTrigger trigger, bool minimizeToTrayEnabled) =>
        trigger == WindowCloseTrigger.UserClickedClose && minimizeToTrayEnabled;
}
