using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// The tray's "Macro timing" item (<c>ref/docs/macro-timing.md</c>): the
/// commander's <i>"I would like to move this to a server side adjustment
/// area if possible"</i>. The same split
/// <c>TrayMacroBuilderMenuTests</c>/<c>TrayTransferMenuTests</c> make - the
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
public class TrayMacroTimingMenuTests
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
    /// Timing pane. Asserted as the whole literal URL: each part is
    /// separately wrong in a way nobody would notice from a screenshot.
    /// </summary>
    [Fact]
    public void TheUrl_IsTheLoopbackHostPort_OpenedStraightOntoTheTimingPane()
    {
        Assert.Equal("http://127.0.0.1:51824/#timing", TrayMacroTiming.Url(51824));
    }

    /// <summary>
    /// Never the LAN address: that one is reachable from every device on the
    /// network and grants nothing, so a menu item pointing at it would open a
    /// page showing the commander a pairing screen.
    /// </summary>
    [Fact]
    public void TheUrl_IsNeverAnythingButLoopback()
    {
        Assert.StartsWith("http://127.0.0.1:", TrayMacroTiming.Url(51824), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUrl_IsNullWhenNoHostListenerWasBound()
    {
        Assert.Null(TrayMacroTiming.Url(0));
    }

    /// <summary>
    /// All three host-surface tray items open three different panes. One test
    /// rather than three pinning one fragment each, because the claim is that
    /// they DIFFER - a copy-paste that left this one on <c>#macros</c> would
    /// satisfy any single-sided assertion, and would land the commander on
    /// the macro builder every time they went looking for timing.
    /// </summary>
    [Fact]
    public void TheThreeTrayItems_OpenThreeDifferentPanes()
    {
        Assert.NotEqual(TrayMacroBuilder.Url(51824), TrayMacroTiming.Url(51824));
        Assert.NotEqual(TrayTransfer.Url(51824), TrayMacroTiming.Url(51824));
        Assert.EndsWith("#timing", TrayMacroTiming.Url(51824), StringComparison.Ordinal);
        Assert.EndsWith("#macros", TrayMacroBuilder.Url(51824), StringComparison.Ordinal);
        Assert.EndsWith("#transfer", TrayTransfer.Url(51824), StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserted as a literal. A commander who has just watched a macro miss
    /// keystrokes is looking for the word "timing" on this menu, and no
    /// other item on it says anything close.
    /// </summary>
    [Fact]
    public void TheMenuLabel_SaysWhatItDoes()
    {
        Assert.Equal("Macro timing", TrayMacroTiming.MenuLabel);
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
            b => b.Action == TrayAction.MacroTiming);
    }

    [Fact]
    public void TheTrayMenu_CarriesTheItem_LabelledFromTheSeam()
    {
        Assert.Contains(
            "menu.Items.Add(TrayMacroTiming.MenuLabel, null, (_, _) => Dispatch(TrayAction.MacroTiming))",
            ReadTraySource(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheTray_TakesTheUrlFromTheSeam()
    {
        Assert.Contains("TrayMacroTiming.Url(options.HostAccessPort)", ReadTraySource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Left off the menu entirely when no host listener was bound, rather
    /// than added and pointed at nothing - the same condition the other two
    /// host-surface items are gated on, pinned here because it is a third
    /// <c>if</c> that could easily have been forgotten.
    /// </summary>
    [Fact]
    public void TheItem_IsGatedOnAHostListenerExisting()
    {
        Assert.Contains("if (_macroTimingUrl is not null)", ReadTraySource(), StringComparison.Ordinal);
    }
}
