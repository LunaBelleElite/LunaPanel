namespace LunaPanel.Core.Tray;

/// <summary>
/// Whether the tray's Status window minimises back to the tray on close
/// (the shipped default, unchanged behaviour) or LunaPanel instead always
/// shows a taskbar window and closing it quits the program for real
/// (<c>AboutForm</c>'s "Minimize to system tray" checkbox). Mirrors
/// <c>LunaPanel.Core.Network.PortSettings</c>'s exact shape - server-wide,
/// one file, no device id.
/// </summary>
public sealed record TrayBehaviorSettings(bool MinimizeToTrayEnabled)
{
    public static readonly TrayBehaviorSettings Default = new(true);
}
