using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// The tray's "Import or export" item (<c>ref/docs/transfer.md</c>): the
/// seam driven directly, and the WinForms half source-scanned - the same
/// split <c>TrayMacroBuilderMenuTests</c> makes, and for the same reason
/// (<c>LunaPanel.Tray</c> is <c>net10.0-windows</c> and this assembly does
/// not reference it).
///
/// <b>The source scans in this file are PINS, not driven behaviour.</b>
/// Nothing here proves a menu item appears on a real tray icon; only that
/// the line that adds it is in the file, and that it takes its label and its
/// URL from the seam rather than from a second copy of either.
/// </summary>
public class TrayTransferMenuTests
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
    /// The loopback address, the host port and the fragment that lands on
    /// the Import/export pane. Asserted as the whole literal URL: each part
    /// is separately wrong in a way nobody would notice from a screenshot.
    /// </summary>
    [Fact]
    public void TheUrl_IsTheLoopbackHostPort_OpenedStraightOntoTheTransferPane()
    {
        Assert.Equal("http://127.0.0.1:51824/#transfer", TrayTransfer.Url(51824));
    }

    /// <summary>
    /// Never the LAN address: that one is reachable from every device on the
    /// network and grants nothing, so a menu item pointing at it would open
    /// a page showing the commander a pairing screen.
    /// </summary>
    [Fact]
    public void TheUrl_IsNeverAnythingButLoopback()
    {
        Assert.StartsWith("http://127.0.0.1:", TrayTransfer.Url(51824), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUrl_IsNullWhenNoHostListenerWasBound()
    {
        Assert.Null(TrayTransfer.Url(0));
    }

    /// <summary>
    /// The two tray items open two different panes. One test rather than two
    /// pinning one fragment each, because the claim is that they DIFFER - a
    /// copy-paste that left both on <c>#macros</c> would satisfy either
    /// single-sided assertion.
    /// </summary>
    [Fact]
    public void TheTwoTrayItems_OpenDifferentPanes()
    {
        Assert.NotEqual(TrayMacroBuilder.Url(51824), TrayTransfer.Url(51824));
        Assert.EndsWith("#transfer", TrayTransfer.Url(51824), StringComparison.Ordinal);
        Assert.EndsWith("#macros", TrayMacroBuilder.Url(51824), StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserted as a literal, because a device refused a transfer route is
    /// told to go and use this exact item by name
    /// (<c>HostOnlyRoutes.NotTheHostTransferAdvice</c>) - the two are pinned
    /// against each other in <c>HostOnlyRoutesTests</c>, and this is the end
    /// of that chain that a person actually reads off a menu.
    /// </summary>
    [Fact]
    public void TheMenuLabel_SaysWhatItDoes()
    {
        Assert.Equal("Import or export", TrayTransfer.MenuLabel);
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
            b => b.Action == TrayAction.Transfer);
    }

    [Fact]
    public void TheTrayMenu_CarriesTheItem_LabelledFromTheSeam()
    {
        Assert.Contains(
            "menu.Items.Add(TrayTransfer.MenuLabel, null, (_, _) => Dispatch(TrayAction.Transfer))",
            ReadTraySource(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheTray_TakesTheUrlFromTheSeam()
    {
        Assert.Contains("TrayTransfer.Url(options.HostAccessPort)", ReadTraySource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Left off the menu entirely when no host listener was bound, rather
    /// than added and pointed at nothing - the same condition
    /// <c>TrayMacroBuilder</c>'s item is gated on, pinned here because it is
    /// a second <c>if</c> that could easily have been forgotten.
    /// </summary>
    [Fact]
    public void TheItem_IsGatedOnAHostListenerExisting()
    {
        Assert.Contains("if (_transferUrl is not null)", ReadTraySource(), StringComparison.Ordinal);
    }
}
