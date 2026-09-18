using LunaPanel.Server.Http;

namespace LunaPanel.Server.Tray;

/// <summary>
/// The tray's "Import or export" item: its label, and the URL it opens.
///
/// The same seam <see cref="TrayMacroBuilder"/> is, for the same reason and
/// deliberately not folded into it. The commander asked for two things -
/// <i>"a way to import or export macros from the windows server program"</i>
/// and <i>"a way to import or export profiles from the windows server
/// program"</i> - and both live behind a loopback-only address nobody would
/// guess (<see cref="HostRequest"/>), so the program that owns that address
/// is what has to offer them. One menu item, the default browser, no address
/// to remember.
///
/// <b>One item for both, not two.</b> A profile carries the macros its
/// buttons name (<c>ref/docs/transfer.md</c>), so the two asks are one pane
/// with two halves rather than two places to go and choose between.
/// </summary>
public static class TrayTransfer
{
    /// <summary>
    /// Spelled exactly as <see cref="HostOnlyRoutes.NotTheHostTransferAdvice"/>
    /// quotes it - a device refused a transfer route is sent here by name,
    /// and a refusal naming a menu item that does not exist is worse than
    /// one that names nothing.
    /// </summary>
    public const string MenuLabel = "Import or export";

    /// <summary>
    /// The page, opened straight onto its Import/export pane.
    /// <see langword="null"/> when no host listener was bound at all - the
    /// item is left off the menu rather than opening an address that answers
    /// nothing, exactly as <see cref="TrayMacroBuilder.Url"/> does.
    /// </summary>
    public static string? Url(int hostAccessPort) => HostRequest.LoopbackUrl(hostAccessPort, "#transfer");
}
