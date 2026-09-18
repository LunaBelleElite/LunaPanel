using System.Reflection;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Pins <see cref="PanelClientEndpoint.BuildPage"/>'s shape - the parts of a
/// hand-written HTML/CSS/JS page a test can actually hold to a claim without
/// a browser: well-formedness, the hard "no CDN, no external reference"
/// constraint (the device may have no internet at all), that the real
/// <see cref="ApiPaths"/> route literals were substituted in rather than
/// typed twice, the three rendering rules carried forward verbatim from
/// <c>ref/docs/device-calibration.md</c>, and that the client's one
/// remaining duplicated layout constant - gutter (see
/// <see cref="PanelClientEndpoint"/>'s own remarks: <c>headerStrip</c> and
/// <c>framePadding</c> are no longer duplicated at all, see
/// <see cref="PanelClientSourceGuardTests"/> - <c>GET /api/panel</c> still
/// never exposes the raw gutter figure) still matches
/// <see cref="CellSizeEstimator"/>'s real private constant.
///
/// <b>What this file does NOT cover, stated plainly rather than implied</b>:
/// no test here drives the page's JavaScript - there is no headless browser
/// in this suite. Whether a tap actually posts to <c>/api/press</c>, whether
/// a degraded slot actually refuses to fire, whether the SSE handler
/// actually updates a lit class - none of that is unit-tested. Only the
/// route wiring (a real Kestrel round trip in <c>ServerHostBuilderTests</c>)
/// and this file's static-content pins exist.
/// </summary>
public class PanelClientEndpointTests
{
    private static double RealConst(string fieldName)
    {
        var field = typeof(CellSizeEstimator).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetRawConstantValue();
        Assert.NotNull(value);
        return (double)value!;
    }

