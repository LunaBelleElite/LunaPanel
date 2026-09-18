using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// The client half of the tray's "Edit Live Panels" item
/// (<c>ref/docs/pairing-and-devices.md</c>): opening this page straight onto
/// its Devices pane, where the per-device "edit a device live from the PC"
/// buttons already live.
///
/// <b>Every assertion in this file is a PIN, not driven behaviour.</b> There
/// is no headless browser in this suite, so nothing here proves the page
/// actually shows the Devices pane on screen - only that the code that would
/// do it is present and takes its fragment from the same seam the tray's own
/// URL uses, the same shape <c>TransferClientTests</c>/
/// <c>MacroTimingClientTests</c> use for their own fragments.
/// </summary>
public class DevicesClientTests
{
    private static string HostPage() => PanelClientEndpoint.BuildPage(canAuthorMacros: true);

    /// <summary>
    /// The tray opens this page at <c>#devices</c>. Pinned against the
    /// tray's own URL rather than a repeated literal, so the two cannot
    /// drift into disagreeing about the fragment.
    /// </summary>
    [Fact]
    public void ThePage_OpensTheDevicesPane_ForTheFragmentTheTraySends()
    {
        var page = HostPage();

        Assert.Contains("location.hash === '#devices'", page, StringComparison.Ordinal);
        Assert.Contains("showSettingsPane('paneDevices')", page, StringComparison.Ordinal);
        Assert.EndsWith("#devices", LunaPanel.Server.Tray.TrayEditLivePanels.Url(51824), StringComparison.Ordinal);
    }
}
