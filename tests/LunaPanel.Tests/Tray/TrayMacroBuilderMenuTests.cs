using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// The tray's "Build a macro" item: the seam driven directly, and the
/// WinForms half source-scanned - the same split every other tray test in
/// this namespace makes, and for the same reason
/// (<c>LunaPanel.Tray</c> is <c>net10.0-windows</c> and this assembly does
/// not reference it; see <c>AddDeviceFormSourceGuardTests</c>).
/// </summary>
public class TrayMacroBuilderMenuTests
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
    /// The loopback address, the host port, and the fragment that lands on
    /// the Macros pane rather than on the panel with the builder three taps
    /// away. Asserted as the whole literal URL: each part is separately
    /// wrong in a way nobody would notice from a screenshot.
    /// </summary>
    [Fact]
    public void TheUrl_IsTheLoopbackHostPort_OpenedStraightOntoTheMacrosPane()
    {
        Assert.Equal("http://127.0.0.1:51824/#macros", TrayMacroBuilder.Url(51824));
    }

    /// <summary>
    /// Never the LAN address: that one is reachable from every device on the
    /// network and grants nothing, so a menu item pointing at it would open
    /// a page showing the commander a pairing screen.
    /// </summary>
    [Fact]
    public void TheUrl_IsNeverAnythingButLoopback()
    {
        Assert.StartsWith("http://127.0.0.1:", TrayMacroBuilder.Url(51824), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUrl_IsNullWhenNoHostListenerWasBound()
    {
        Assert.Null(TrayMacroBuilder.Url(0));
    }

    [Fact]
    public void TheMenuLabel_SaysWhatItDoes()
    {
        Assert.Equal("Build a macro", TrayMacroBuilder.MenuLabel);
    }

    /// <summary>
    /// The menu item itself. A source scan is all this suite can do - and
    /// what it can still see is the whole chain: that the item is added to
    /// the tray menu, that its label is the seam's own rather than a second
    /// copy of the string, and that clicking it reaches the launcher.
    /// </summary>
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
            b => b.Action == TrayAction.MacroBuilder);
    }

    [Fact]
    public void TheTrayMenu_CarriesTheItem_LabelledFromTheSeam()
    {
        var content = ReadTraySource();

        Assert.Contains("menu.Items.Add(TrayMacroBuilder.MenuLabel, null, (_, _) => Dispatch(TrayAction.MacroBuilder))", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The URL comes from the seam, keyed on the host port the options
    /// carry - not from a string built in the WinForms layer where nothing
    /// could check it.
    /// </summary>
    [Fact]
    public void TheTray_TakesTheUrlFromTheSeam()
    {
        var content = ReadTraySource();

        Assert.Contains("TrayMacroBuilder.Url(options.HostAccessPort)", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>UseShellExecute = true</c> is what makes a URL open in the default
    /// browser at all - without it, <c>Process.Start</c> tries to execute
    /// the string as a program and throws. The commander would see the
    /// fallback message box every time, and the feature would be "the tray
    /// tells you an address to type".
    /// </summary>
    [Fact]
    public void TheLauncher_OpensTheDefaultBrowser_NotAProcess()
    {
        var content = ReadTraySource();

        Assert.Contains("UseShellExecute = true", content, StringComparison.Ordinal);
    }
}
