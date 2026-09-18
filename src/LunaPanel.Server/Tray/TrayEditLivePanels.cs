using LunaPanel.Server.Http;

namespace LunaPanel.Server.Tray;

/// <summary>
/// The tray's "Edit Live Panels" item: its label, and the URL it opens.
///
/// The same seam <see cref="TrayMacroTiming"/> and <see cref="TrayTransfer"/>
/// are, for the same reason. The per-device "edit a device live from the PC"
/// buttons already live on the Devices settings pane
/// (<c>ref/docs/pairing-and-devices.md</c>); this item is only a way in - the
/// address is one nobody would guess, and until this item existed the only
/// route to it was to already know it.
/// </summary>
public static class TrayEditLivePanels
{
    /// <summary>
    /// Names the destination rather than the mechanism, matching how the
    /// other host-surface items are named.
    /// </summary>
    public const string MenuLabel = "Edit Live Panels";

    /// <summary>
    /// The page, opened straight onto its Devices pane.
    /// <see langword="null"/> when no host listener was bound at all - the
    /// item is left off the menu rather than opening an address that answers
    /// nothing, exactly as <see cref="TrayMacroTiming.Url"/> and
    /// <see cref="TrayTransfer.Url"/> do.
    /// </summary>
    public static string? Url(int hostAccessPort) => HostRequest.LoopbackUrl(hostAccessPort, "#devices");
}