    [Fact]
    public void BuildPage_IsAWellFormedHtmlDocument()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.StartsWith("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("</html>", html.TrimEnd(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("<script src")]
    [InlineData("<link ")]
    [InlineData("cdn.")]
    public void BuildPage_ContainsNoExternalReference(string forbiddenNeedle)
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain(forbiddenNeedle, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPage_SubstitutesTheRealApiRouteConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.Press}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.Pair}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.PanelLive}'", html, StringComparison.Ordinal);
        Assert.Contains(ApiPaths.Panel, html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_UsesBreakWord_NeverAnywhere()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("overflow-wrap: break-word", html, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow-wrap: anywhere", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HonoursTheUsersOwnLineBreak_ViaWhiteSpacePreLine()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("white-space: pre-line", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// ver-0.45.6.0-dev removed the "Elite Dangerous is not running"
    /// placeholder and the grid-hiding it gated: the grid now stays visible
    /// and tappable with the game closed, so a commander can lay out and
    /// test buttons without launching it. The separate, unconditional
    /// "Elite Dangerous is not the active window" refusal at press time
    /// (<c>InjectionGuard</c>) is untouched by this and still applies.
    /// </summary>
    [Fact]
    public void BuildPage_NoLongerHidesTheGridWhenTheGameIsNotRunning()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain("Elite Dangerous is not running", html, StringComparison.Ordinal);
        Assert.DoesNotContain("notRunning", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HasAFullscreenControl()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("requestFullscreen", html, StringComparison.Ordinal);
        Assert.Contains("id=\"fsBtn\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HasAPairingForm_PostingToPair()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"pairCode\"", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.Pair}'", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The settings gear (ref/docs/theme.md's colour chain) and the deleted
    // temporary palette experiment.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildPage_DeletesTheTemporaryPaletteExperiment()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain("palette-experiment", html, StringComparison.Ordinal);
        // The hardcoded cyan-border/pink-text/white-lit values it forced on
        // every commander regardless of their own theme.
        Assert.DoesNotContain("#95fbff", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#ff8ecb", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPage_HasASettingsGear_OpeningASheet()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"gearBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"settingsSheet\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// RENAMED 2026-09-10 from
    /// <c>BuildPage_SettingsSheet_HasTheThreeRoleSwatchPickers_AndTheOneColourShortcut_AndAReset</c>:
    /// Background became a fourth commander-choosable role
    /// (<c>ref/docs/theme.md</c>), so the old name asserted a count that is
    /// no longer true. A supersession of what the pane offers, not a
    /// weakening - the three original rows are all still pinned below,
    /// unchanged.
    ///
    /// The background row is asserted as its whole button element rather
    /// than as the bare <c>data-role="background"</c> attribute the other
    /// three use, and that is deliberate: the page's own JavaScript
    /// contains <c>.roleSwatch[data-role="background"]</c> as a
    /// querySelector string, so the bare needle stays green with the row
    /// deleted outright. Measured 2026-09-10 by deleting the row: this
    /// test did not redden until the needle was narrowed. The same
    /// weakness applies to the three original needles above and is left
    /// alone here rather than widened into unrelated work.
    /// </summary>
    [Fact]
    public void BuildPage_SettingsSheet_HasTheFourRoleSwatchPickers_AndTheOneColourShortcut_AndAReset()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("data-role=\"border\"", html, StringComparison.Ordinal);
        Assert.Contains("data-role=\"text\"", html, StringComparison.Ordinal);
        Assert.Contains("data-role=\"lit\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"swatch roleSwatch\" data-role=\"background\"", html, StringComparison.Ordinal);
        Assert.Contains("data-role=\"all\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"resetTheme\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// PIN over the served page's own text, not driven behaviour - nothing
    /// in this suite executes the page, so what is proven here is that the
    /// row and its wiring are present and spelled the way the server's JSON
    /// field names require, never that a tap produces a colour.
    /// </summary>
    [Fact]
    public void BuildPage_BackgroundSwatch_IsPaintedFromTheStatusResponsesBackgroundField()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("<span>Background</span>", html, StringComparison.Ordinal);
        Assert.Contains(".roleSwatch[data-role=\"background\"]').style.background = themeState.background;", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// PIN over the served page's own text. Two claims in one exact line,
    /// because both live in the same expression: "one colour for
    /// everything" must NOT set the background (that paints the panel a
    /// single flat rectangle), and a background that was never chosen is
    /// posted as null rather than as the currently-derived ground
    /// (<c>ref/docs/button-naming.md</c>'s override principle). Asserted as
    /// the whole literal expression so any edit to either half reddens -
    /// a looser "contains background:" needle would survive both mistakes.
    /// </summary>
    [Fact]
    public void BuildPage_OneColourForEverything_LeavesTheBackgroundAlone_AndADerivedGroundIsNeverPostedBack()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "background: role === 'background' ? hex : (themeState.backgroundChosen ? themeState.background : null),",
            html,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// PIN over the served page's own text. The contrast warning
    /// (<c>ref/docs/theme.md</c>'s contrast section) ships hidden and is
    /// un-hidden from <c>lowContrast</c>; it never gates a pick, which is
    /// why there is no assertion here about refusing anything.
    /// </summary>
    [Fact]
    public void BuildPage_HasAContrastWarningLine_ShippedHidden_AndDrivenByLowContrast()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("<p id=\"colourWarning\" class=\"hidden\"></p>", html, StringComparison.Ordinal);
        Assert.Contains("themeState.lowContrast", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// SUPERSEDES this test's own previous assertion that
    /// <c>id="overrideToggle"</c> exists. 2026-09-06 (second correction -
    /// ref/docs/theme.md's "commander's real mapping"): the brief that
    /// produced the checkbox was wrong - "Completely override colours"
    /// hid the three role rows until ticked, when the actual spec is three
    /// rows, always visible, with no checkbox gating them at all. The
    /// override itself is still created only by picking a swatch, never by
    /// a checkbox (button-naming.md's override principle) - this is a
    /// supersession of what the sheet shows, not a weakening of that rule.
    /// </summary>
    [Fact]
    public void BuildPage_SettingsSheet_HasNoOverrideCheckbox_ThreeRoleRowsAreAlwaysVisible()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain("overrideToggle", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Completely override colours", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The colour picker modal opened by tapping any role swatch: two
    /// labelled groups (ref/docs/web-client.md) - the commander's own EDHM
    /// colours ("From your HUD", populated dynamically, so it isn't
    /// expected to appear pre-labelled with any specific colour name at
    /// build time) and the always-present built-in palette ("Standard
    /// colours").
    /// </summary>
    [Fact]
    public void BuildPage_HasAColourPickerModal_WithFromYourHudAndStandardColoursGroups()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"colourPicker\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"pickerHudGroup\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"pickerHudSwatches\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"pickerStandardSwatches\"", html, StringComparison.Ordinal);
        Assert.Contains("From your HUD", html, StringComparison.Ordinal);
        Assert.Contains("Standard colours", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_SubstitutesTheThemeRouteConstants()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.Theme}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.ThemeReset}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__PALETTE_", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_EmbedsTheRealBuiltInPalette_NotACopiedList()
    {
        var html = PanelClientEndpoint.BuildPage();

        // Every swatch's real hex value (LunaPanel.Core.Theme.HudPalette)
        // must appear in the embedded palette JSON - proving the page reads
        // the shipped data rather than a second hand-typed list that could
        // drift from it.
        foreach (var swatch in LunaPanel.Core.Theme.HudPalette.Swatches)
        {
            Assert.Contains(swatch.Color.ToHex(), html, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// SUPERSEDES this test's own previous body (before 2026-09-07): it used
    /// to assert the page's JavaScript contained the literal
    /// <c>isPhone() ? 40 : 48</c> - <see cref="CellSizeEstimator"/>'s
    /// phone/tablet <c>headerStrip</c> figures, restated as a second,
    /// hand-copied constant because <c>GET /api/panel</c> did not yet return
    /// them. That restatement is exactly what caused two shipped defects in
    /// opposite directions (LC9 in <c>tests/notes/live-checks.md</c>: chrome
    /// too tall, bottom row pushed off screen; and its opposite, buttons
    /// flush to the screen edge) and was about to be a third occurrence for
    /// the tab row - see <c>ref/docs/panels-and-pages.md</c>'s "Tabs".
    ///
    /// The fix removes the duplication rather than re-syncing it:
    /// <c>GET /api/panel</c> now returns <c>headerStrip</c> directly
    /// (<see cref="PanelEndpoint.PanelResponse.HeaderStrip"/>), and this test
    /// now asserts the client reads that field rather than recomputing it -
    /// plus <see cref="PanelClientSourceGuardTests"/>'s source-scan pin,
    /// which fails the build if the removed literal pattern ever reappears
    /// (a behavioural test like this one cannot see a re-introduction that
    /// happens to compute the same html output through a differently-shaped
    /// call, the same reasoning <c>DeviceRegistryTests</c>' own source-scan
    /// pin documents).
    /// </summary>
    [Fact]
    public void BuildPage_NoLongerRestatesHeaderStripAsAJavaScriptLiteral_ConsumesTheServersFieldInstead()
    {
        var phone = (int)RealConst("PhoneHeaderStrip");
        var tablet = (int)RealConst("TabletHeaderStrip");
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain($"isPhone() ? {phone} : {tablet}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("headerStripFor", html, StringComparison.Ordinal);
        Assert.Contains("data.headerStrip", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// SUPERSEDES this test's own previous body (before 2026-09-07) - same
    /// reasoning and same dated record as
    /// <see cref="BuildPage_NoLongerRestatesHeaderStripAsAJavaScriptLiteral_ConsumesTheServersFieldInstead"/>,
    /// applied to <c>framePadding</c> instead of <c>headerStrip</c>.
    /// </summary>
    [Fact]
    public void BuildPage_NoLongerRestatesFramePaddingAsAJavaScriptLiteral_ConsumesTheServersFieldInstead()
    {
        var phone = (int)RealConst("PhoneFramePadding");
        var tablet = (int)RealConst("TabletFramePadding");
        var html = PanelClientEndpoint.BuildPage();

        Assert.DoesNotContain($"isPhone() ? {phone} : {tablet}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("framePadFor", html, StringComparison.Ordinal);
        Assert.Contains("data.framePadding", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tab row's own allowance (<c>tabStripHeight</c>) never had a
    /// client-side literal to begin with - it is designed correctly from the
    /// start the same way <c>headerStrip</c>/<c>framePadding</c> now are,
    /// rather than being introduced as a third duplicate and fixed later.
    /// </summary>
    [Fact]
    public void BuildPage_TabStripHeight_IsConsumedFromTheServersField_NeverAClientLiteral()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("data.tabStripHeight", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tabStripFor", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The tab row itself (ref/docs/panels-and-pages.md's "Tabs").
    // -------------------------------------------------------------------

    [Fact]
    public void BuildPage_HasATabsRow()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"tabs\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_TabsRow_IsRenderedFromPageNamesAndPageIndex_AndSwitchesPageOnTap()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("data.pageNames", html, StringComparison.Ordinal);
        Assert.Contains("data.pageIndex", html, StringComparison.Ordinal);
        Assert.Contains("currentPage = index", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A press must target whichever page is actually showing, not a
    /// hardcoded page 0 - the exact regression this pins against: before
    /// tabs existed, every press body was <c>{ page: 0, slot: ... }</c>
    /// unconditionally, which would silently mis-target every slot on any
    /// page past the first once tabs made a second page reachable.
    /// </summary>
    [Fact]
    public void BuildPage_PressBody_UsesCurrentPage_NotAHardcodedZero()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("page: currentPage", html, StringComparison.Ordinal);
        Assert.DoesNotContain("page: 0,", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_GutterConstants_MatchCellSizeEstimatorsRealPrivateFields()
    {
        var phone = (int)RealConst("PhoneGutter");
        var tablet = (int)RealConst("TabletGutter");
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"isPhone() ? {phone} : {tablet}", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The template picker moved into settings, and the "merging and
    // expanding panels" toggle (ref/docs/panels-and-pages.md).
    // -------------------------------------------------------------------

    /// <summary>
    /// SUPERSEDES this test's own previous name and body
    /// (<c>BuildPage_SettingsSheet_HasThreeTabs_ColourPanelsAndBindings</c>,
    /// before the Devices pane below existed): the sheet now holds four
    /// tabs, not three, and the old name would read as false the moment a
    /// fourth existed even though its own assertions never actually
    /// excluded one.
    /// </summary>
    [Fact]
    public void BuildPage_SettingsSheet_HasFourTabs_ColourPanelsBindingsAndDevices()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("data-pane=\"paneColour\"", html, StringComparison.Ordinal);
        Assert.Contains("data-pane=\"panePanels\"", html, StringComparison.Ordinal);
        Assert.Contains("data-pane=\"paneBindings\"", html, StringComparison.Ordinal);
        Assert.Contains("data-pane=\"paneDevices\"", html, StringComparison.Ordinal);
        Assert.Contains(">Colour<", html, StringComparison.Ordinal);
        Assert.Contains(">Panels<", html, StringComparison.Ordinal);
        Assert.Contains(">Controls<", html, StringComparison.Ordinal);
        Assert.Contains(">Devices<", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The Devices pane (ref/docs/pairing-and-devices.md): lists every
    // paired device, lets this device open pairing for a new one ("Add
    // another device"), and forgets any OTHER device - never itself.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildPage_HasADevicesPane_WithAnAddAnotherDeviceButtonAndAList()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"paneDevices\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"addAnotherDevice\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"deviceList\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_SubstitutesTheDevicesRouteConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.Devices}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.DevicesForget}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.DevicesOpenPairing}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_DEVICES__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_DEVICES_FORGET__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_DEVICES_OPEN_PAIRING__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// "A device never forgets itself" (ref/docs/pairing-and-devices.md,
    /// decided 2026-09-07) has to hold in the rendering logic itself, not
    /// just on the server: the row for whichever device matches the
    /// response's own <c>selfDeviceId</c> must never get a forget button at
    /// all, rather than one that merely happens to be disabled or hidden by
    /// CSS. Asserted against the actual conditional in the source, since
    /// there is no headless browser in this suite to render the list and
    /// check which row got a button.
    /// </summary>
    [Fact]
    public void BuildPage_DeviceList_NeverRendersAForgetButtonOnTheCallersOwnRow()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("device.deviceId !== data.selfDeviceId", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_ForgetDevice_PostsTheDeviceId_AndReloadsTheListOnSuccess()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("async function onForgetDevice(deviceId)", html, StringComparison.Ordinal);
        var start = html.IndexOf("async function onForgetDevice(deviceId)", StringComparison.Ordinal);
        var end = html.IndexOf("\nasync function", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];
        Assert.Contains("JSON.stringify({ deviceId })", body, StringComparison.Ordinal);
        Assert.Contains("loadDevicesPane()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The route this task's brief calls "the one that gets used": opening
    /// pairing from an already-paired device's own settings must show the
    /// fresh code on THIS device's screen, not merely confirm success.
    /// </summary>
    [Fact]
    public void BuildPage_AddAnotherDevice_ShowsTheFreshCodeOnThisDevicesOwnScreen()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("async function onAddAnotherDevice()", html, StringComparison.Ordinal);
        var start = html.IndexOf("async function onAddAnotherDevice()", StringComparison.Ordinal);
        var end = html.IndexOf("\nasync function", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];
        Assert.Contains("body.code", body, StringComparison.Ordinal);
    }

    /// <remarks>
    /// [2026-09-10, superseded: the needle was
    /// <c>"tab.dataset.pane === 'paneDevices'"</c>.] The per-pane loads moved
    /// out of the tab click handler into <c>showSettingsPane(paneId)</c>, so
    /// the tray's "Build a macro" could open the Macros pane through the
    /// same one path a tapped tab does rather than a second copy of it
    /// (<c>ref/docs/hosting.md</c>). The claim is unchanged - selecting the
    /// Devices pane loads the list - and the second half of it is now
    /// pinned properly: that the tab handler still reaches that function at
    /// all, which the old needle happened to cover by spelling
    /// <c>tab.dataset.pane</c> and this one would otherwise have lost.
    /// </remarks>
    [Fact]
    public void BuildPage_DevicesTab_LoadsTheListWhenSelected()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("paneId === 'paneDevices'", html, StringComparison.Ordinal);
        Assert.Contains("loadDevicesPane()", html, StringComparison.Ordinal);
        Assert.Contains("showSettingsPane(tab.dataset.pane)", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The Bindings pane (ref/docs/bindings-source.md's "It has to be
    // visible") and its refresh control.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildPage_HasABindingsPane_WithAStatusLineAndARefreshButton()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"paneBindings\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"bindingsStatus\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"refreshBindings\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_SubstitutesTheBindingsRouteConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.BindingsStatus}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.BindingsRefresh}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_BINDINGS_STATUS__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_BINDINGS_REFRESH__", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_RefreshBindings_ReloadsThePanel_SoAFreshBindTakesEffectImmediately()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("async function refreshBindings()", html, StringComparison.Ordinal);
        Assert.Contains("refreshBindings').addEventListener('click'", html, StringComparison.Ordinal);
        // The bindings refresh handler's own body must call loadPanel(), not
        // merely happen to appear somewhere earlier in the file alongside
        // some unrelated call to it.
        var refreshBodyStart = html.IndexOf("async function refreshBindings()", StringComparison.Ordinal);
        var refreshBodyEnd = html.IndexOf("el('refreshBindings').addEventListener", StringComparison.Ordinal);
        Assert.True(refreshBodyStart >= 0 && refreshBodyEnd > refreshBodyStart);
        var refreshBody = html[refreshBodyStart..refreshBodyEnd];
        Assert.Contains("await loadPanel()", refreshBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HasATemplatePickerPane_RenderedFromTheRealApiTemplatesRoute()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"rungLadder\"", html, StringComparison.Ordinal);
        Assert.Contains($"{ApiPaths.Templates}?w=", html, StringComparison.Ordinal);
        Assert.Contains("r.verdict", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HasAMergeExpandToggle_WiredToTheRealApiPanelSettingsRoute()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"mergeExpandToggle\"", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.PanelSettings}'", html, StringComparison.Ordinal);
        Assert.Contains("mergeExpand", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The "?" must be tap-to-open, not hover (ref/docs/panels-and-pages.md's
    /// own heading, verbatim) - these are touch devices, and a hover
    /// tooltip is invisible on the exact hardware this ships to. Pinned two
    /// ways: the help button has a click listener that opens the existing
    /// tap-triggered #toast (never a new, parallel popup mechanism), and no
    /// CSS `:hover` rule governs anything under this pane - a `:hover`-only
    /// reveal would be the exact defect this test exists to catch, and
    /// would still pass every other pin in this file.
    /// </summary>
    [Fact]
    public void BuildPage_MergeExpandHelp_IsTapToOpen_NeverHover()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"mergeExpandHelp\"", html, StringComparison.Ordinal);
        Assert.Contains("mergeExpandHelp').addEventListener('click'", html, StringComparison.Ordinal);
        Assert.DoesNotContain(":hover", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_SubstitutesTheTemplatePickerRouteConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(ApiPaths.Templates, html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.PanelTemplate}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.PanelSettings}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_TEMPLATES__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_PANEL_TEMPLATE__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_PANEL_SETTINGS__", html, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The Timing pane (ref/docs/macro-timing.md) - server-wide hold
    // duration/inter-press gap, unlike every other pane on this sheet.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildPage_HasATimingTabAndPane_WiredToTheRealApiMacroTimingRoute()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("data-pane=\"paneTiming\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"paneTiming\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"holdMsInput\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"gapMsInput\"", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.MacroTiming}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_MACRO_TIMING__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tap-to-open, never hover - same discipline as
    /// <see cref="BuildPage_MergeExpandHelp_IsTapToOpen_NeverHover"/>, and the
    /// same reason: these are touch devices, and a hover tooltip is invisible
    /// on the exact hardware this ships to.
    /// </summary>
    [Fact]
    public void BuildPage_TimingHelpButtons_AreTapToOpen_NeverHover()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"holdMsHelp\"", html, StringComparison.Ordinal);
        Assert.Contains("holdMsHelp').addEventListener('click'", html, StringComparison.Ordinal);
        Assert.Contains("id=\"gapMsHelp\"", html, StringComparison.Ordinal);
        Assert.Contains("gapMsHelp').addEventListener('click'", html, StringComparison.Ordinal);
        Assert.DoesNotContain(":hover", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verbatim from ref/docs/macro-timing.md's own tooltip copy - these name
    /// the SYMPTOM ("presses being missed entirely" / "lands short"), not a
    /// definition of "hold" or "gap", which is deliberate: the two failures
    /// look different from the cockpit, and that is what tells a commander
    /// which field to touch.
    /// </summary>
    [Fact]
    public void BuildPage_ContainsTheExactHoldAndGapTooltipCopy()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "Raise this first if button presses are being missed entirely. Elite checks the keyboard a few times a second; a key held for less time than that gap can be missed completely. Default 150 ms.",
            html, StringComparison.Ordinal);
        Assert.Contains(
            "Raise this if a button that presses something several times lands short — stepping three menu items and only moving two. Too small a gap and Elite reads two presses as one key being held down. Default 100 ms.",
            html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pinned by CONTENT, not by being non-empty
    /// (<c>ref/docs/macro-timing.md</c>'s "The warning copy" - this project
    /// has already shipped a refusal message whose only assertion was
    /// <c>NotEmpty</c>, so it could have been changed to anything with a
    /// thousand tests still green). The closing sentence is the one that
    /// actually matters (a commander who knows the failure is silent will
    /// suspect this setting; one who does not will hunt for a bug elsewhere),
    /// so it is asserted as its own separate substring rather than folded
    /// into one giant literal, which is exactly the paraphrase risk this
    /// pin exists to catch.
    /// </summary>
    [Fact]
    public void BuildPage_ContainsTheExactBelowMinimumWarningCopy()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("Below the tested minimum. This may work fine on your machine", html, StringComparison.Ordinal);
        Assert.Contains(
            "if buttons start doing nothing at all, or a button that presses something several times lands short, raise this back up.",
            html, StringComparison.Ordinal);
        Assert.Contains("Missed keystrokes are silent: nothing will tell you it happened.", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The warning is shown automatically (below each field, driven by
    /// checkTimingWarnings()), never only behind a tap - distinct from the
    /// "?" tooltips above, which explain the field regardless of its current
    /// value. Pinned by structure (two dedicated warning elements, hidden by
    /// default, toggled from JavaScript) rather than by re-asserting the
    /// copy a second time.
    /// </summary>
    [Fact]
    public void BuildPage_TimingWarnings_AreTwoSeparateElements_HiddenByDefault_ToggledFromScript()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"holdMsWarning\" class=\"timingWarning hidden\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"gapMsWarning\" class=\"timingWarning hidden\"", html, StringComparison.Ordinal);
        Assert.Contains("function checkTimingWarnings", html, StringComparison.Ordinal);
        Assert.Contains("holdMsWarning').classList.toggle('hidden'", html, StringComparison.Ordinal);
        Assert.Contains("gapMsWarning').classList.toggle('hidden'", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// SUPERSEDED 2026-09-08 (was
    /// <c>BuildPage_PhoneTabletBoundary_MatchesCellSizeEstimatorsRealPrivateField</c>,
    /// which read the boundary off a private field by reflection and checked
    /// the page's own hand-typed <c>600</c> still agreed with it). The page
    /// no longer restates it: <see cref="CellSizeEstimator.PhoneTabletBoundary"/>
    /// is public and substituted in, because the client now needs the same
    /// number for a second purpose - reporting its own class at pairing
    /// (<c>ref/docs/layout-import.md</c>) - and this project has already paid
    /// twice for a hand-copied copy of one of that type's constants
    /// (<c>tests/notes/live-checks.md</c> LC9 and its opposite).
    ///
    /// Both halves are asserted, and deliberately not against each other:
    /// the boundary's <b>value</b> is pinned as a literal (an assertion), and
    /// the page is pinned to carry that same literal with no placeholder left
    /// behind (which is what proves the substitution ran at all).
    /// </summary>
    [Fact]
    public void BuildPage_SubstitutesTheOneRealPhoneTabletBoundary_RatherThanRestatingIt()
    {
        Assert.Equal(600, CellSizeEstimator.PhoneTabletBoundary);

        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("PHONE_MAX = 600;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__PHONE_TABLET_BOUNDARY__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The client half of Task A: the pairing request has to actually carry
    /// a class and a name, and the class has to come from the same
    /// <c>isPhone()</c> the grid already uses - not a second rule invented
    /// in the pairing handler, which would be a fresh copy of exactly the
    /// constant the pin above exists to stop being copied.
    /// </summary>
    [Fact]
    public void BuildPage_PairRequest_SendsADeviceClassFromIsPhone_AndTheTypedName()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("deviceName: el('pairName').value,", html, StringComparison.Ordinal);
        Assert.Contains("deviceClass: isPhone() ? 'phone' : 'tablet',", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name field is a placeholder, never a pre-filled value. A real
    /// value would be sent verbatim by every commander who ignored the
    /// field, every device would be called "Phone", and the server-side
    /// ordinal ("Phone 2") would never fire - which is the exact state
    /// <c>ref/docs/layout-import.md</c> was written to get out of.
    /// </summary>
    [Fact]
    public void BuildPage_PairNameField_IsAPlaceholderNeverAPrefilledValue()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"pairName\"", html, StringComparison.Ordinal);
        Assert.Contains("el('pairName').placeholder = isPhone() ? 'Phone' : 'Tablet';", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"pairName\" type=\"text\" value=", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Arm 3 of the recovery flow, as a property of the page rather than of
    /// the server: "no orphans" arrives as no <c>recovery</c> object at all,
    /// and the page's very first act on it is to return. A chooser that
    /// opened on an empty list would interrupt every first-ever pairing.
    /// </summary>
    [Fact]
    public void BuildPage_Recovery_ReturnsImmediatelyWhenThereIsNoRecoveryObject()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("async function handleRecovery(recovery) {\n  if (!recovery) return;", html, StringComparison.Ordinal);
        Assert.Contains("recovery.candidates.length > 0", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both halves the spec insists on for an import: a warning before the
    /// overwrite, and an undo offered afterwards. Source-scan pins, matching
    /// this file's own stated limit - there is no headless browser here to
    /// drive a <c>confirm()</c>.
    /// </summary>
    [Fact]
    public void BuildPage_Import_WarnsBeforeOverwriting_AndOffersAnUndoAfterwards()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("This replaces the buttons on this device now.", html, StringComparison.Ordinal);
        Assert.Contains("if (!confirm(warning)) return;", html, StringComparison.Ordinal);
        Assert.Contains($"fetch('{ApiPaths.LayoutImportUndo}'", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Start fresh" is an equal option, not a fallback: it is a real
    /// control in the sheet with its own handler, not something reached only
    /// by dismissing the chooser.
    /// </summary>
    [Fact]
    public void BuildPage_ImportSheet_OffersStartFreshAsItsOwnControl()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"importStartFresh\"", html, StringComparison.Ordinal);
        Assert.Contains("el('importStartFresh').addEventListener('click', closeImportSheet);", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_SubstitutesTheLayoutImportRoutes_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.LayoutImport}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.LayoutImportUndo}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_LAYOUT_IMPORT__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_LAYOUT_IMPORT_UNDO__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__DEVICE_NAME_MAX__", html, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // Reset to default (ref/docs/reset-to-default.md) - the Panels pane's
    // "Reset this device's buttons to default…" control. Same static-content-
    // only limit as everything else in this file: no headless browser here
    // to actually drive confirm() or the click.
    // -----------------------------------------------------------------

    [Fact]
    public void BuildPage_ResetToDefault_ButtonExistsInThePanelsPane()
    {
        var html = PanelClientEndpoint.BuildPage();

        var paneStart = html.IndexOf("id=\"panePanels\"", StringComparison.Ordinal);
        var paneEnd = html.IndexOf("id=\"paneBindings\"", StringComparison.Ordinal);
        Assert.True(paneStart >= 0 && paneEnd > paneStart);
        var pane = html[paneStart..paneEnd];

        Assert.Contains("id=\"resetToDefault\"", pane, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pinned by content, not by non-emptiness - this project has already
    /// shipped a confirmation whose only assertion was <c>NotEmpty</c>, which
    /// a thousand tests would still pass no matter what the copy said. The
    /// spec's three requirements are each asserted as their own substring so
    /// a paraphrase that drops one silently (the exact risk a single giant
    /// literal would hide) is caught: it warns before acting, it names what
    /// is lost (every button, every page, replacing what's there), and it
    /// says the action is recoverable.
    /// </summary>
    [Fact]
    public void BuildPage_ResetToDefault_ContainsTheExactWarningCopy()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("Reset all buttons on this device to default?", html, StringComparison.Ordinal);
        Assert.Contains("Every button on every page returns to the starting layout, replacing what's there now.", html, StringComparison.Ordinal);
        Assert.Contains("This can be undone.", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The brief's own constraint: warn once, then act - never a second
    /// modal stacked on top of the confirmation the way the import sheet's
    /// "keep it?" step currently does. Read between the handler's own
    /// <c>addEventListener</c> and its closing, top-level <c>});</c> - found
    /// by searching for <c>"\n});"</c> rather than bare <c>"});"</c>, because
    /// this handler's own <c>fetch(url, { ... })</c> call ends its line with
    /// <c>'same-origin' });</c>, which contains the bare three-character
    /// needle too and would truncate the scanned body before ever reaching a
    /// second <c>confirm(</c> - the exact way a text-search guard whose
    /// needle can appear somewhere unintended goes vacuous. (Found by this
    /// test failing to redden on its own first mutation, 2026-09-08 - see
    /// the run ledger.)
    /// </summary>
    [Fact]
    public void BuildPage_ResetToDefault_WarnsOnce_AndNeverShowsASecondModal()
    {
        var html = PanelClientEndpoint.BuildPage();

        var handlerStart = html.IndexOf("el('resetToDefault').addEventListener", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0);
        var handlerEnd = html.IndexOf("\n});", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart);
        var handlerBody = html[handlerStart..handlerEnd];

        var confirmCount = 0;
        var searchFrom = 0;
        while (true)
        {
            var i = handlerBody.IndexOf("confirm(", searchFrom, StringComparison.Ordinal);
            if (i < 0)
            {
                break;
            }

            confirmCount++;
            searchFrom = i + 1;
        }

        Assert.Equal(1, confirmCount);
    }

    [Fact]
    public void BuildPage_SubstitutesTheLayoutResetRoute_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.LayoutReset}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_LAYOUT_RESET__", html, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // Lit level rendering (ref/docs/lit-state.md): "off = not lit, regular
    // = partial brightness on the edges, full beam = high glowing edges."
    // Source-scan pins only, matching this file's own stated limit - no
    // headless browser here to actually drive the SSE handler's classList
    // updates (see this class's own remarks above).
    // -----------------------------------------------------------------

    [Fact]
    public void BuildPage_SlotLitCss_SplitsPartialBrightnessFromFullGlow()
    {
        var html = PanelClientEndpoint.BuildPage();

        // Partial ("regular brightness on the edges"): border/text colour
        // only, no glow - this is the base .slot.lit rule, and it must not
        // carry the box-shadow glow that belongs to full beam alone.
        Assert.Contains(".slot.lit {\n    border-color: var(--lp-lit); color: var(--lp-lit);\n  }", html, StringComparison.Ordinal);

        // Full ("high glowing edges"): a separate rule adds the glow on top
        // of the same border/text colour - never the reverse (a lit-full
        // rule that duplicates border-color/color would let partial and
        // full drift independently over time).
        // The spread (3px) is deliberate, added 2026-09-08. The original blur
        // with no spread rendered correctly but read as nothing against the
        // panel ground at device scale - proven by temporarily adding a red
        // outline to this same rule, which appeared while the glow did not.
        // Two rounds were spent hunting a client bug that never existed.
        Assert.Contains(".slot.lit-full {\n    box-shadow: 0 0 0 3px var(--lp-lit), 0 0 20px 5px var(--lp-lit);\n  }", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_ApplyLive_ReadsTheLitLevelString_NotABoolean()
    {
        var html = PanelClientEndpoint.BuildPage();

        // The 'lit' class (partial brightness) applies at both Partial and
        // Full; the extra 'lit-full' glow class applies only at Full - off
        // is neither class, the ordinary "no class at all" case.
        Assert.Contains("level === 'Partial' || level === 'Full'", html, StringComparison.Ordinal);
        Assert.Contains("classList.toggle('lit-full', level === 'Full')", html, StringComparison.Ordinal);

        // The old boolean comparison must be gone, not merely joined by the
        // new one - a leftover `=== true` would mean the level string can
        // never actually satisfy it, silently making every slot unlit.
        Assert.DoesNotContain("litByIndex.get(index) === true", html, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // The on-device editor (ref/docs/editor.md, 2026-09-07). Same limit as
    // every other client behaviour on this page: no headless browser in
    // this suite, so these are static-content pins only - presence of the
    // markup/route substitutions, not that a tap actually opens a sheet or
    // that an assign actually reaches the server. The server-side contract
    // these screens are reasoned against is covered directly by
    // SlotEditEndpointTests/ActionsEndpointTests/ServerHostBuilderTests.
    // -----------------------------------------------------------------

    [Fact]
    public void BuildPage_SubstitutesTheEditorRouteConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"`{ApiPaths.Actions}?all=", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.SlotAssign}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.SlotLabel}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.SlotClear}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_ACTIONS__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_SLOT_ASSIGN__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_SLOT_LABEL__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_SLOT_CLEAR__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rendered-output pin (not driven behaviour - there is no browser
    /// here) for drag-to-move: the move route reaches the page as a real
    /// path, and the two classes the drag paints while it is in flight
    /// exist as CSS rules rather than as class names nothing styles. A
    /// dragged button with no rule behind <c>.dragging</c> looks to a
    /// commander exactly like a gesture that did not take.
    /// </summary>
    [Fact]
    public void BuildPage_SubstitutesTheSlotMoveRoute_AndCarriesTheDragFeedbackStyles()
    {
        var html = PanelClientEndpoint.BuildPage();

        // The LITERAL path, not $"'{ApiPaths.SlotMove}'" - interpolating the
        // constant into its own assertion is an identity that stays green
        // however the route is renamed, and this page's job is to agree with
        // a route the server already published.
        Assert.Contains("'/api/panel/slot/move'", html, StringComparison.Ordinal);
        Assert.Equal("/api/panel/slot/move", ApiPaths.SlotMove);
        Assert.DoesNotContain("__API_SLOT_MOVE__", html, StringComparison.Ordinal);
        Assert.Contains(".slot.dragging {", html, StringComparison.Ordinal);
        Assert.Contains(".slot.drag-target {", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_EditorMarkup_SlotSheetActionPickerAndRenameDialog_ArePresent()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"editBtn\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheet\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheetAssign\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheetRename\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheetClear\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"actionPicker\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"actionPickerSearch\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"actionPickerShowAll\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"renameDialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"renameInput\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_RenameInput_IsATextareaNotAnInput_SoEnterAddsALineBreak_NotASubmit()
    {
        // button-naming.md: labels are up to two lines and the commander
        // picks the break with Enter - a single-line <input> would submit
        // the form on Enter instead of adding the newline.
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("<textarea id=\"renameInput\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_ClearConfirm_NeverMentionsDeletingThePage()
    {
        // "Must not be confusable with deleting a page" (ref/docs/editor.md) -
        // a source-scan style check that the confirm wording used for
        // clearing a slot doesn't borrow language that reads as removing
        // the whole page.
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("Clear this button? The page itself is not affected", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_UnboundActionRows_AreMarkedBeforeAssignment()
    {
        // "A commander must not have to place a button to find out it will
        // not work" - the picker row itself carries the unbound marker.
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("actionRow' + (a.isBound ? '' : ' unbound')", html, StringComparison.Ordinal);
        Assert.Contains(".actionRow.unbound", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_RenameCancel_NeverCallsAnEndpoint_BeforeOk()
    {
        // button-naming.md: "The assignment is not committed until OK."
        // Nothing between openRenameDialog and the form's submit handler
        // may reach the network - Cancel's own handler must never appear
        // anywhere near a fetch call.
        var html = PanelClientEndpoint.BuildPage();

        var cancelHandlerStart = html.IndexOf("el('renameCancel').addEventListener", StringComparison.Ordinal);
        Assert.True(cancelHandlerStart >= 0);
        var cancelHandlerEnd = html.IndexOf("});", cancelHandlerStart, StringComparison.Ordinal);
        var cancelHandlerBody = html[cancelHandlerStart..cancelHandlerEnd];

        Assert.DoesNotContain("fetch(", cancelHandlerBody, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // The label-fit rule (2026-09-07, ref/docs/button-naming.md's "The flat
    // cap has to go") and long-press wiring. Same limit as every other
    // client behaviour on this page: static-content pins only, no headless
    // browser to actually drive a held pointer or a rename submit.
    // -----------------------------------------------------------------

    [Fact]
    public void BuildPage_SubstitutesTheLongPressRouteConstant_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.SlotLongPress}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_SLOT_LONGPRESS__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two numbers the rename dialog's own budget check runs against
    /// must be the REAL <see cref="LayoutValidator"/> constants, not a
    /// second hand-typed pair that could drift from them - the whole point
    /// of "put the decision somewhere both can reach."
    /// </summary>
    [Fact]
    public void BuildPage_SubstitutesTheRealLabelBudgetConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"LABEL_MAX_LINES = {LayoutValidator.UserLabelMaxLines};", html, StringComparison.Ordinal);
        Assert.Contains($"LABEL_LINE_CAP = {LayoutValidator.UserLabelLineCap};", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__LABEL_MAX_LINES__", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__LABEL_LINE_CAP__", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_RenameSubmit_ChecksTheLabelBudget_BeforeClosingTheDialogOrCommitting()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("function labelFitsBudget(text)", html, StringComparison.Ordinal);
        var submitStart = html.IndexOf("el('renameForm').addEventListener('submit'", StringComparison.Ordinal);
        Assert.True(submitStart >= 0);
        var submitEnd = html.IndexOf("\nel(", submitStart + 1, StringComparison.Ordinal);
        var submitBody = html[submitStart..(submitEnd > submitStart ? submitEnd : html.Length)];

        Assert.Contains("labelFitsBudget(finalText)", submitBody, StringComparison.Ordinal);
        // The budget check must appear before the dialog is hidden and the
        // handler invoked - not merely present anywhere in the handler.
        var budgetCheckIndex = submitBody.IndexOf("labelFitsBudget(finalText)", StringComparison.Ordinal);
        var closeDialogIndex = submitBody.IndexOf("renameDialog').classList.add('hidden')", StringComparison.Ordinal);
        Assert.True(budgetCheckIndex >= 0 && closeDialogIndex > budgetCheckIndex);
    }

    /// <summary>
    /// [2026-09-09] The client's own copy of the rule has to WRAP, or the
    /// dialog refuses names the server now accepts - which is the same
    /// "accepted here, rejected there" gap this pair of implementations was
    /// put on shared constants to close, running the other way. Sliced to
    /// <c>labelFitsBudget</c>'s own body rather than the whole page: the
    /// <c>line !== line.trim()</c> clause is the one that refused the
    /// commander a ten-character name, so a single edit reinstating it
    /// inside this function has to be visible, and a page-wide needle would
    /// not see it move.
    ///
    /// Same stated limit as every other pin in this file - the function is
    /// read, not executed; there is no headless browser here. It was driven
    /// by hand under node against the twelve cases
    /// <c>LayoutValidatorTests</c> pins in C#, and agreed on all twelve.
    /// </summary>
    [Fact]
    public void BuildPage_LabelBudget_WrapsAtSpaces_AndNoLongerRefusesASurroundingSpace()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("function wrapLabel(text)", html, StringComparison.Ordinal);

        var start = html.IndexOf("function labelFitsBudget(text)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\n}", start, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.Contains("wrapLabel(text)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("line.trim()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// [2026-09-09] The macro builder's name field is a single-line
    /// <c>&lt;input&gt;</c>, and the device this product is built for has an
    /// on-screen keyboard with no Enter key - so the help text telling the
    /// commander to press Enter for a second line named a gesture that
    /// could not be made on either. Sliced to that one handler, since the
    /// word "Enter" is legitimate elsewhere on the page.
    /// </summary>
    [Fact]
    public void BuildPage_MacroNameHelp_SaysTheNameSplitsItself_NeverToPressEnter()
    {
        var html = PanelClientEndpoint.BuildPage();

        var start = html.IndexOf("el('macroNameHelp').addEventListener('click'", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\nel(", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.DoesNotContain("Press Enter", body, StringComparison.Ordinal);
        Assert.Contains("splits there by itself", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_SlotSheet_HasLongPressSetAndClearControls()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"slotSheetLongPress\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheetClearLongPress\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_LongPressPickerPick_PostsToLongPressEndpoint_NeverOpensTheRenameDialog()
    {
        var html = PanelClientEndpoint.BuildPage();

        var functionStart = html.IndexOf("function onActionPicked(action)", StringComparison.Ordinal);
        Assert.True(functionStart >= 0);
        var functionEnd = html.IndexOf("\nasync function", functionStart + 1, StringComparison.Ordinal);
        var body = html[functionStart..(functionEnd > functionStart ? functionEnd : html.Length)];

        Assert.Contains("pickerMode === 'longPress'", body, StringComparison.Ordinal);
        Assert.Contains("postSlotLongPress(editorSlotIndex, action.actionName, null)", body, StringComparison.Ordinal);

        // The long-press branch must return before ever reaching
        // openRenameDialog - proven by requiring the branch's own postSlotLongPress
        // call to appear textually before openRenameDialog in this function body.
        var longPressCallIndex = body.IndexOf("postSlotLongPress(editorSlotIndex, action.actionName, null)", StringComparison.Ordinal);
        var renameDialogIndex = body.IndexOf("openRenameDialog(", StringComparison.Ordinal);
        Assert.True(longPressCallIndex >= 0 && renameDialogIndex > longPressCallIndex);
    }

    [Fact]
    public void BuildPage_HasLongPressVisibleAffordance_DistinctFromTheDegradedMarker()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(".slot.has-long-press::before", html, StringComparison.Ordinal);
        Assert.Contains("hasLongPress ? ' has-long-press' : ''", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HasTheLongPressGesture_AndItNeverFiresBothActionsAtOnce()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("function wireSlotGesture(btn, slot)", html, StringComparison.Ordinal);
        Assert.Contains("function onSlotLongPress(slot)", html, StringComparison.Ordinal);
        Assert.Contains("LONG_PRESS_MS", html, StringComparison.Ordinal);
        Assert.Contains("MOVE_CANCEL_PX", html, StringComparison.Ordinal);

        // The pointerup handler must gate onSlotTap behind "not a long press
        // and not a drag" - proven by requiring firedLongPress/moved to both
        // appear in the same conditional guarding onSlotTap's call.
        var gestureStart = html.IndexOf("function wireSlotGesture(btn, slot)", StringComparison.Ordinal);
        var gestureEnd = html.IndexOf("\nfunction ", gestureStart + 1, StringComparison.Ordinal);
        var gestureBody = html[gestureStart..(gestureEnd > gestureStart ? gestureEnd : html.Length)];
        Assert.Contains("!moved && !firedLongPress", gestureBody, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_PressBody_CarriesLongPressFlag_WhenFiringTheLongPressAction()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("slot: slot.index, longPress: true", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The source-scan pins in <see cref="PanelClientSourceGuardTests"/> read
    /// <c>PanelClientEndpoint.cs</c> off disk; this one proves the gate
    /// actually reaches the page a device is served, rather than sitting in
    /// the file behind something that never renders.
    /// </summary>
    [Fact]
    public void BuildPage_CarriesTheNeverSwitchUnderAMovingThumbGate()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("function requestPageSwitch(index)", html, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pointercancel', releasePointer);", html, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // The macro builder (ref/docs/macro-builder.md), 2026-09-09.
    //
    // Every pin below reads BuildPage()'s output as text. NONE of them
    // drives the builder: there is no headless browser here, so nothing in
    // this file proves a step actually gets added when the button is
    // tapped. What they hold is the wiring, the route substitution, and -
    // the part this feature is mostly made of - the exact copy, by content
    // rather than by being non-empty.
    // -----------------------------------------------------------------

    [Fact]
    public void BuildPage_SubstitutesTheFourMacroRouteConstants_AndLeavesNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"'{ApiPaths.Macros}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.MacrosCopy}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.MacrosDelete}'", html, StringComparison.Ordinal);
        Assert.Contains($"'{ApiPaths.MacrosVocabulary}'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_MACROS", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The builder needs this project's one sentence for an action with no
    /// key in Elite while editing a step no server response has ever seen,
    /// so it is substituted from <see cref="PressEndpoint"/>'s own constant
    /// rather than typed a second time - which is what keeps
    /// <c>ref/docs/macro-builder.md</c>'s question 5 answer ("the third
    /// wording is structurally impossible") true now that a second surface
    /// says it.
    ///
    /// <b>Pinned with its surrounding double quotes on purpose.</b> That
    /// sentence contains an apostrophe ("the game's Controls options"), so
    /// a single-quoted JavaScript literal ends early and takes the entire
    /// client script down with a syntax error - on a page nothing in this
    /// suite executes. This was a real defect during the build, caught by a
    /// parser run by hand; the quoting is the half of it a test can hold.
    /// </summary>
    [Fact]
    public void BuildPage_SubstitutesTheRealUnboundAdvice_InAQuoteThatSurvivesItsApostrophe()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"NOT_BOUND_ADVICE = \"{PressEndpoint.NotBoundInEliteAdvice}\";", html, StringComparison.Ordinal);
        Assert.DoesNotContain("__NOT_BOUND_ADVICE__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The builder pre-fills a <c>waitForEdge</c> step's timeout, and shows
    /// it in the duration estimate for a step that names none. Both have to
    /// be the number the runner would really have used - a hand-typed 20000
    /// here would go on claiming 20 seconds after the measured default
    /// moved.
    /// </summary>
    [Fact]
    public void BuildPage_SubstitutesTheRealWaitForEdgeDefaultTimeout()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            $"EDGE_TIMEOUT_DEFAULT_MS = {(int)MacroTimingDefaults.DefaultWaitForEdgeTimeout.TotalMilliseconds};",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__EDGE_TIMEOUT_DEFAULT_MS__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The sweep that keeps the no-tier ruling honest on the client
    /// side.</b> <c>MacrosEndpointTests</c> already proves the server
    /// <em>offers</em> every step kind in the grammar; this proves the
    /// client can <em>build and read back</em> each one. A grammar addition
    /// the builder forgets would otherwise be offered by the picker (which
    /// reads the server's list) and then produce nothing - or produce a
    /// step whose read-back says "Unrecognised step".
    /// </summary>
    [Fact]
    public void BuildPage_KnowsHowToBuildAndReadBackEveryStepKindInTheGrammar()
    {
        var html = PanelClientEndpoint.BuildPage();

        var kindListStart = html.IndexOf("function stepKindOf(step)", StringComparison.Ordinal);
        Assert.True(kindListStart >= 0);
        var kindListEnd = html.IndexOf("\nfunction ", kindListStart + 1, StringComparison.Ordinal);
        var kindListBody = html[kindListStart..(kindListEnd > kindListStart ? kindListEnd : html.Length)];

        var newStepStart = html.IndexOf("function newStepOfKind(kind)", StringComparison.Ordinal);
        Assert.True(newStepStart >= 0);
        var newStepEnd = html.IndexOf("\nfunction ", newStepStart + 1, StringComparison.Ordinal);
        var newStepBody = html[newStepStart..(newStepEnd > newStepStart ? newStepEnd : html.Length)];

        var describeStart = html.IndexOf("function describeStep(step)", StringComparison.Ordinal);
        Assert.True(describeStart >= 0);
        var describeEnd = html.IndexOf("\n// The chord,", describeStart + 1, StringComparison.Ordinal);
        var describeBody = html[describeStart..(describeEnd > describeStart ? describeEnd : html.Length)];

        foreach (var kind in MacroDefinition.StepKindKeys)
        {
            Assert.Contains($"'{kind}'", kindListBody, StringComparison.Ordinal);
            Assert.Contains($"case '{kind}':", newStepBody, StringComparison.Ordinal);
            Assert.Contains($"case '{kind}':", describeBody, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <c>gotoLeftPanelTab</c> is not in <c>macroVocabulary.stepKinds</c>
    /// (deliberately not offered by the "add a step" picker -
    /// <c>macro-builder.md</c>, question 1, superseded 2026-09-12), so the
    /// step editor's "about" text - normally read off that vocabulary row -
    /// would go blank for a macro that already has one of these steps (a
    /// shipped macro, or a copy of one). The editor must carry its own
    /// fallback text for exactly this one kind rather than silently losing
    /// the description.
    /// </summary>
    [Fact]
    public void BuildPage_StepEditor_HasAFallbackAboutTextForGotoLeftPanelTab_SinceItIsNotInVocabulary()
    {
        var html = PanelClientEndpoint.BuildPage();

        var renderStart = html.IndexOf("function renderStepEditor()", StringComparison.Ordinal);
        Assert.True(renderStart >= 0);
        var renderEnd = html.IndexOf("\nfunction ", renderStart + 1, StringComparison.Ordinal);
        var renderBody = html[renderStart..(renderEnd > renderStart ? renderEnd : html.Length)];

        // [SUPERSEDED 2026-09-17] This pinned the whole expression verbatim:
        //   "kind === 'gotoLeftPanelTab' ? GOTO_LEFT_PANEL_TAB_ABOUT : ''"
        // A second not-offered kind (branch, at the time) made that
        // expression a nested ternary, so the needle could no longer appear -
        // a shape guard keyed to the exact shape it guards. Replaced with the
        // claim that actually matters and survives another kind being added:
        // every kind the picker does not offer has its own named fallback
        // constant, that constant is referenced from renderStepEditor, and it
        // is not empty text.
        //
        // [SUPERSEDED AGAIN 2026-09-17] branch came OFF NotOfferedStepKinds
        // once a real arm editor existed (MacrosEndpointTests.
        // BuildVocabulary_OffersBranch_NowThatAnArmEditorExists), so the
        // "Assert.Contains(kind, NotOfferedStepKinds)" premise this loop
        // built on no longer holds for branch. gotoLeftPanelTab is unchanged
        // and keeps that assertion; BRANCH_ABOUT is checked on its own below
        // instead, as what it actually still is - a pre-load-race fallback
        // (macroVocabulary not yet fetched), not a not-offered-kind fallback.
        Assert.Contains("gotoLeftPanelTab", MacrosEndpoint.NotOfferedStepKinds);
        Assert.Contains("kind === 'gotoLeftPanelTab' ? GOTO_LEFT_PANEL_TAB_ABOUT", renderBody, StringComparison.Ordinal);

        var gotoDeclaration = html.IndexOf("const GOTO_LEFT_PANEL_TAB_ABOUT = '", StringComparison.Ordinal);
        Assert.True(gotoDeclaration >= 0, "GOTO_LEFT_PANEL_TAB_ABOUT is referenced but never declared.");
        var gotoTextStart = gotoDeclaration + "const GOTO_LEFT_PANEL_TAB_ABOUT = '".Length;
        Assert.NotEqual('\'', html[gotoTextStart]);

        // BRANCH_ABOUT: still referenced from renderStepEditor's own about-
        // text fallback (for the moment macroVocabulary has not loaded yet),
        // and still not empty. Not gated on NotOfferedStepKinds any more,
        // since branch is offered now.
        Assert.Contains("kind === 'branch' ? BRANCH_ABOUT", renderBody, StringComparison.Ordinal);
        var branchDeclaration = html.IndexOf("const BRANCH_ABOUT = '", StringComparison.Ordinal);
        Assert.True(branchDeclaration >= 0, "BRANCH_ABOUT is referenced but never declared.");
        var branchTextStart = branchDeclaration + "const BRANCH_ABOUT = '".Length;
        Assert.NotEqual('\'', html[branchTextStart]);
    }

    /// <summary>
    /// The one place a step's default is a value from a real enum rather
    /// than a number - a new macro's tab step starts on
    /// <see cref="PanelTab.Navigation"/>, the tab the panel returns to on
    /// its own. Spelled wrong it would be rejected at save with a message
    /// about an unknown tab, for a step the commander never typed.
    /// </summary>
    [Fact]
    public void BuildPage_ANewTabStepDefaultsToARealPanelTab()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains($"{{ gotoLeftPanelTab: '{nameof(PanelTab.Navigation)}' }}", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_HasTheMacrosPaneAndTheBuildersFourScreens()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("data-pane=\"paneMacros\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"macroList\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"macroBuilder\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"stepKindPicker\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"stepEditor\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"tokenPicker\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Basic instructions on the screen itself, not in a doc nobody
    /// opens" - the commander's own request, pinned by content. A pin that
    /// only checked the paragraph was non-empty would stay green if it were
    /// reduced to a caption, which is exactly the failure being guarded.
    /// </summary>
    [Fact]
    public void BuildPage_MacrosPane_SaysWhatAMacroIsAndWhereToPutOne()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "A macro is a short list of things LunaPanel presses for you, one after another - open a panel, step down three rows, select. Build one here, then put it on a button with Edit, on the panel itself.",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPage_StepKindPicker_SaysStepsHappenInOrder_AndWhatTheGatedGroupIsFor()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "Steps happen in order, top to bottom. Most macros are just presses and waits; the ones further down wait for the game itself to be ready before carrying on.",
            html,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The tooltips, pinned by content for the reason
    /// <c>ref/docs/macro-timing.md</c> gives for its own: a tooltip that
    /// defines the term is filler, and the thing worth holding is that each
    /// of these names a consequence a commander would recognise. The
    /// control tooltip in particular carries the promise the whole
    /// intent-never-resolution rule exists to make true.
    /// </summary>
    [Theory]
    [InlineData("How many times in a row this control is pressed. Elite needs a gap between presses to see them as separate - LunaPanel leaves that gap for you, which is why more presses take longer.")]
    [InlineData("How long to keep waiting before giving up. If it runs out, the macro stops there and the steps after it do not happen - nothing is sent by mistake.")]
    [InlineData("If Elite writes one of these instead, stop waiting immediately rather than sitting there until the time runs out. This is how a refused docking request ends in a second rather than twenty.")]
    [InlineData("LunaPanel keeps track of which tab the left panel is on, so this presses only as far as it needs to - often not at all. It is the safest way to reach a tab; walking there with plain presses is what goes wrong when the panel is not where you assumed.")]
    public void BuildPage_StepEditorTooltips_NameTheConsequence_NotTheTerm(string expectedCopy)
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(expectedCopy, html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The promise a commander is being asked to believe: a step naming an
    /// action they have not mapped yet is not a mistake, and mapping it in
    /// Elite later needs no edit here. It is real - a macro stores the
    /// action name and resolves it on every run - which is why the tooltip
    /// is allowed to say it.
    /// </summary>
    [Fact]
    public void BuildPage_ControlTooltip_PromisesThatMappingItLaterNeedsNoEditHere()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "LunaPanel stores the control, never a key - so mapping it in Elite later makes this step start working with nothing to change here.",
            html,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The measured hazard (live-checks LC17/LC18), stated as a note on the
    /// builder screen rather than as a gate. Pinned by content because the
    /// whole value is that it names what actually happens - a press landing
    /// in the wrong place still succeeds - rather than saying "be careful".
    /// </summary>
    [Fact]
    public void BuildPage_WarnsThatAMacroWhichChecksNothingStillLooksLikeItWorked_AndDoesNotBlockIt()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "If a panel is already open when it runs, the presses land in that panel instead - and they will still look like they worked.",
            html,
            StringComparison.Ordinal);

        // Nothing in the notes may disable Save. The never-gate rule is
        // about refusing to start, and a note that turned into a block is
        // exactly the back door ref/docs/macro-builder.md forbids.
        var notesStart = html.IndexOf("function renderMacroNotes()", StringComparison.Ordinal);
        Assert.True(notesStart >= 0);
        var notesEnd = html.IndexOf("\nel('macroBuilderClose')", notesStart + 1, StringComparison.Ordinal);
        var notesBody = html[notesStart..(notesEnd > notesStart ? notesEnd : html.Length)];
        Assert.DoesNotContain("disabled", notesBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The duration estimate must be computed from the timing the commander
    /// actually has, read back from <c>GET /api/macro-timing</c> - never a
    /// constant, for the reason <c>MacroRunner</c> refuses to use a timer
    /// for its own lock. Pinned as the estimate reading the two variables
    /// the fetch populates, and as the sentence naming them on screen so
    /// the number is explainable rather than mysterious.
    /// </summary>
    [Fact]
    public void BuildPage_DurationEstimate_ReadsTheConfiguredTiming_NeverAConstant()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("macroTimingHoldMs = timing.holdMs;", html, StringComparison.Ordinal);
        Assert.Contains("macroTimingGapMs = timing.interPressGapMs;", html, StringComparison.Ordinal);

        var start = html.IndexOf("function renderMacroEstimate()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\n// Notes, never refusals", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.Contains("stepDurationMs(step, macroTimingHoldMs, macroTimingGapMs, false)", body, StringComparison.Ordinal);
        Assert.Contains("stepDurationMs(step, macroTimingHoldMs, macroTimingGapMs, true)", body, StringComparison.Ordinal);
        Assert.Contains("at your current timing (", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The builder edits <c>GET /api/macros</c>'s <c>definition</c> - the
    /// macro as the grammar stores it - and never its <c>steps[]</c>, which
    /// is a display projection carrying no <c>repeat</c>, no timeout and no
    /// condition tokens. Reading the projection would silently drop every
    /// number in a macro the moment somebody opened it, which is the shape
    /// of bug that looks like it worked.
    /// </summary>
    [Fact]
    public void BuildPage_OpeningAMacroToEdit_ReadsTheStoredDefinition_NotTheDisplayProjection()
    {
        var html = PanelClientEndpoint.BuildPage();

        var start = html.IndexOf("function openMacroBuilder(macro)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\nfunction renderMacroDraft()", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.Contains("macro.definition.steps", body, StringComparison.Ordinal);
        Assert.DoesNotContain("macro.steps", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shipped macro opens read only and offers a copy, never an edit -
    /// <c>ref/docs/macro-builder.md</c>'s question 3, whose whole point is
    /// that an override of a shipped id shadows a later release's fix
    /// invisibly.
    ///
    /// [2026-09-10, superseded in two needles, and driven against the PC's
    /// own page rather than the default one.] Before-values:
    /// <c>"macroDraftReadOnly = !!(macro &amp;&amp; macro.isShipped);"</c> and
    /// <c>"el('macroCopy').classList.toggle('hidden', !macroDraftReadOnly);"</c>.
    /// Both changed because read-only now has two reasons rather than one
    /// (<c>ref/docs/macro-builder.md</c>'s "Authoring moved to the PC"): a
    /// shipped macro is read-only everywhere, and every macro is read-only
    /// on a device. The shipped-macro claim this test exists for is
    /// unchanged and is now asserted where it is the <em>only</em> reason in
    /// play - <see cref="PanelClientEndpoint.BuildPage(bool)"/> with
    /// authoring on - so a page that had simply stopped offering the copy
    /// button to anybody could not pass it.
    /// </summary>
    [Fact]
    public void BuildPage_AShippedMacro_OpensReadOnly_AndOffersACopyRatherThanAnEdit()
    {
        var html = PanelClientEndpoint.BuildPage(canAuthorMacros: true);

        Assert.Contains("if (macro && macro.isShipped) return 'shipped';", html, StringComparison.Ordinal);
        Assert.Contains("el('macroSave').classList.toggle('hidden', macroDraftReadOnly);", html, StringComparison.Ordinal);
        Assert.Contains("el('macroCopy').classList.toggle('hidden', !macroDraftReadOnly || !CAN_AUTHOR_MACROS);", html, StringComparison.Ordinal);
        // The id posted for a shipped macro is null, so a save can never
        // address a shipped id even if one reached the save path.
        Assert.Contains("id: macro.isShipped ? null : macro.id,", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// [2026-09-12] The staleness line (<c>ref/docs/macro-builder.md</c>,
    /// question 3), previously recorded there as NOT built: opening a macro
    /// reads whether it is stale and what to call its source straight off
    /// the row <c>GET /api/macros</c> already carries this on, rather than
    /// recomputing anything client-side - the same discipline every other
    /// piece of server-decided state on this row already follows
    /// (<c>isDegraded</c>, <c>isShipped</c>).
    /// </summary>
    [Fact]
    public void BuildPage_OpeningAMacro_ReadsItsStalenessFromTheRow()
    {
        var html = PanelClientEndpoint.BuildPage();

        var start = html.IndexOf("function openMacroBuilder(macro)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\nfunction renderMacroDraft()", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.Contains("macroDraftIsStale = !!(macro && macro.isStale);", body, StringComparison.Ordinal);
        Assert.Contains("macroDraftSourceName = macro ? (macro.sourceMacroName || null) : null;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The note itself, and its second wording for the case a source's own
    /// row is gone entirely: never a refusal, and the same "this copy still
    /// does what it did" reassurance either way - a stale copy has not
    /// stopped working, only its source has moved on.
    /// </summary>
    [Fact]
    public void BuildPage_MacroNotes_NamesTheStaleSource_OrSaysItIsGone()
    {
        var html = PanelClientEndpoint.BuildPage();

        var start = html.IndexOf("function renderMacroNotes()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\nel('macroBuilderClose')", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.Contains("if (macroDraftIsStale) {", body, StringComparison.Ordinal);
        Assert.Contains(
            "'Copied from ' + macroDraftSourceName + ', which has changed since. This macro still does what it did when you copied it.'",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "'Copied from a macro that no longer exists in this build. This macro still does what it did when you copied it.'",
            body,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Marked in the Macros pane's own list too, before a commander even
    /// opens the builder - the same "before it is placed on a button"
    /// discipline the degraded marker already follows on this row.
    /// </summary>
    [Fact]
    public void BuildPage_MacroList_MarksAStaleCopyInItsMetaLine()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(
            "if (macro.isStale) parts.push('copied from a macro that has since changed');",
            html,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The action picker's MACROS group - the gap <c>ref/docs/editor.md</c>
    /// has named as missing since the editor shipped. A macro row must
    /// commit through the <c>macro</c> field, not the <c>action</c> one:
    /// posting a macro id as an action name would save a layout naming an
    /// action that does not exist, and the slot would render
    /// <c>UnknownAction</c> rather than running anything.
    /// </summary>
    [Fact]
    public void BuildPage_ActionPicker_OffersMacros_AndAssignsThemThroughTheMacroField()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("function appendMacroPickerGroup(list, macros)", html, StringComparison.Ordinal);
        Assert.Contains("label.textContent = 'MACROS';", html, StringComparison.Ordinal);

        var start = html.IndexOf("function onMacroPicked(macro)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\n// Exactly one of", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.Contains("postSlotAssign(editorSlotIndex, null, macro.id, label)", body, StringComparison.Ordinal);
        Assert.Contains("postSlotLongPress(editorSlotIndex, null, macro.id)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A macro is never offered while filling in a macro's own step. A
    /// macro cannot call a macro - it is not in the grammar, and a step
    /// naming another id would need cycle detection nothing has built
    /// (<c>ref/docs/macro-builder.md</c>'s "Not decided"). The picker is
    /// one screen serving three verbs, so this is the arm that has to be
    /// right, not merely the string.
    /// </summary>
    [Fact]
    public void BuildPage_ActionPicker_NeverOffersAMacroWhileFillingInAMacroStep()
    {
        var html = PanelClientEndpoint.BuildPage();

        // The whole expression, not just its first line: a bare "? []"
        // needle would match almost anywhere and prove nothing about which
        // arm this picker actually takes. Line endings normalised so the
        // pin holds the code rather than the source file's newline style.
        Assert.Contains(
            "const macroMatches = pickerMode === 'macroStep'\n    ? []\n    : (q ? macroList",
            html.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The rough edge <c>ref/docs/editor.md</c> named: the latch toggle was
    /// offered on a macro slot because <c>GET /api/panel</c> could not tell
    /// an action slot from a macro one, and the commander found out by
    /// tapping it and reading a refusal.
    ///
    /// [2026-09-09] Supersedes nothing in this file - the old behaviour was
    /// only ever pinned as "offered for any occupied slot" in
    /// <c>ref/docs/editor.md</c>'s prose, not by a test. The server-side
    /// refusal is untouched and still enforces it; this pin is about the
    /// button not being offered in the first place.
    /// </summary>
    [Fact]
    public void BuildPage_SlotSheet_DoesNotOfferTheLatchToggleOnAMacroSlot()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("const isMacroSlot = !!(slot && slot.macroId);", html, StringComparison.Ordinal);
        // [2026-09-16] SUPERSEDED by folders - before-value:
        // "el('slotSheetLatch').classList.toggle('hidden', !slot || isMacroSlot);"
        // A folder has no key at all, so it joined the same guard as a third
        // disjunct. The CLAIM is unchanged and still bites: a macro slot is
        // still never offered the latch toggle.
        Assert.Contains("el('slotSheetLatch').classList.toggle('hidden', !slot || isMacroSlot || isFolderSlot);", html, StringComparison.Ordinal);
        // And the way in to the macro that replaced it.
        Assert.Contains("el('slotSheetEditMacro').classList.toggle('hidden', !isMacroSlot);", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The token picker's whole reason for existing: a plain name first, a
    /// sentence under it, and the raw token present but LAST - "available,
    /// never the first thing anyone sees", which is the answer this
    /// dispatch gave to <c>ref/docs/macro-builder.md</c>'s "how the
    /// vocabulary picker glosses a flag".
    ///
    /// <b>Rewritten 2026-09-09 after it failed to bite.</b> The first
    /// version compared the source positions of three specific assignment
    /// lines, and a mutation that inserted a FOURTH element - the raw token
    /// again, at the top of the row - left all three in the same relative
    /// order and the test green. That is a positional assertion over source
    /// order, which stops meaning what its name says the moment anything is
    /// added around it. This version sweeps every part the row builds, in
    /// order, and pins the whole sequence: an inserted part fails, a
    /// dropped part fails, and a reordered part fails.
    /// </summary>
    [Fact]
    public void BuildPage_TokenPickerRow_ShowsPlainNameAndMeaningAndTheRawTokenLast()
    {
        var html = PanelClientEndpoint.BuildPage();

        var start = html.IndexOf("function renderTokenPicker()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("\nel('tokenPickerClose')", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        var parts = System.Text.RegularExpressions.Regex
            .Matches(body, @"\.className = '(tokenRow\w+)';")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(
            new[] { "tokenRowLabel", "tokenRowMeaning", "tokenRowConfidence", "tokenRowToken" },
            parts);
    }

    /// <summary>
    /// Low-confidence flags are offered like any other and marked, never
    /// withheld - withholding them would be the never-gate rule broken in
    /// the picker instead of the runner. Pinned as the picker having no
    /// filter on confidence at all, plus the two markings it does show.
    /// </summary>
    [Fact]
    public void BuildPage_TokenPicker_MarksLowConfidenceFlags_AndFiltersNoneOut()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("Reported by the community, not in Frontier", html, StringComparison.Ordinal);
        Assert.Contains("One source only - treat this one as a guess.", html, StringComparison.Ordinal);

        var start = html.IndexOf("function renderTokenPicker()", StringComparison.Ordinal);
        var end = html.IndexOf("\nel('tokenPickerClose')", start + 1, StringComparison.Ordinal);
        var body = html[start..end];

        // Every flag is pushed; nothing anywhere skips one on confidence.
        Assert.Contains("for (const flag of macroVocabulary.flags) {", body, StringComparison.Ordinal);
        Assert.DoesNotContain("continue", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Negation is a status-condition idea. A journal event either arrived
    /// or did not, and <c>EdgeCondition.Parse</c> has no <c>!</c> form at
    /// all - offering the toggle there would produce a token the parser
    /// rejects at save, from a control that looked like it belonged.
    /// </summary>
    [Fact]
    public void BuildPage_TokenPicker_OffersNegationForStatusConditionsOnly_NeverForJournalEvents()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("el('tokenPickerNotRow').classList.toggle('hidden', mode === 'journal');", html, StringComparison.Ordinal);
        Assert.Contains("const negate = tokenPickerMode !== 'journal' && el('tokenPickerNot').checked;", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing the builder does reaches the network until Save, which is
    /// what makes closing a sheet safe - the same principle
    /// <c>#renameCancel</c> already holds for the rename dialog, checked
    /// the same way: by reading the handler's own body.
    /// </summary>
    [Fact]
    public void BuildPage_ClosingTheStepEditorOrTokenPicker_NeverReachesTheNetwork()
    {
        var html = PanelClientEndpoint.BuildPage();

        var start = html.IndexOf("el('stepEditorClose').addEventListener", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = html.IndexOf("// ---", start + 1, StringComparison.Ordinal);
        var body = html[start..(end > start ? end : html.Length)];

        Assert.DoesNotContain("fetch(", body, StringComparison.Ordinal);
    }
}
