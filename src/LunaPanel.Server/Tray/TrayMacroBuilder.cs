using LunaPanel.Server.Http;

namespace LunaPanel.Server.Tray;

/// <summary>
/// The tray's "Build a macro" item: its label, and the URL it opens.
///
/// <b>Why the tray owns this at all.</b> The macro builder is on the PC now
/// (<c>ref/docs/macro-builder.md</c>) and the PC reaches it on a
/// loopback-only address a commander has no reason to know
/// (<see cref="HostRequest"/>). The commander asked for it to be findable
/// from the program itself - *"it needs an option for it built into the
/// server program LunaPanel so that it can be found easily"* - and this is
/// that: one menu item, the default browser, no address to remember and no
/// pairing code to type.
///
/// Deliberately a plain seam like <c>TrayStatusWindowActions</c>: the label
/// and the URL are decided here, where a test can read them, and
/// <c>LunaPanel.Tray.TrayApplicationContext</c> only adds a menu item and
/// starts a process - the same testable/untestable split
/// <c>ref/docs/hosting.md</c> describes for the rest of the tray.
/// </summary>
public static class TrayMacroBuilder
{
    public const string MenuLabel = "Build a macro";

    /// <summary>
    /// The page, opened straight onto its Macros pane. <see langword="null"/>
    /// when no host listener was bound at all, which is the one case where
    /// there is no address that would work - the item is left off the menu
    /// rather than opening a URL that answers nothing.
    /// </summary>
    public static string? Url(int hostAccessPort) => HostRequest.LoopbackUrl(hostAccessPort, "#macros");
}
