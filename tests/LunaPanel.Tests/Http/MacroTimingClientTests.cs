using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// The client half of "a macro-timing change made on the PC reaches a
/// connected tablet" (<c>ref/docs/macro-timing.md</c>).
///
/// <b>Every assertion in this file is a PIN, not driven behaviour.</b> There
/// is no headless browser in this suite, so nothing here proves the page
/// actually re-renders a field when a push arrives - only that the code that
/// would do it is present, is reached from the live handler, and sits where
/// it has to sit to run at all. The one thing that IS driven end to end is
/// the server side of the same feature, in
/// <c>ServerHostBuilderTests.PanelLive_MacroTimingSavedOnTheHost_...</c>.
/// </summary>
public class MacroTimingClientTests
{
    private static string DevicePage() => PanelClientEndpoint.BuildPage();

    private static string HostPage() => PanelClientEndpoint.BuildPage(canAuthorMacros: true);

    /// <summary>
    /// <c>applyLive</c>'s body only - never the whole page. The ordering
    /// claim below is about the order two statements execute in inside one
    /// function, which is exactly what source order means here; asserted
    /// against the whole file it would degrade into "written earlier in the
    /// document", which means nothing.
    /// </summary>
    private static string ApplyLiveBody(string page)
    {
        var normalised = page.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = normalised.IndexOf("function applyLive(state) {", StringComparison.Ordinal);
        Assert.True(start >= 0, "applyLive(state) was not found in the built page at all.");

        // The next brace in the first column closes the function - every
        // brace inside it is indented.
        var end = normalised.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, "applyLive(state) appeared to have no closing brace.");
        return normalised[start..end];
    }

    /// <summary>
    /// The tray's "Macro timing" opens this page at <c>#timing</c>. Pinned
    /// against the tray's own URL rather than a repeated literal, so the two
    /// cannot drift into disagreeing about the fragment - the same shape
    /// <c>TransferClientTests</c> uses for <c>#transfer</c>.
    /// </summary>
    [Fact]
    public void ThePage_OpensTheTimingPane_ForTheFragmentTheTraySends()
    {
        var page = HostPage();

        Assert.Contains("location.hash === '#timing'", page, StringComparison.Ordinal);
        Assert.Contains("showSettingsPane('paneTiming')", page, StringComparison.Ordinal);
        Assert.EndsWith("#timing", LunaPanel.Server.Tray.TrayMacroTiming.Url(51824), StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The trap this pin exists for.</b> <c>applyLive</c> used to return
    /// early when the game was not running, hiding the grid and stopping -
    /// which meant a timing push was delivered, parsed, and thrown away for
    /// every commander who had not launched Elite yet, exactly the commander
    /// most likely to be sitting on the PC adjusting settings (ver-0.45.6.0-dev
    /// removed that early return so the grid stays usable with Elite closed).
    /// This pin now guards the surviving half of that fix: a pushed timing is
    /// still applied unconditionally, and no gameRunning gate has crept back
    /// in ahead of it to reintroduce the drop.
    /// </summary>
    [Fact]
    public void APushedTiming_IsAppliedUnconditionally_WithNoGameRunningGateAheadOfIt()
    {
        foreach (var body in new[] { ApplyLiveBody(DevicePage()), ApplyLiveBody(HostPage()) })
        {
            var applyAt = body.IndexOf("applyPushedTiming(", StringComparison.Ordinal);

            Assert.True(applyAt >= 0, "applyLive never applies a pushed timing at all.");
            Assert.DoesNotContain("if (!state.gameRunning)", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An edge, not a level: every ordinary push carries <c>timing: null</c>,
    /// and applying that would blank both fields on the next heartbeat.
    /// </summary>
    [Fact]
    public void APushedTiming_IsOnlyAppliedWhenThePushActuallyCarriesOne()
    {
        Assert.Contains("if (state.timing) applyPushedTiming(state.timing);", DevicePage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Both fields, and the below-minimum warning re-evaluated afterwards -
    /// a pushed value can be below the measured minimum just as easily as a
    /// typed one, and the warning is the whole feature there
    /// (<c>ref/docs/macro-timing.md</c>'s "The label is the feature, not the
    /// limit").
    /// </summary>
    [Fact]
    public void ApplyPushedTiming_SetsBothFields_AndRechecksTheWarning()
    {
        var page = DevicePage();

        Assert.Contains("el('holdMsInput').value = timing.holdMs;", page, StringComparison.Ordinal);
        Assert.Contains("el('gapMsInput').value = timing.interPressGapMs;", page, StringComparison.Ordinal);
        Assert.Contains("function applyPushedTiming(timing) {", page, StringComparison.Ordinal);

        var start = page.IndexOf("function applyPushedTiming(timing) {", StringComparison.Ordinal);
        var end = page.IndexOf("\n}", start, StringComparison.Ordinal);
        var body = page[start..end];
        Assert.Contains("el('holdMsInput').value", body, StringComparison.Ordinal);
        Assert.Contains("el('gapMsInput').value", body, StringComparison.Ordinal);
        Assert.Contains("checkTimingWarnings()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The device's own Timing pane is untouched by all of this - the
    /// commander asked for a server-side area, not for this one to be taken
    /// away. Pinned on the DEVICE page specifically (the one that would be
    /// stripped if somebody decided timing was host-only), and by the tab
    /// existing rather than by the pane markup, because hiding the tab is
    /// how it would actually be removed.
    /// </summary>
    [Fact]
    public void TheDevicePage_StillCarriesTheTimingTab_AndItsEditableFields()
    {
        var page = DevicePage();

        Assert.Contains("data-pane=\"paneTiming\">Timing</button>", page, StringComparison.Ordinal);
        Assert.Contains("<input type=\"number\" id=\"holdMsInput\"", page, StringComparison.Ordinal);
        Assert.Contains("<input type=\"number\" id=\"gapMsInput\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("el('tabTiming').classList.toggle('hidden'", page, StringComparison.Ordinal);
        Assert.DoesNotContain("el('holdMsInput').readOnly = true", page, StringComparison.Ordinal);
    }
}
