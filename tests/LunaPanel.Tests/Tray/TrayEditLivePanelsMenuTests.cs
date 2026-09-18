using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// The tray's "Edit Live Panels" item (<c>ref/docs/pairing-and-devices.md</c>):
/// a way in to the Devices settings pane's existing per-device "edit a device
/// live from the PC" buttons. The same split
/// <c>TrayMacroTimingMenuTests</c>/<c>TrayTransferMenuTests</c> make - the
/// seam driven directly, the WinForms half source-scanned, because
/// <c>LunaPanel.Tray</c> is <c>net10.0-windows</c> and this assembly does not
/// reference it.
///
/// <b>The source scans in this file are PINS, not driven behaviour.</b>
/// Nothing here proves a menu item appears on a real tray icon, or that
/// clicking it opens a browser; only that the line adding it is in the file,
/// and that it takes its label and its URL from the seam rather than from a
/// second copy of either.
/// </summary>
public class TrayEditLivePanelsMenuTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LunaPanel.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing LunaPanel.sln) above {AppContext.BaseDirectory}.");
    }

    private static string ReadTraySource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", "TrayApplicationContext.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The loopback address, the host port and the fragment that lands on the
    /// Devices pane. Asserted as the whole literal URL: each part is
    /// separately wrong in a way nobody would notice from a screenshot.
    /// </summary>
    [Fact]
    public void TheUrl_IsTheLoopbackHostPort_OpenedStraightOntoTheDevicesPane()
    {
        Assert.Equal("http://127.0.0.1:51824/#devices", TrayEditLivePanels.Url(51824));
    }

    /// <summary>
    /// Never the LAN address: that one is reachable from every device on the
    /// network and grants nothing, so a menu item pointing at it would open a
    /// page showing the commander a pairing screen.
    /// </summary>
    [Fact]
    public void TheUrl_IsNeverAnythingButLoopback()
    {
        Assert.StartsWith("http://127.0.0.1:", TrayEditLivePanels.Url(51824), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUrl_IsNullWhenNoHostListenerWasBound()
    {
        Assert.Null(TrayEditLivePanels.Url(0));
    }

    /// <summary>
    /// All four host-surface tray items open four different panes. One test
    /// rather than four pinning one fragment each, because the claim is that
    /// they DIFFER - a copy-paste that left this one on <c>#macros</c> would
    /// satisfy any single-sided assertion, and would land the commander on
    /// the macro builder every time they went looking for Devices.
    /// </summary>
    [Fact]
    public void TheFourTrayItems_OpenFourDifferentPanes()
    {
        Assert.NotEqual(TrayMacroBuilder.Url(51824), TrayEditLivePanels.Url(51824));
        Assert.NotEqual(TrayTransfer.Url(51824), TrayEditLivePanels.Url(51824));
        Assert.NotEqual(TrayMacroTiming.Url(51824), TrayEditLivePanels.Url(51824));
        Assert.EndsWith("#devices", TrayEditLivePanels.Url(51824), StringComparison.Ordinal);
        Assert.EndsWith("#macros", TrayMacroBuilder.Url(51824), StringComparison.Ordinal);
        Assert.EndsWith("#transfer", TrayTransfer.Url(51824), StringComparison.Ordinal);
        Assert.EndsWith("#timing", TrayMacroTiming.Url(51824), StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserted as a literal. A commander looking for the per-device live
    /// edit buttons is looking for a menu item that names the destination.
    /// </summary>
    [Fact]
    public void TheMenuLabel_SaysWhatItDoes()
    {
        Assert.Equal("Edit Live Panels", TrayEditLivePanels.MenuLabel);
    }

    /// <summary>
    /// The Status window offers the same item, from the same seam and the
    /// same dispatch - <i>"I need buttons on the status window that match
    /// the items put on the tray"</i> (2026-09-10). A menu item that never
    /// grew its button is exactly the drift this pins against.
    /// </summary>
    [Fact]
    public void TheStatusWindow_CarriesTheSameItem()
    {
        Assert.Contains(
            TrayStatusWindowActions.ButtonsFor(51824),
            b => b.Action == TrayAction.EditLivePanels);
    }

    [Fact]
    public void TheTrayMenu_CarriesTheItem_LabelledFromTheSeam()
    {
        Assert.Contains(
            "menu.Items.Add(TrayEditLivePanels.MenuLabel, null, (_, _) => Dispatch(TrayAction.EditLivePanels))",
            ReadTraySource(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheTray_TakesTheUrlFromTheSeam()
    {
        Assert.Contains("TrayEditLivePanels.Url(options.HostAccessPort)", ReadTraySource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Left off the menu entirely when no host listener was bound, rather
    /// than added and pointed at nothing - the same condition the other
    /// three host-surface items are gated on.
    /// </summary>
    [Fact]
    public void TheItem_IsGatedOnAHostListenerExisting()
    {
        Assert.Contains("if (_editLivePanelsUrl is not null)", ReadTraySource(), StringComparison.Ordinal);
    }
}
