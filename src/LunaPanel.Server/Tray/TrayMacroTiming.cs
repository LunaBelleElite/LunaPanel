using LunaPanel.Server.Http;

namespace LunaPanel.Server.Tray;

/// <summary>
/// The tray's "Macro timing" item: its label, and the URL it opens.
///
/// The same seam <see cref="TrayMacroBuilder"/> and <see cref="TrayTransfer"/>
/// are, for the same reason. The commander asked for this one directly, in
/// their notes on the settings sheet's Timing pane: <i>"I would like to move
/// this to a server side adjustment area if possible."</i>
///
/// <b>What that ask needed, and what it did not.</b> The values were already
/// machine-wide rather than per device (<c>ref/docs/macro-timing.md</c>'s
/// "Scope: the PC, not the device"), and the Timing pane already renders on
/// the host's own page - the loopback-only surface
/// (<see cref="HostRequest"/>) is the same client the tablets get. So the
/// missing piece was never a second copy of the pane: it was a way in. The
/// address is one nobody would guess, and until this item existed the only
/// route to it was to already know it.
///
/// <b>The device's own Timing pane stays exactly as it was</b>, deliberately.
/// The commander asked for a server-side area, not for the tablet's to be
/// taken away, and <c>/api/macro-timing</c> is correspondingly NOT in
/// <see cref="HostOnlyRoutes"/> under either method - see
/// <c>ServerHostBuilderTests.MacroTiming_IsStillReachableFromAPairedDevice_NotHostOnly</c>,
/// which pins that on purpose against a later tidy-up.
/// </summary>
public static class TrayMacroTiming
{
    /// <summary>
    /// Names the thing rather than the pane it opens ("Timing" alone would
    /// read, on a menu sitting beside "Devices" and "Status", like something
    /// about the clock). A commander arrives here having just watched a macro
    /// miss keystrokes, and this is the only item on the menu carrying the
    /// word.
    /// </summary>
    public const string MenuLabel = "Macro timing";

    /// <summary>
    /// The page, opened straight onto its Timing pane.
    /// <see langword="null"/> when no host listener was bound at all - the
    /// item is left off the menu rather than opening an address that answers
    /// nothing, exactly as <see cref="TrayMacroBuilder.Url"/> and
    /// <see cref="TrayTransfer.Url"/> do.
    /// </summary>
    public static string? Url(int hostAccessPort) => HostRequest.LoopbackUrl(hostAccessPort, "#timing");
}
