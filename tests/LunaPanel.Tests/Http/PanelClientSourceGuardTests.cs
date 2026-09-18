namespace LunaPanel.Tests.Http;

/// <summary>
/// Source-scan pin (repo root located by walking up from the test assembly
/// to <c>LunaPanel.sln</c>, same idiom as <c>CoreNeedleGuardTests</c> and
/// <c>DeviceRegistryTests</c>' <c>FixedTimeEquals</c> pin) closing
/// <c>ref/docs/panels-and-pages.md</c>'s "close the headerStrip duplication
/// BY CONSTRUCTION" task by reading <c>PanelClientEndpoint.cs</c>'s own text.
///
/// <b>Why a source-scan pin rather than a behavioural one.</b>
/// <see cref="PanelClientEndpointTests.BuildPage_NoLongerRestatesHeaderStripAsAJavaScriptLiteral_ConsumesTheServersFieldInstead"/>
/// already proves the literal is gone from <em>this build's</em> output. It
/// cannot prove the literal cannot come back - a future edit could
/// reintroduce <c>isPhone() ? 40 : 48</c> under a new function name that
/// happens to produce the same page text in every case that test's fixtures
/// exercise. Reading the raw source text is what actually forecloses that,
/// the same way <c>DeviceRegistryTests</c>' own remarks explain for
/// <c>FixedTimeEquals</c>.
///
/// <b>The two defects this exists to stop a third occurrence of</b> (both
/// recorded in <c>tests/notes/live-checks.md</c>): <b>LC9</b> - the
/// calibration page's own chrome once exceeded the allowance the estimator
/// assumed, pushing the bottom row off screen - and its opposite, a page
/// whose chrome undershot the allowance, leaving buttons flush to the screen
/// edge with nothing explaining the gap. Both happened because the
/// allowance was spelled twice (once in <c>CellSizeEstimator.cs</c>, once as
/// a hand-copied JavaScript literal) and the two copies drifted. A tab row
/// was about to be the third occurrence (<c>ref/docs/panels-and-pages.md</c>'s
/// "Tabs": "If this happens a third time, the lesson is not 'be more
/// careful': it is that the client should ask the server for its allowances
/// rather than restating them in JavaScript").
/// </summary>
public class PanelClientSourceGuardTests
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

    private static string ReadPanelClientSource()
    {
        var sourcePath = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Server", "Http", "PanelClientEndpoint.cs");
        Assert.True(File.Exists(sourcePath), $"Expected {sourcePath} to exist.");
        return File.ReadAllText(sourcePath);
    }

    /// <summary>
    /// Extracts a single CSS rule's declaration body (everything between the
    /// first <c>{</c> after <paramref name="selector"/> and its matching
    /// <c>}</c>), so a test can assert on what the rule actually declares
    /// rather than merely that the selector string appears somewhere in the
    /// file (which a comment or an unrelated rule could also satisfy).
    /// </summary>
    /// <summary>
    /// Extracts a single JavaScript function's body by brace-matching from
    /// the first <c>{</c> after its declaration. Scoping every assertion
    /// below to one function body is what stops them being vacuous: a needle
    /// searched for across this whole 2,300-line file would be satisfied by a
    /// comment, an unrelated handler, or the very code the guard exists to
    /// forbid sitting somewhere else.
    /// </summary>
    private static string ExtractJsFunctionBody(string content, string declaration)
    {
        var start = content.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{declaration}' to appear in the page source.");

        var open = content.IndexOf('{', start + declaration.Length);
        Assert.True(open >= 0, $"Expected a body after '{declaration}'.");

        var depth = 0;
        for (var i = open; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content.Substring(open, i - open + 1);
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after '{declaration}'.");
    }

    private static string ExtractCssRuleBody(string content, string selector)
    {
        var selectorIndex = content.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(selectorIndex >= 0, $"Expected selector '{selector}' to appear in the page source.");

        var braceOpen = content.IndexOf('{', selectorIndex);
        var braceClose = content.IndexOf('}', braceOpen);
        return content.Substring(braceOpen, braceClose - braceOpen);
    }

    /// <summary>
    /// The specific literal patterns the two removed functions used
    /// (<c>isPhone() ? 40 : 48</c> for headerStrip, <c>isPhone() ? 12 : 16</c>
    /// for framePadding) must never reappear anywhere in this file's
    /// JavaScript, under any function name - not just absent from the
    /// current <c>headerStripFor</c>/<c>framePadFor</c> names, which a
    /// rename alone could dodge while reintroducing the exact duplication
    /// this pin exists to prevent.
    /// </summary>
    [Fact]
    public void PanelClientSource_NeverRestatesHeaderStripOrFramePaddingLiteralsInJavaScript()
    {
        var content = ReadPanelClientSource();

        Assert.DoesNotContain("isPhone() ? 40 : 48", content, StringComparison.Ordinal);
        Assert.DoesNotContain("isPhone() ? 12 : 16", content, StringComparison.Ordinal);
        Assert.DoesNotContain("headerStripFor", content, StringComparison.Ordinal);
        Assert.DoesNotContain("framePadFor", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The positive half of the same guard: the client must actually be
    /// reading the server's own allowance fields, not simply missing the
    /// literal by coincidence (e.g. a rewrite that dropped the header row
    /// entirely rather than sourcing its height from the response).
    /// </summary>
    [Fact]
    public void PanelClientSource_ConsumesTheServersAllowanceFields()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("data.headerStrip", content, StringComparison.Ordinal);
        Assert.Contains("data.framePadding", content, StringComparison.Ordinal);
        Assert.Contains("data.tabStripHeight", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gear button reported grey/white on a black panel background
    /// (2026-09-07, "the settings icon in the panels is grey / white, it
    /// does not match the black color background"). Cause A of two:
    /// <c>#gearBtn</c> used to declare only <c>padding</c>/<c>font-size</c>/
    /// <c>line-height</c>, with no <c>color</c>, <c>background</c> or
    /// <c>border</c> at all, so it fell back to the browser's own default
    /// button chrome instead of the panel's theme - every comparable control
    /// (<c>#pairSubmit</c>, <c>#fsBtn</c>, <c>.sheetHeader button</c>) was
    /// already styled. It now shares the themed rule <c>#pairSubmit</c>/
    /// <c>#fsBtn</c> already use, rather than standing alone unstyled.
    /// </summary>
    [Fact]
    public void PanelClientSource_GearButtonSharesThemedStylingWithItsSiblingControls()
    {
        var content = ReadPanelClientSource();

        var rule = ExtractCssRuleBody(content, "#pairSubmit, #fsBtn, #gearBtn");

        Assert.Contains("color: var(--lp-text)", rule, StringComparison.Ordinal);
        Assert.Contains("background: transparent", rule, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid var(--lp-frame)", rule, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cause B of two, and the one that actually matters - styling
    /// <c>#gearBtn</c> alone (the pin above) is not sufficient. The gear
    /// glyph is <c>&amp;#9881;</c> (U+2699 GEAR), which Android and several
    /// other platforms render with the colour emoji font by default; an
    /// emoji-presentation glyph ignores CSS <c>color</c> entirely, so a
    /// themed button still shows a stubbornly grey gear. U+FE0E (the text
    /// variation selector immediately after the gear codepoint) forces text
    /// presentation so the glyph is drawn as text and actually inherits
    /// <c>color</c>.
    ///
    /// <b>This character looks like a stray invisible one and is not.</b> A
    /// later "tidy-up" that deletes it as noise would silently reinstate the
    /// reported bug (grey gear on a dark panel) with no other symptom to
    /// catch it - which is exactly why this needs a pin rather than relying
    /// on it being noticed by eye. Do not remove it.
    /// </summary>
    [Fact]
    public void PanelClientSource_GearGlyphForcesTextPresentation_NotColorEmoji()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("&#9881;&#xFE0E;", content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Automatic vessel-context page switching (ref/docs/vessel-context.md).
    //
    // There is no headless browser in this suite, so none of the JavaScript
    // below is DRIVEN by any test - see PanelClientEndpointTests' own "What
    // this file does NOT cover". These are source-scan pins, and they are
    // scoped to individual function bodies precisely so each one CAN fail to
    // a single realistic edit rather than being satisfied by the file being
    // large.
    // ------------------------------------------------------------------

    /// <summary>
    /// <b>The rule most likely to be quietly dropped for simplicity, and the
    /// one that protects a commander from firing the wrong control.</b> The
    /// live channel's handler must ROUTE a switch through
    /// <c>requestPageSwitch</c> - which holds it while a pointer is down -
    /// and must never call <c>applyPageSwitch</c> itself. Calling apply
    /// directly is a one-word edit that looks like a simplification, keeps
    /// every other pin in this suite green, and reintroduces a page changing
    /// under a thumb already moving.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheLiveHandler_RequestsAPageSwitch_NeverAppliesOneDirectly()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        Assert.Contains("requestPageSwitch(state.switchToPage)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("applyPageSwitch(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate itself: a switch arriving while any pointer is down is HELD,
    /// not applied and not dropped.
    /// </summary>
    [Fact]
    public void PanelClientSource_RequestPageSwitch_HoldsTheSwitchWhileAPointerIsDown()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function requestPageSwitch(index)");

        Assert.Contains("activePointers > 0", body, StringComparison.Ordinal);
        Assert.Contains("pendingPageSwitch = index", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half - a held switch that is never released is a switch
    /// silently dropped, which reads as "the panel ignored me" rather than as
    /// a bug.
    /// </summary>
    [Fact]
    public void PanelClientSource_ReleasingTheLastPointer_AppliesAHeldSwitch()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function releasePointer()");

        Assert.Contains("activePointers--", body, StringComparison.Ordinal);
        Assert.Contains("applyPageSwitch(target)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The listener registrations are pinned verbatim, capture flag and
    /// all.</b> The ordering guarantee this feature rests on is that the
    /// pressed button's own <c>pointerup</c> handler (which fires the press
    /// and reads <c>currentPage</c>) runs BEFORE the window-level release
    /// that applies a held switch. That holds only in the bubble phase:
    /// adding a third <c>true</c>/<c>{ capture: true }</c> argument, or
    /// moving the listener onto the grid, would invert it and send the press
    /// to the new page's slot of the same index - a wrong-control press, with
    /// nothing else in the suite to notice. A verbatim match is the assertion
    /// that a capture flag cannot be added without reddening this.
    /// </summary>
    [Fact]
    public void PanelClientSource_PointerTrackingIsOnWindow_InTheBubblePhase()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("window.addEventListener('pointerdown', () => { activePointers++; });", content, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pointerup', releasePointer);", content, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('pointercancel', releasePointer);", content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // A rebind reaching an already-open device (BindsFileWatcher, via the
    // live channel's bindingsChanged edge - ref/docs/bindings-source.md).
    // Same "no headless browser, these are source-scan pins" caveat as the
    // vessel-context block above, and the same reason each is scoped to one
    // function body.
    // ------------------------------------------------------------------

    /// <summary>
    /// The live handler must ROUTE a bindings-changed signal through
    /// <c>requestBindingsRefetch</c> - which holds it while a pointer is
    /// down, exactly like <c>requestPageSwitch</c> does for a page switch -
    /// and must never call <c>loadPanel()</c> itself. Calling it directly is
    /// the one-word "simplification" that would reintroduce a re-fetch
    /// yanking a button out from under a thumb already moving.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheLiveHandler_RequestsABindingsRefetch_NeverCallsLoadPanelDirectly()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        // The exact statement, not merely the bare identifier - a comment
        // mentioning "requestBindingsRefetch()" would satisfy a bare Contains
        // just as well as the real call does.
        Assert.Contains("if (state.bindingsChanged) requestBindingsRefetch();", body, StringComparison.Ordinal);
        Assert.DoesNotContain("loadPanel()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The gate itself: a bindings-changed signal arriving while any pointer
    /// is down is HELD, not applied and not dropped - same shape as
    /// <c>requestPageSwitch</c>'s own gate.
    /// </summary>
    [Fact]
    public void PanelClientSource_RequestBindingsRefetch_HoldsWhileAPointerIsDown()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function requestBindingsRefetch()");

        Assert.Contains("activePointers > 0", body, StringComparison.Ordinal);
        Assert.Contains("pendingBindingsRefetch = true", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The re-fetch storm guard: a second (or third) push arriving while a
    /// re-fetch from an earlier one is still in flight must not queue a
    /// second overlapping fetch - it is coalesced into the single one already
    /// running, independent of whether a pointer is down at all.
    /// </summary>
    [Fact]
    public void PanelClientSource_RequestBindingsRefetch_CoalescesAgainstItsOwnInFlightFetch()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function requestBindingsRefetch()");

        Assert.Contains("bindingsRefetchInFlight", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the storm guard: a push that arrived WHILE a fetch
    /// was in flight is not simply dropped - once that fetch finishes, the
    /// coalesced request is re-issued exactly once, rather than losing the
    /// rebind it was reporting.
    /// </summary>
    [Fact]
    public void PanelClientSource_RunBindingsRefetch_ReissuesOnceMoreIfAnotherPushArrivedWhileFetching()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function runBindingsRefetch()");

        Assert.Contains("bindingsRefetchInFlight = false", body, StringComparison.Ordinal);
        Assert.Contains("if (pendingBindingsRefetch)", body, StringComparison.Ordinal);
        Assert.Contains("requestBindingsRefetch()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half - a held re-fetch that is never released is one
    /// silently dropped, leaving a rebind invisible until some unrelated
    /// later re-fetch happens to cover for it.
    /// </summary>
    [Fact]
    public void PanelClientSource_ReleasingTheLastPointer_AppliesAHeldBindingsRefetch()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function releasePointer()");

        Assert.Contains("pendingBindingsRefetch", body, StringComparison.Ordinal);
        Assert.Contains("requestBindingsRefetch()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rebind-loop lockup (user-confirmed live, ~10:27pm 2026-09-11):
    /// <c>applyLive</c> used to cache <c>lastLiveState = state</c> BEFORE
    /// consuming the edge fields, so <c>renderPanel</c>'s replay of
    /// <c>lastLiveState</c> at the end of every <c>loadPanel()</c> - including
    /// the very render the refetch itself produced - saw
    /// <c>bindingsChanged</c> still <c>true</c> and fired another refetch,
    /// forever (the in-flight/pending guard only stopped it from being a
    /// stack overflow, turning it into a steady poll loop instead). The cached
    /// copy must have both one-shot edge fields cleared at the moment it is
    /// captured - a positional check, not just a bare Contains, because a
    /// fix that clears the fields on a SEPARATE object the edge handlers never
    /// see would leave the exact same bug live while still containing the
    /// string "bindingsChanged: false" somewhere in the function.
    /// </summary>
    [Fact]
    public void PanelClientSource_ApplyLive_ClearsBindingsChangedAndSwitchToPageOnTheCachedCopy_BeforeTheEdgeHandlersRun()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        // [2026-09-17, O28 - the cacheStatement literal below (and its two
        // copies in the next tests) was
        // "lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false };"
        // and gained ", macroFinished: null" when the fifth edge field was
        // added. Superseded, not weakened: the claim is one field larger.]

        // The bare re-assignment this replaces must be gone - if it is still
        // there, the cached copy is the live-carrying edge object itself and
        // this whole pin is vacuous.
        Assert.DoesNotContain("lastLiveState = state;", body, StringComparison.Ordinal);

        // [2026-09-18 - SUPERSEDED again. The cacheStatement literal below
        // (and its three copies in the tests that follow) was
        // "...layoutChanged: false, macroFinished: null };" and gained
        // ", themeChanged: false" when the sixth edge field was added.
        // Superseded, not weakened: the claim is one field larger.]
        const string cacheStatement = "lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false, macroFinished: null, themeChanged: false };";
        Assert.Contains(cacheStatement, body, StringComparison.Ordinal);

        var cacheIndex = body.IndexOf(cacheStatement, StringComparison.Ordinal);
        var switchCheckIndex = body.IndexOf("requestPageSwitch(state.switchToPage)", StringComparison.Ordinal);
        var refetchCheckIndex = body.IndexOf("if (state.bindingsChanged) requestBindingsRefetch();", StringComparison.Ordinal);

        Assert.True(switchCheckIndex >= 0, "Expected the switchToPage edge check to still be present.");
        Assert.True(refetchCheckIndex >= 0, "Expected the bindingsChanged edge check to still be present.");

        // Caching must happen BEFORE either edge handler runs - both handlers
        // read off the ORIGINAL `state` parameter, never off `lastLiveState`,
        // so ordering here is what proves the cached copy cannot be the one a
        // handler consults while deciding to fire.
        Assert.True(cacheIndex < switchCheckIndex, "Expected the cached copy to be assigned before the switchToPage check.");
        Assert.True(cacheIndex < refetchCheckIndex, "Expected the cached copy to be assigned before the bindingsChanged check.");
    }

    /// <summary>
    /// <c>state.timing</c> and <c>state.slots</c> are LEVELS, not edges - the
    /// fix above must not overreach and start stripping them from the cached
    /// copy too, or <c>renderPanel</c>'s replay (the whole reason the cache
    /// exists - see its own remarks) would stop re-lighting slots after a
    /// resize/orientation refetch.
    /// </summary>
    [Fact]
    public void PanelClientSource_ApplyLive_CachedCopyStillCarriesTimingAndSlotsForReplay()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        const string cacheStatement = "lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false, macroFinished: null, themeChanged: false };";
        Assert.Contains(cacheStatement, body, StringComparison.Ordinal);
        Assert.DoesNotContain("timing: null", cacheStatement, StringComparison.Ordinal);
        Assert.DoesNotContain("slots:", cacheStatement, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // LayoutChanged (2026-09-13) - a device's layout edited live from the PC
    // (?asDevice=<id>), reaching that device's own already-open live
    // channel. Reuses requestBindingsRefetch entirely - same "bare re-fetch
    // signal" shape as bindingsChanged above, just a different cause - so
    // these pins only need to cover applyLive's own routing and the cached
    // copy's edge-clearing, never a second copy of requestBindingsRefetch's
    // own gate/coalescing pins (those already apply to whichever caller
    // triggers it).
    // ------------------------------------------------------------------

    /// <summary>
    /// The live handler must ROUTE a layout-changed signal through
    /// <c>requestBindingsRefetch</c> too - the same one-word "simplification"
    /// risk <c>bindingsChanged</c>'s own pin above guards against, just for
    /// the second caller of that function.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheLiveHandler_RoutesLayoutChangedThroughRequestBindingsRefetch()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        Assert.Contains("if (state.layoutChanged) requestBindingsRefetch();", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same replay lockup <c>bindingsChanged</c>'s own cached-copy pin
    /// guards against (2026-09-11's "rebind locks the panel"), for the new
    /// field: the cached copy must have <c>layoutChanged</c> cleared too, or
    /// <c>renderPanel</c>'s unconditional replay of <c>lastLiveState</c> at
    /// the end of every <c>loadPanel()</c> - including the very render the
    /// refetch itself produces - would see it still <c>true</c> and re-fire
    /// <c>requestBindingsRefetch()</c> forever.
    /// </summary>
    [Fact]
    public void PanelClientSource_ApplyLive_ClearsLayoutChangedOnTheCachedCopy_BeforeTheEdgeHandlerRuns()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        const string cacheStatement = "lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false, macroFinished: null, themeChanged: false };";
        Assert.Contains(cacheStatement, body, StringComparison.Ordinal);

        var cacheIndex = body.IndexOf(cacheStatement, StringComparison.Ordinal);
        var layoutCheckIndex = body.IndexOf("if (state.layoutChanged) requestBindingsRefetch();", StringComparison.Ordinal);

        Assert.True(layoutCheckIndex >= 0, "Expected the layoutChanged edge check to be present.");
        Assert.True(cacheIndex < layoutCheckIndex, "Expected the cached copy to be assigned before the layoutChanged check.");
    }

    // ------------------------------------------------------------------
    // ThemeChanged (2026-09-18) - an EDHM colour edit reaching an
    // already-open device (ThemeFileWatcher). Reuses requestBindingsRefetch
    // for the button grid, same "bare re-fetch signal" shape as
    // bindingsChanged/layoutChanged above, PLUS a second, conditional
    // refresh of the settings gear's own theme fetch (loadTheme) when its
    // sheet happens to be open right now.
    // ------------------------------------------------------------------

    /// <summary>
    /// The live handler must ROUTE a theme-changed signal through
    /// <c>requestBindingsRefetch</c> too - the same one-word "simplification"
    /// risk <c>bindingsChanged</c>'s own pin above guards against, just for
    /// the third caller of that function.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheLiveHandler_RoutesThemeChangedThroughRequestBindingsRefetch()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");
        var themeBranch = ExtractArm(body, "if (state.themeChanged) {");

        Assert.Contains("requestBindingsRefetch();", themeBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second half of a theme-changed push: the settings gear's own
    /// "From your HUD" swatches (<c>GET /api/theme</c> via <c>loadTheme()</c>)
    /// are NOT refreshed by <c>requestBindingsRefetch</c>/<c>loadPanel</c> at
    /// all - a client that never opened the settings sheet must not pay for
    /// a fetch that has nothing on screen to update, so this is gated on the
    /// sheet actually being open, exactly the same condition <c>boot()</c>
    /// already uses to decide whether to call <c>loadTheme()</c> for any of
    /// its four hash-routed panes.
    /// </summary>
    [Fact]
    public void PanelClientSource_ThemeChanged_RefreshesTheSettingsSheetTheme_OnlyWhenItIsOpen()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        var themeBranch = ExtractArm(body, "if (state.themeChanged) {");

        Assert.Contains("classList.contains('hidden')", themeBranch, StringComparison.Ordinal);
        Assert.Contains("loadTheme()", themeBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same replay lockup <c>bindingsChanged</c>'s own cached-copy pin
    /// guards against (2026-09-11's "rebind locks the panel"), for the sixth
    /// edge field: the cached copy must have <c>themeChanged</c> cleared too,
    /// or <c>renderPanel</c>'s unconditional replay of <c>lastLiveState</c> at
    /// the end of every <c>loadPanel()</c> - including the very render the
    /// refetch itself produces - would see it still <c>true</c> and re-fire
    /// <c>requestBindingsRefetch()</c> (and, if the sheet happened to be
    /// open, <c>loadTheme()</c> too) forever.
    /// </summary>
    [Fact]
    public void PanelClientSource_ApplyLive_ClearsThemeChangedOnTheCachedCopy_BeforeTheEdgeHandlerRuns()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        const string cacheStatement = "lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false, macroFinished: null, themeChanged: false };";
        Assert.Contains(cacheStatement, body, StringComparison.Ordinal);

        var cacheIndex = body.IndexOf(cacheStatement, StringComparison.Ordinal);
        var themeCheckIndex = body.IndexOf("if (state.themeChanged)", StringComparison.Ordinal);

        Assert.True(themeCheckIndex >= 0, "Expected the themeChanged edge check to be present.");
        Assert.True(cacheIndex < themeCheckIndex, "Expected the cached copy to be assigned before the themeChanged check.");
    }

    // ------------------------------------------------------------------
    // Drag a button to a different slot, in edit mode (ref/docs/editor.md).
    //
    // EVERY ASSERTION BELOW IS A PIN, NOT DRIVEN BEHAVIOUR. There is no
    // headless browser in this suite, so nothing here proves a finger
    // actually moves a button on a tablet - only that the source says what
    // it has to say for that to be possible, and does NOT say the things
    // that would make it dangerous. The server half of the same feature IS
    // driven end to end (ServerHostBuilderTests' SlotMove_* and
    // SlotEditEndpointTests' Move_*).
    //
    // Each one is scoped to a single function body or a single conditional
    // arm, for the reason the vessel-context block above already states: a
    // needle searched across this whole file would be satisfied by a
    // comment, or by the very code the guard exists to forbid sitting
    // somewhere else entirely.
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts one conditional arm - the substring from
    /// <paramref name="opener"/> to its own matching close brace - so an
    /// assertion can be made about the branch that is actually REACHED in a
    /// given state rather than about the file containing a line somewhere.
    /// "Every sentence can be true of the branch it was written for while
    /// the wrong branch is the one taken" is exactly the defect a
    /// whole-file needle cannot see.
    /// </summary>
    private static string ExtractArm(string content, string opener)
    {
        var start = content.IndexOf(opener, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{opener}' to appear in the page source.");

        var open = start + opener.LastIndexOf('{');
        var depth = 0;
        for (var i = open; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content.Substring(open, i - open + 1);
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after '{opener}'.");
    }

    /// <summary>
    /// <b>The single most important claim in this feature, and the one the
    /// commander's panel depends on: a drag can never move a button outside
    /// edit mode, and can never fire one inside it.</b> Both follow from
    /// one structural fact rather than from any runtime check -
    /// <c>layoutGrid</c> attaches <em>exactly one</em> of the two gestures
    /// to each cell, in the two arms of one conditional.
    /// <c>wireSlotGesture</c> is the only thing on this page that presses a
    /// control, and it lives in the mode-OFF arm.
    ///
    /// Scoped to the arms, not the function, deliberately: a version that
    /// wired BOTH gestures in the edit-mode arm - which is exactly what a
    /// careless "let the buttons still work while editing" edit produces -
    /// would satisfy every whole-file needle for both names.
    /// </summary>
    [Fact]
    public void PanelClientSource_EditModeArm_WiresTheDragGesture_AndNeverThePressGesture()
    {
        var grid = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        var editArm = ExtractArm(grid, "if (editMode) {");

        Assert.Contains("wireEditGesture(btn, index, slot)", editArm, StringComparison.Ordinal);
        Assert.DoesNotContain("wireSlotGesture", editArm, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other arm, and the other direction of the same claim - the press
    /// gesture must still be wired when the mode is OFF, and the drag
    /// gesture must not be. Without this half, deleting the whole
    /// <c>else</c> branch would leave the pin above perfectly green over a
    /// panel that no longer fires anything at all.
    /// </summary>
    [Fact]
    public void PanelClientSource_NonEditArm_WiresThePressGesture_AndNeverTheDragGesture()
    {
        var grid = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        var playArm = ExtractArm(grid, "} else if (slot) {");

        Assert.Contains("wireSlotGesture(btn, slot)", playArm, StringComparison.Ordinal);
        Assert.DoesNotContain("wireEditGesture", playArm, StringComparison.Ordinal);
    }

    /// <summary>
    /// A drag must not fire the button, stated as a property of the drag
    /// handler itself rather than only as a property of where it is wired.
    /// <c>wireEditGesture</c> must contain nothing that presses: no
    /// <c>onSlotTap</c>, no <c>onSlotLongPress</c>, and no press route.
    /// This is what stops "make the buttons live in edit mode too" being a
    /// one-line change nobody notices.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheDragGesture_NeverPressesAnything()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireEditGesture(btn, index, slot)");

        Assert.DoesNotContain("onSlotTap", body, StringComparison.Ordinal);
        Assert.DoesNotContain("onSlotLongPress", body, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_PRESS__", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tap is told from a drag by DISTANCE, never by elapsed time - the
    /// brief's own requirement, because a slow tap is still a tap. The pin
    /// is that the drag handler contains no timer at all: a
    /// <c>setTimeout</c> appearing inside it is the exact shape of the
    /// regression ("treat a hold as a pick-up") and would also put this
    /// gesture into direct competition with the 500ms long-press that
    /// already means something on a slot with a second action.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheDragGesture_StartsOnDistance_WithNoTimerAnywhereInIt()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireEditGesture(btn, index, slot)");

        Assert.Contains("Math.abs(dx) > DRAG_START_PX || Math.abs(dy) > DRAG_START_PX", body, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LONG_PRESS_MS", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The threshold is a distance in pixels, and it is the same 10 the
    /// page already uses to decide a pointer is no longer stationary
    /// (<c>MOVE_CANCEL_PX</c>). Pinned as a literal rather than as
    /// <c>Assert.Equal(theOtherConstant, this)</c>, which would be an
    /// identity and not an assertion.
    /// </summary>
    [Fact]
    public void PanelClientSource_DragThresholdAndMoveCancelThreshold_AreBothTenPixels()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("const DRAG_START_PX = 10;", content, StringComparison.Ordinal);
        Assert.Contains("const MOVE_CANCEL_PX = 10;", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A completed drag must not also open the slot sheet, and a drop that
    /// landed nowhere must not move anything. Both live in the
    /// <c>pointerup</c> handler's dragging arm: it commits the move and
    /// RETURNS, so the tap branch below it is unreachable after a drag.
    /// </summary>
    [Fact]
    public void PanelClientSource_ACompletedDrag_CommitsAMoveAndReturns_NeverOpeningTheSlotSheet()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireEditGesture(btn, index, slot)");

        var draggingArm = ExtractArm(body, "if (wasDragging) {");

        Assert.Contains("if (target !== null) postSlotMove(index, target);", draggingArm, StringComparison.Ordinal);
        Assert.Contains("return;", draggingArm, StringComparison.Ordinal);
        Assert.DoesNotContain("openSlotSheet", draggingArm, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the tap half: a pointer that never passed the threshold still
    /// opens the slot sheet, guarded on movement alone. <c>wasMoved</c>
    /// rather than any duration is the whole point.
    /// </summary>
    [Fact]
    public void PanelClientSource_ATapStillOpensTheSlotSheet_GuardedOnMovementAlone()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireEditGesture(btn, index, slot)");

        Assert.Contains("if (!wasMoved) openSlotSheet(index, slot);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Touch first, not mouse-first-and-hope. Two things make this work
    /// with a finger and neither is optional: <c>setPointerCapture</c>, so
    /// the moves and the release keep arriving once the finger has left the
    /// button it started on (without it, a drop over a different cell is
    /// never reported at all), and <c>touch-action: none</c> on the cells
    /// while the mode is on, without which the browser claims a vertical
    /// finger move as a pan and cancels the drag mid-gesture.
    ///
    /// The CSS rule is pinned as scoped to <c>#grid.editing</c>: applying
    /// it to <c>.slot</c> outright would change how every button on the
    /// panel handles touch outside edit mode, which nothing here asked for.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheDragGesture_CapturesThePointer_AndTouchActionIsDisabledOnEditModeCells()
    {
        var content = ReadPanelClientSource();
        var body = ExtractJsFunctionBody(content, "function wireEditGesture(btn, index, slot)");

        Assert.Contains("btn.setPointerCapture(e.pointerId)", body, StringComparison.Ordinal);

        var rule = ExtractCssRuleBody(content, "#grid.editing .slot");
        Assert.Contains("touch-action: none", rule, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mode's own class has to actually be applied, or the CSS rule
    /// above is styling a selector nothing ever matches - a rule that can
    /// never fire, and a drag that dies to a browser pan on the first
    /// downward flick. <c>layoutGrid</c> is where it happens, and it is a
    /// <c>toggle</c> against <c>editMode</c> so leaving the mode takes it
    /// off again.
    /// </summary>
    [Fact]
    public void PanelClientSource_TheGridCarriesTheEditingClass_ToggledByTheModeItself()
    {
        var grid = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        Assert.Contains("grid.classList.toggle('editing', editMode);", grid, StringComparison.Ordinal);
    }

    /// <summary>
    /// A move commits through the one route, on ONE page - the same
    /// <c>currentPage</c> for both ends. Crossing pages is deliberately not
    /// part of this gesture (<c>ref/docs/editor.md</c>), and this pin is
    /// what makes adding it a decision rather than a drift: a second page
    /// index appearing in this body cannot be added without reddening the
    /// verbatim match.
    /// </summary>
    [Fact]
    public void PanelClientSource_AMoveIsPostedForOnePage_WithNoSecondPageIndex()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postSlotMove(fromIndex, toIndex)");

        Assert.Contains("fetch('__API_SLOT_MOVE__'", body, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify({ page: currentPage, from: fromIndex, to: toIndex })", body, StringComparison.Ordinal);
        Assert.Contains("await loadPanel();", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The sheet stack (ref/docs/web-client.md, live-test row: "the control
    // picker is buried under #macroBuilder"). Every modal .sheet shares
    // position: fixed; inset: 0; z-index: 900, so paint order is pure DOM
    // order - #macroBuilder sits later in the document than #actionPicker,
    // so a macro step's "Choose a control..." button opening the picker
    // while the builder stayed open (hand-hidden only the step editor, not
    // the builder underneath) painted the builder's scrim on top of an
    // already-open, already-populated picker. Taps landed on the builder,
    // not the picker - the control could not be chosen at all.
    //
    // pushSheet/popSheet replace every hand-hide with a tracked stack: a
    // push hides whatever else is visible and remembers it, a pop restores
    // exactly that. Same "no headless browser, these are source-scan pins"
    // ceiling as the rest of this file.
    // ------------------------------------------------------------------

    /// <summary>
    /// The stack's bookkeeping is explicit, not read back off the 'hidden'
    /// class - that class is also toggled directly by plenty of other code
    /// on this page, so a push that only inspected classList at pop time
    /// could resurrect a sheet the commander had genuinely closed
    /// underneath in the meantime. <c>sheetStack</c> is the record.
    /// </summary>
    [Fact]
    public void PanelClientSource_PushSheet_TracksSuppressedSheetsInAnExplicitStack()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function pushSheet(id)");

        Assert.Contains("sheetStack.push(", body, StringComparison.Ordinal);
        Assert.Contains("suppressed.push(sheet.id)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>popSheet</c> restores exactly what its own matching push
    /// suppressed, read back off that same stack entry - not a blind
    /// "un-hide everything else" that would just as happily resurrect a
    /// sheet closed underneath it.
    /// </summary>
    [Fact]
    public void PanelClientSource_PopSheet_RestoresExactlyWhatItsMatchingPushSuppressed()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function popSheet(id)");

        Assert.Contains("sheetStack.pop()", body, StringComparison.Ordinal);
        Assert.Contains("top.suppressed.forEach(sid => el(sid).classList.remove('hidden'));", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard that stops a genuinely-closed sheet springing back:
    /// <c>popSheet</c> only pops/restores from the stack when its OWN id is
    /// the one on top. Popping unconditionally (e.g. always
    /// <c>sheetStack.pop()</c> regardless of whose id is on top) would let
    /// an out-of-order close - or a sheet that was never pushed at all -
    /// restore a stack frame that belongs to someone else entirely.
    /// </summary>
    [Fact]
    public void PanelClientSource_PopSheet_OnlyRestoresWhenTheStackTopMatchesTheSheetBeingClosed()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function popSheet(id)");

        Assert.Contains("sheetStack[sheetStack.length - 1].id === id", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression itself, and the actual root cause named in the
    /// brief: <c>openActionPicker</c> must show itself through
    /// <c>pushSheet</c>, not a bare <c>classList.remove('hidden')</c> that
    /// leaves every other open sheet (in particular <c>#macroBuilder</c>)
    /// still painting over it.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenActionPicker_PushesTheSheetStack_NotADirectShow()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function openActionPicker(mode)");

        Assert.Contains("pushSheet('actionPicker');", body, StringComparison.Ordinal);
        Assert.DoesNotContain("el('actionPicker').classList.remove('hidden')", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same claim for <c>#tokenPicker</c> - the brief notes it only
    /// worked today by accident of being last in the document, which is
    /// exactly the kind of thing that stops being true the moment a new
    /// sheet is added after it. It must not depend on DOM order either.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenTokenPicker_PushesTheSheetStack_NotADirectShow()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openTokenPicker(mode, onPick)");

        Assert.Contains("pushSheet('tokenPicker');", body, StringComparison.Ordinal);
        Assert.DoesNotContain("el('tokenPicker').classList.remove('hidden')", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The specific line the live-test bug lived on: the step editor's
    /// Control button used to hand-hide ONLY <c>#stepEditor</c> before
    /// opening the action picker, leaving <c>#macroBuilder</c> open and
    /// later-in-DOM underneath it. Scoped to <c>renderStepEditor</c>'s own
    /// body (where the button's click handler is built) rather than the
    /// whole file, so this can fail to the literal regression coming back
    /// under a different surrounding line, not just to the string
    /// vanishing from the file entirely.
    /// </summary>
    [Fact]
    public void PanelClientSource_StepEditorControlButton_NoLongerHandHidesTheStepEditorBeforeOpeningThePicker()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        Assert.Contains("openActionPicker('macroStep')", body, StringComparison.Ordinal);
        Assert.DoesNotContain("el('stepEditor').classList.add('hidden')", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The general sweep this bug's fix is supposed to make unnecessary to
    /// repeat one sheet at a time: NONE of the six nestable picker/editor
    /// sheets may be shown or hidden by a bare <c>classList</c> call
    /// anywhere in the file any more - every show/hide must route through
    /// <c>pushSheet</c>/<c>popSheet</c>, which is the only place these
    /// literals are still allowed to appear (asserted by the two pushSheet/
    /// popSheet pins above). A reintroduced hand-hide anywhere - not only at
    /// the one call site that caused the reported bug - reddens this.
    /// </summary>
    [Theory]
    [InlineData("slotSheet")]
    [InlineData("actionPicker")]
    [InlineData("macroBuilder")]
    [InlineData("stepKindPicker")]
    [InlineData("stepEditor")]
    [InlineData("tokenPicker")]
    [InlineData("macroRunResult")]
    public void PanelClientSource_EveryNestableSheet_ShowsAndHidesOnlyThroughTheSheetStack(string sheetId)
    {
        var content = ReadPanelClientSource();

        Assert.DoesNotContain($"el('{sheetId}').classList.add('hidden')", content, StringComparison.Ordinal);
        Assert.DoesNotContain($"el('{sheetId}').classList.remove('hidden')", content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The hold gesture (ref/docs/latching-keys.md's hold-to-thrust
    // extension). No headless browser in this suite, so these are
    // source-scan pins, same discipline as the sheet-stack pins above.
    // ------------------------------------------------------------------

    /// <summary>
    /// <b>The constraint the user most cares about, stated as a positive
    /// claim about the SHARED function rather than only about the new one.</b>
    /// <c>wireSlotGesture</c> must dispatch a hold-flagged slot to
    /// <c>wireHoldGesture</c> and return before touching any of the existing
    /// tap/long-press machinery - the guard has to come first, or a hold
    /// slot would still arm the 500ms timer on its way to being overridden.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireSlotGesture_DispatchesAHoldFlaggedSlotToWireHoldGesture_BeforeAnythingElse()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireSlotGesture(btn, slot)");

        var guardIndex = body.IndexOf("if (slot.hold)", StringComparison.Ordinal);
        var dispatchIndex = body.IndexOf("wireHoldGesture(btn, slot)", StringComparison.Ordinal);
        var timerIndex = body.IndexOf("let timer = null;", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "Expected wireSlotGesture to guard on slot.hold.");
        Assert.True(dispatchIndex > guardIndex, "Expected the hold guard to dispatch to wireHoldGesture.");
        Assert.True(timerIndex > dispatchIndex, "Expected the hold guard to run BEFORE the long-press timer setup.");
    }

    /// <summary>
    /// <b>The byte-for-byte half of the same constraint.</b> Everything
    /// wireSlotGesture already did for a non-hold slot - the 500ms timer,
    /// the move-cancel tracking, the tap-vs-long-press decision on
    /// pointerup - must appear in this file completely unmodified from
    /// before this feature existed. Pinned as an exact multi-line literal
    /// rather than a handful of substrings, so a change to any line inside
    /// this block (not just a deletion) reddens here.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireSlotGesture_TheExistingTapAndLongPressMachinery_IsByteForByteUnchanged()
    {
        var content = ReadPanelClientSource();

        const string unchangedSince = """
              let timer = null;
              let firedLongPress = false;
              let moved = false;
              let startX = 0, startY = 0;

              btn.addEventListener('pointerdown', e => {
                firedLongPress = false;
                moved = false;
                startX = e.clientX;
                startY = e.clientY;
                timer = setTimeout(() => {
                  timer = null;
                  firedLongPress = true;
                  onSlotLongPress(slot);
                }, LONG_PRESS_MS);
              });
            """;

        Assert.Contains(unchangedSince, content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The new gesture itself: immediate pointerdown/pointerup POSTs, with
    /// no timer of any kind - a hold has to react to the very first frame of
    /// contact, not to 500ms of it.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireHoldGesture_PostsDownOnPointerdown_AndUpOnPointerupCancelLeave_WithNoTimer()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireHoldGesture(btn, slot)");

        Assert.Contains("postHoldPhase(slot, 'down')", body, StringComparison.Ordinal);
        Assert.Contains("postHoldPhase(slot, 'up')", body, StringComparison.Ordinal);
        Assert.Contains("'pointerup'", body, StringComparison.Ordinal);
        Assert.Contains("'pointercancel'", body, StringComparison.Ordinal);
        Assert.Contains("'pointerleave'", body, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LONG_PRESS_MS", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A sustained touch-hold otherwise triggers the mobile browser's own
    /// text-selection/copy menu - a live-tested regression from the same
    /// round that led to this feature.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireHoldGesture_SuppressesContextMenu()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireHoldGesture(btn, slot)");

        Assert.Contains("'contextmenu'", body, StringComparison.Ordinal);
        Assert.Contains("preventDefault()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The POST body itself carries the phase as a string, matching
    /// <c>PressEndpoint.HoldPhase</c>'s own <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>
    /// - a request that sent a bare boolean or a numeric phase would 400
    /// against the real server, and nothing in a source-only pin suite would
    /// otherwise catch that the two sides disagree.
    /// </summary>
    [Fact]
    public void PanelClientSource_PostHoldPhase_SendsTheHoldFieldAlongsidePageAndSlot()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postHoldPhase(slot, phase)");

        Assert.Contains("page: currentPage, slot: slot.index, hold: phase", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The affordance: only a slot the server actually flagged
    /// <c>hold: true</c> gets the CSS class, so the touch-behaviour override
    /// below can never apply to an ordinary button by accident.
    /// </summary>
    [Fact]
    public void PanelClientSource_LayoutGrid_AddsTheHoldableClass_OnlyWhenTheSlotIsFlaggedHold()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        Assert.Contains("const hasHold = !!(slot && slot.hold);", body, StringComparison.Ordinal);
        Assert.Contains("(hasHold ? ' holdable' : '')", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The CSS scoping constraint, stated both ways.</b> The touch-action/
    /// user-select override lives on <c>.slot.holdable</c> only - never on
    /// the base <c>.slot</c> rule, which is what keeps every other button's
    /// touch handling (scrolling, text selection where it already worked)
    /// completely unchanged.
    /// </summary>
    [Fact]
    public void PanelClientSource_TouchActionOverride_IsScopedToHoldableSlots_NeverOnTheBaseSlotRule()
    {
        var content = ReadPanelClientSource();

        var holdableRule = ExtractCssRuleBody(content, ".slot.holdable");
        Assert.Contains("touch-action: none", holdableRule, StringComparison.Ordinal);
        Assert.Contains("user-select: none", holdableRule, StringComparison.Ordinal);
        Assert.Contains("-webkit-user-select: none", holdableRule, StringComparison.Ordinal);

        var baseSlotRule = ExtractCssRuleBody(content, "\n  .slot {");
        Assert.DoesNotContain("touch-action", baseSlotRule, StringComparison.Ordinal);
        Assert.DoesNotContain("user-select", baseSlotRule, StringComparison.Ordinal);
    }

    /// <summary>
    /// [2026-09-16, superseding the 2026-09-15 always-on version] Drag is
    /// edit-mode-only (same "mutually exclusive by construction" split
    /// editMode/wireEditGesture already use for the grid), so the touch
    /// override is scoped to #tabs.editing - not the base .tab rule - or an
    /// overflowing tab row could never be scrolled by touch outside edit
    /// mode. Same both-ways scoping check as .slot.holdable above.
    /// </summary>
    [Fact]
    public void PanelClientSource_TabTouchOverride_IsScopedToEditingMode_NotTheBaseTabRule()
    {
        var content = ReadPanelClientSource();

        var editingTabRule = ExtractCssRuleBody(content, "#tabs.editing .tab:not(.tabAdd) {");
        Assert.Contains("touch-action: none", editingTabRule, StringComparison.Ordinal);
        Assert.Contains("user-select: none", editingTabRule, StringComparison.Ordinal);
        Assert.Contains("-webkit-user-select: none", editingTabRule, StringComparison.Ordinal);

        var baseTabRule = ExtractCssRuleBody(content, "\n  .tab {");
        Assert.DoesNotContain("touch-action", baseTabRule, StringComparison.Ordinal);
        Assert.DoesNotContain("user-select", baseTabRule, StringComparison.Ordinal);
    }

    /// <summary>
    /// The visible "these can be dragged" cue, requested after a live test
    /// showed the drag capability itself works but has no affordance -
    /// present only while #tabs carries the editing class, and never on the
    /// "+" button, which is not a page and is never draggable.
    /// </summary>
    [Fact]
    public void PanelClientSource_EditingTabs_ShowAGripGlyph_ButNeverOnTheAddButton()
    {
        var content = ReadPanelClientSource();

        var gripRule = ExtractCssRuleBody(content, "#tabs.editing .tab:not(.tabAdd)::before {");
        Assert.Contains("content:", gripRule, StringComparison.Ordinal);
    }

    /// <summary>
    /// #tabs gains the 'editing' class at the same render-time point
    /// #grid's own 'editing' toggle already lives at (inside the render
    /// function itself, not the editBtn click handler that merely triggers
    /// a re-render) - tied to actual render state, not just one call site,
    /// so it can never drift out of sync with what wireTabGesture's own
    /// editMode check is honoring.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderTabs_TogglesTabsEditingClass_SameWayRenderGridDoes()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderTabs(data)");

        Assert.Contains("tabs.classList.toggle('editing', editMode)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the edit-mode gate: wireTabGesture's pointermove
    /// must never begin a drag while editMode is off, or the drag capability
    /// (and the touch-scroll regression it would cause) exists regardless of
    /// what the CSS/editBtn wiring above claims.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireTabGesture_NeverEntersADrag_WhenEditModeIsOff()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireTabGesture(btn, index)");
        var pointerMoveBody = ExtractJsFunctionBody(body, "btn.addEventListener('pointermove', e => ");

        Assert.Contains("if (!moved || !editMode) return;", pointerMoveBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The slot sheet's own toggle - exact mirror of the latch button's own
    /// wiring, targeting the hold route and field instead.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenSlotSheet_TogglesTheHoldButton_MirroringTheLatchButton()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openSlotSheet(index, slot)");

        Assert.Contains("const isHold = !!(slot && slot.hold);", body, StringComparison.Ordinal);
        // [2026-09-16] SUPERSEDED by folders - before-value:
        // "el('slotSheetHold').classList.toggle('hidden', !slot || isMacroSlot);"
        // Same third disjunct the latch toggle gained, for the same reason
        // (a folder has no key to hold). The mirroring this pin is about is
        // intact: both controls still hide under identical conditions.
        Assert.Contains("el('slotSheetHold').classList.toggle('hidden', !slot || isMacroSlot || isFolderSlot);", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelClientSource_PostSlotHold_TargetsTheHoldRoute_WithTheHoldField()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postSlotHold(slotIndex, hold)");

        Assert.Contains("__API_SLOT_HOLD__", body, StringComparison.Ordinal);
        Assert.Contains("page: currentPage, slot: slotIndex, hold: hold", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Editing a specific paired device's real layout live, from the PC. No
    // headless browser in this suite, same caveat as every source-scan pin
    // above - these prove the wiring a one-line "simplification" would most
    // plausibly drop is still there, not that a finger on real hardware
    // actually sees the banner or the button. The server half (asDevice's
    // resolution, the refusal, the security-critical cookie-request
    // isolation, the live-channel scoping) is driven end to end instead, in
    // DeviceAuthMiddlewareExtensionsTests and ServerHostBuilderTests.
    // ------------------------------------------------------------------

    /// <summary>
    /// <b>The single mechanism this feature's whole client half rests on.</b>
    /// Rather than editing each of this file's several dozen individual
    /// <c>fetch(...)</c> call sites to append <c>asDevice</c>, <c>window.fetch</c>
    /// itself is overridden once - every call this page already makes goes
    /// through it. A version that instead added a NEW helper function and
    /// left <c>window.fetch</c> untouched would satisfy a bare search for
    /// "asDeviceQuerySuffix" while leaving every one of those pre-existing
    /// call sites unaffected, so this is pinned as the actual reassignment.
    /// </summary>
    [Fact]
    public void PanelClientSource_WindowFetchIsOverridden_ToThreadAsDeviceThroughEveryExistingCall()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("const _nativeFetch = window.fetch.bind(window);", content, StringComparison.Ordinal);
        Assert.Contains("window.fetch = (input, init) => {", content, StringComparison.Ordinal);
        Assert.Contains("input = input + asDeviceQuerySuffix(input.includes('?'));", content, StringComparison.Ordinal);
        Assert.Contains("return _nativeFetch(input, init);", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// EventSource never goes through <c>fetch</c> at all, so the override
    /// above cannot reach the live channel - this is the one other place
    /// asDevice has to be threaded through by hand.
    /// </summary>
    [Fact]
    public void PanelClientSource_ConnectLive_AppendsAsDeviceToTheEventSourceUrl()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function connectLive()");

        Assert.Contains("asDeviceQuerySuffix(true)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Host-only visibility, stated as a property of the guard itself.</b>
    /// The entry point must never appear on a paired device's own screen -
    /// gated on <c>IS_HOST</c>, the same constant every other host-only
    /// affordance on this page (the Transfer tab, the macro-authoring
    /// buttons) already gates on, rather than a second, independently
    /// invented condition that could drift out of agreement with it.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderDeviceList_EditLiveButton_OnlyAddedWhenIsHost()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderDeviceList(data)");

        Assert.Contains("if (IS_HOST && device.deviceId !== EDITING_DEVICE_ID) {", body, StringComparison.Ordinal);
        Assert.Contains("onEditDeviceLive(device.deviceId)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The navigation itself: a full reload carrying <c>?asDevice=</c>,
    /// matching how this page's other host-only entry points
    /// (<c>#macros</c>/<c>#timing</c>/<c>#transfer</c>) already reach this
    /// one document rather than inventing a second navigation mechanism.
    /// </summary>
    [Fact]
    public void PanelClientSource_OnEditDeviceLive_NavigatesWithAsDeviceSetToTheChosenDevice()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function onEditDeviceLive(deviceId)");

        Assert.Contains("location.href = location.pathname + '?' + ASDEVICE_PARAM + '=' + encodeURIComponent(deviceId);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The persistent indicator and its exit control, both in one place: the
    /// banner must actually be un-hidden (not merely have its text set, which
    /// would leave it invisible), and the stop control must reload with no
    /// <c>asDevice</c> at all - <c>location.pathname</c> alone carries no
    /// query string, which is what returns the session to the host's own
    /// private layout.
    /// </summary>
    [Fact]
    public void PanelClientSource_InitEditingDeviceBanner_ShowsTheBanner_AndWiresStopEditingBackToTheHostsOwnLayout()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function initEditingDeviceBanner()");

        Assert.Contains("if (!EDITING_DEVICE_ID) return;", body, StringComparison.Ordinal);
        Assert.Contains("location.href = location.pathname;", body, StringComparison.Ordinal);
        Assert.Contains("el('editingDeviceBanner').classList.remove('hidden');", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The integration point: a banner function that exists but is never
    /// called would leave every pin above green over a feature nobody ever
    /// sees run.
    /// </summary>
    [Fact]
    public void PanelClientSource_Boot_CallsInitEditingDeviceBanner()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function boot()");

        Assert.Contains("await initEditingDeviceBanner();", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The live-tested defect this closes: the banner (<c>position: fixed</c>)
    /// drew directly over #chrome's row, hiding #editBtn and #gearBtn from a
    /// commander editing a real device's layout live. The fix must measure
    /// the banner's ACTUAL rendered height (not a hardcoded guess that could
    /// drift from the CSS above) and publish it as a custom property AFTER
    /// the banner is un-hidden, not before - measuring while it is still
    /// <c>display: none</c> would read a height of zero.
    /// </summary>
    [Fact]
    public void PanelClientSource_InitEditingDeviceBanner_MeasuresTheVisibleBanner_AndPublishesItsHeightAsAnOffsetVariable()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function initEditingDeviceBanner()");

        var unhideIndex = body.IndexOf("el('editingDeviceBanner').classList.remove('hidden');", StringComparison.Ordinal);
        var offsetIndex = body.IndexOf("document.documentElement.style.setProperty(", StringComparison.Ordinal);

        Assert.True(unhideIndex >= 0, "Expected the banner to be un-hidden.");
        Assert.True(offsetIndex >= 0, "Expected --editingBannerOffset to be published.");
        Assert.True(offsetIndex > unhideIndex,
            "Expected the offset to be measured AFTER the banner is un-hidden, not before " +
            "(a height read while the banner is still display:none is zero).");

        Assert.Contains("'--editingBannerOffset',", body, StringComparison.Ordinal);
        Assert.Contains("el('editingDeviceBanner').getBoundingClientRect().height + 'px'", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same fix, on the CSS side: #panelScreen is
    /// pushed down by the published offset rather than #chrome's own
    /// declared height changing - see the doc comment beside
    /// #editingDeviceBannerStop in the page source for why. The <c>0px</c>
    /// fallback is what proves the "hidden" half of the claim: whenever the
    /// custom property was never set (every session with EDITING_DEVICE_ID
    /// unset - see the guard above's <c>if (!EDITING_DEVICE_ID) return;</c>,
    /// which returns before the property is ever touched), this rule
    /// resolves to zero and #panelScreen's layout is exactly what it was
    /// before this fix existed.
    /// </summary>
    [Fact]
    public void PanelClientSource_PanelScreen_PadsForTheBannerOffset_DefaultingToZeroWhenNeverSet()
    {
        var content = ReadPanelClientSource();
        var rule = ExtractCssRuleBody(content, "#panelScreen { padding-top:");

        Assert.Contains("padding-top: var(--editingBannerOffset, 0px);", rule, StringComparison.Ordinal);
    }

    /// <summary>
    /// Closes the blast radius the doc comment on #editingDeviceBannerStop
    /// warns against: #chrome's own declared height (the fit-loop's
    /// HeaderStrip allowance, spelled once in
    /// <c>CellSizeEstimator.PhoneHeaderStrip</c>/<c>TabletHeaderStrip</c> and
    /// pinned by <c>CellSizeEstimatorTests</c>) must stay exactly what it was
    /// before this fix - the banner offset is applied to #panelScreen, never
    /// folded into #chrome's own rule. A #chrome rule that grew a
    /// padding/margin tied to --editingBannerOffset would silently misreport
    /// that allowance a third time, which is the exact failure the source
    /// comment says this design exists to prevent.
    /// </summary>
    [Fact]
    public void PanelClientSource_Chrome_DoesNotReferenceTheBannerOffset()
    {
        var content = ReadPanelClientSource();
        var rule = ExtractCssRuleBody(content, "#chrome {");

        Assert.DoesNotContain("editingBannerOffset", rule, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The per-step run read-back (ref/docs/macro-builder.md's "What the
    // builder shows after a run"): a press response's new `steps` field is
    // now rendered on the device rather than discarded. No headless browser
    // in this suite, same caveat as every source-scan pin above - these
    // cannot prove the sheet paints correctly, only that the wiring a
    // one-line "simplification" would most plausibly drop is still there.
    // ------------------------------------------------------------------

    /// <summary>
    /// The guard that keeps this sheet from popping up over every ordinary
    /// action press: it must bail out before touching the DOM when
    /// <c>body.steps</c> is missing or empty, which is the shape a plain
    /// (non-macro) press response has, and the shape a macro press refused
    /// before it ever started (busy, guard) reports too. Also pins that the
    /// list is actually rebuilt (<c>innerHTML = ''</c>) rather than appended
    /// to, and that it opens through <c>pushSheet</c> - the stack-aware
    /// open/close every other sheet on this page uses - rather than toggling
    /// the <c>hidden</c> class directly, which would desynchronise it from
    /// <c>sheetStack</c> the moment it is opened from underneath another
    /// sheet.
    /// </summary>
    [Fact]
    public void PanelClientSource_ShowMacroRunResult_BailsOutWithNoSteps_AndOpensThroughPushSheet()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function showMacroRunResult(body)");

        Assert.Contains("if (!body || !body.steps || !body.steps.length) return;", body, StringComparison.Ordinal);
        Assert.Contains("list.innerHTML = '';", body, StringComparison.Ordinal);
        Assert.Contains("pushSheet('macroRunResult')", body, StringComparison.Ordinal);
        Assert.DoesNotContain("classList.remove('hidden')", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each rendered row reuses the builder's own established row style
    /// (<c>renderMacroDraft</c>'s <c>.stepRow</c>/<c>.stepRowText</c>/
    /// <c>.stepRowNote</c>) rather than a second, hand-invented look for a
    /// step list - and marks a failed step with the same <c>bad</c> class
    /// an unbound step's note already uses, so a failure reads as a failure
    /// at a glance instead of only through its text.
    /// </summary>
    [Fact]
    public void PanelClientSource_ShowMacroRunResult_RendersRowsInTheEstablishedStepRowStyle()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function showMacroRunResult(body)");

        Assert.Contains("row.className = 'stepRow';", body, StringComparison.Ordinal);
        Assert.Contains("main.className = 'stepRowMain';", body, StringComparison.Ordinal);
        Assert.Contains("text.className = 'stepRowText';", body, StringComparison.Ordinal);
        Assert.Contains("'stepRowNote' + (failed ? ' bad' : '')", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actual integration point, moved (2026-09-17, O28). A macro's
    /// press response no longer carries the run's outcome - it answers
    /// <c>Started</c> at once, and the outcome arrives later as the live
    /// channel's <c>macroFinished</c> edge. So neither press site may show
    /// the read-back off the press response any more (there is nothing
    /// there to show, and a call left behind would be dead code that reads
    /// as if the outcome still arrived there), and <c>applyLive</c> must
    /// route the edge to the ONE handler that does - never gated behind
    /// <c>fired</c>, since a run that aborted partway is exactly the case
    /// the read-back exists to explain.
    ///
    /// [2026-09-17 - SUPERSEDED. This was
    /// <c>PanelClientSource_OnSlotTapAndLongPress_ShowMacroRunResultAfterEveryPress</c>,
    /// asserting <c>Contains("showMacroRunResult(body);")</c> in BOTH press
    /// bodies. The claim is now the opposite for the press sites and moved
    /// to the live handler; the old expectation is recorded here rather
    /// than deleted.]
    /// </summary>
    [Fact]
    public void PanelClientSource_TheRunReadBack_ComesFromTheLiveChannelsMacroFinishedEdge_NotFromEitherPressResponse()
    {
        var content = ReadPanelClientSource();

        var tap = ExtractJsFunctionBody(content, "async function onSlotTap(slot)");
        var longPress = ExtractJsFunctionBody(content, "async function onSlotLongPress(slot)");
        var applyLive = ExtractJsFunctionBody(content, "function applyLive(state)");
        var onFinished = ExtractJsFunctionBody(content, "function onMacroFinished(finished)");

        Assert.DoesNotContain("showMacroRunResult(", tap, StringComparison.Ordinal);
        Assert.DoesNotContain("showMacroRunResult(", longPress, StringComparison.Ordinal);

        Assert.Contains("if (state.macroFinished) onMacroFinished(state.macroFinished);", applyLive, StringComparison.Ordinal);
        Assert.Contains("showMacroRunResult(finished);", onFinished, StringComparison.Ordinal);
        Assert.DoesNotContain("if (finished.fired)", onFinished, StringComparison.Ordinal);
    }

    /// <summary>
    /// The synchronous refusals (guard, busy, and the second press that
    /// stops a run) still come back on the press response with
    /// <c>fired:false</c> and a reason - so the toast at both press sites
    /// must survive the move above. Only the read-back moved.
    /// </summary>
    [Fact]
    public void PanelClientSource_BothPressSites_StillToastASynchronousRefusal()
    {
        var content = ReadPanelClientSource();

        var tap = ExtractJsFunctionBody(content, "async function onSlotTap(slot)");
        var longPress = ExtractJsFunctionBody(content, "async function onSlotLongPress(slot)");

        Assert.Contains("if (!body || !body.fired) {", tap, StringComparison.Ordinal);
        Assert.Contains("if (!body || !body.fired) {", longPress, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The Background-only darker swatch group (ref/docs/theme.md's "Known
    // limit, not fixed here: every offered background is a bright one").
    // Scoped to openColourPicker's own body so this can fail to a role
    // check moved or dropped, not just to the DARK_PALETTE constant
    // vanishing from the file entirely.
    // ------------------------------------------------------------------

    /// <summary>
    /// The positive half: picking the Background role's swatch must select
    /// the darker palette, not the shared bright one, and must update the
    /// group's own label so a commander does not mistake the darker set for
    /// the same "Standard colours" group every other role shows.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenColourPicker_BackgroundRole_UsesTheDarkPalette()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openColourPicker(role)");

        Assert.Contains("const standardPalette = role === 'background' ? DARK_PALETTE : PALETTE;", body, StringComparison.Ordinal);
        Assert.Contains("el('pickerStandardLabel').textContent = role === 'background' ? 'Dark shades' : 'Standard colours';", body, StringComparison.Ordinal);
        Assert.Contains("for (const swatch of standardPalette)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression guard: the loop that actually populates the swatch
    /// group must read off the role-selected <c>standardPalette</c>, never
    /// straight off the shared <c>PALETTE</c> constant - which is exactly
    /// the one-word "simplification" that would silently widen the darker
    /// set back out to every role, or (the other direction) leave Background
    /// stuck on the bright set while the selection line above looks correct
    /// on its own.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenColourPicker_PopulatesTheGroupFromTheSelectedPalette_NeverTheBareBrightOne()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openColourPicker(role)");

        Assert.DoesNotContain("for (const swatch of PALETTE)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression this whole feature must not cause: Border/Text/Lit
    /// (and "one colour for everything") must keep offering the same bright
    /// palette they always have. There is no per-role branch for any of
    /// these three other than the Background check itself, so this is pinned
    /// as the absence of any role-specific special case for them, together
    /// with the array constants themselves (<see cref="HudPalette.Swatches"/>/
    /// <see cref="HudPalette.DarkSwatches"/> are two distinct arrays - see
    /// <c>LunaPanel.Core.Theme.HudPalette</c>) so an accidental swap of which
    /// array is the "else" branch is caught here rather than only in a live
    /// picker.
    /// </summary>
    [Theory]
    [InlineData("border")]
    [InlineData("text")]
    [InlineData("lit")]
    [InlineData("all")]
    public void PanelClientSource_OpenColourPicker_NonBackgroundRoles_KeepTheOriginalBrightPalette(string role)
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openColourPicker(role)");

        // The ternary's false branch is PALETTE for every role that is not
        // 'background' - asserted once here per role name so a future edit
        // that special-cases one of these four (e.g. giving 'lit' its own
        // dark set too) reddens this pin rather than sailing through
        // unnoticed.
        Assert.Contains("role === 'background' ? DARK_PALETTE : PALETTE", body, StringComparison.Ordinal);
        Assert.DoesNotContain($"role === '{role}' ? DARK_PALETTE", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The page bar's "+", rename, and visibility picker
    // (ref/docs/panels-and-pages.md). No headless browser in this suite,
    // so these are source-scan pins, same discipline as every pin above.
    // ------------------------------------------------------------------

    /// <summary>
    /// The "+" itself: appended after renderTabs' own pageNames loop, never
    /// inside it, and wired to the add-page flow rather than to the ordinary
    /// tap-to-switch click every named tab gets.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderTabs_AppendsATrailingAddButton_AfterTheLoop_WiredToOpenAddPageFlow()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderTabs(data)");

        // [2026-09-16] SUPERSEDED by folders - before-value:
        // "wireTabGesture(b, index);". A folder-owned page is excluded from
        // pageNames, so a tab's position in the row is no longer its page
        // index and every tab is now wired with data.pageIndices[index]
        // instead. The claim this pin makes - the "+" is appended AFTER the
        // loop, never inside it - is unchanged; only the landmark it reads
        // the end of the loop by moved.
        var loopEnd = body.IndexOf("wireTabGesture(b, realIndex);", StringComparison.Ordinal);
        var addBtnIndex = body.IndexOf("addBtn.className = 'tab tabAdd'", StringComparison.Ordinal);
        var wireIndex = body.IndexOf("addBtn.addEventListener('click', openAddPageFlow)", StringComparison.Ordinal);

        Assert.True(loopEnd >= 0, "Expected renderTabs to wire each named tab through wireTabGesture.");
        Assert.True(addBtnIndex > loopEnd, "Expected the '+' button to be built AFTER the pageNames loop, not inside it.");
        Assert.True(wireIndex > addBtnIndex, "Expected the '+' button to be wired to openAddPageFlow.");
    }

    /// <summary>
    /// <b>The regression guard the brief specifically calls for.</b> Every
    /// named tab must still switch pages on a plain tap - the exact
    /// behaviour that existed before this feature - for a page that is NOT
    /// newly created, i.e. this proves the ordinary tap-to-switch path
    /// inside wireTabGesture is untouched, not merely that it exists
    /// somewhere.
    ///
    /// [2026-09-15] Extracted from the 'pointerup' handler, not a 'click'
    /// listener - the drag-to-reorder rewrite folded tap-to-switch into
    /// pointerup and removed the separate click listener entirely (a click
    /// can still synthesize after a completed drag, which would otherwise
    /// re-trigger this same path on the source tab - see wireEditGesture's
    /// own reasoning for why IT has no click listener either).
    /// </summary>
    [Fact]
    public void PanelClientSource_WireTabGesture_OrdinaryTap_StillSwitchesPages_UnlessALongPressAlreadyFired()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireTabGesture(btn, index)");

        var pointerUpBody = ExtractJsFunctionBody(body, "btn.addEventListener('pointerup', e => ");

        Assert.Contains("if (firedLongPress)", pointerUpBody, StringComparison.Ordinal);
        Assert.Contains("if (index === currentPage) return;", pointerUpBody, StringComparison.Ordinal);
        Assert.Contains("currentPage = index;", pointerUpBody, StringComparison.Ordinal);
        Assert.Contains("loadPanel();", pointerUpBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The long-press half of the same function: a held tab opens page
    /// settings for the PRESSED tab's own index, using the same
    /// LONG_PRESS_MS convention wireSlotGesture already established for a
    /// slot's own long-press - never a second, hand-typed duration.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireTabGesture_LongPress_OpensPageSettingsForThePressedTab_UsingTheSharedDuration()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireTabGesture(btn, index)");

        Assert.Contains("openPageSettingsSheet(index)", body, StringComparison.Ordinal);
        Assert.Contains("LONG_PRESS_MS", body, StringComparison.Ordinal);
        Assert.Contains("firedLongPress = true;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The gap this rewrite opens with, closed.</b> Before this feature,
    /// wireTabGesture never watched pointermove at all, so a drag attempt
    /// would also fire the long-press timer mid-drag. This proves the fix:
    /// once real movement passes DRAG_START_PX, the long-press timer is
    /// cancelled inside the 'pointermove' handler itself.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireTabGesture_PointerMove_CancelsTheLongPressTimer_PastDragStartThreshold()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireTabGesture(btn, index)");

        var pointerMoveBody = ExtractJsFunctionBody(body, "btn.addEventListener('pointermove', e => ");

        Assert.Contains("DRAG_START_PX", pointerMoveBody, StringComparison.Ordinal);
        Assert.Contains("cancelTimer()", pointerMoveBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Drag-to-reorder itself: a completed drag that lands on a different
    /// tab commits through postPageMove, using rectangles captured once at
    /// drag start (tabButtons) - same discipline as the grid's own
    /// editButtons/slotIndexAtPoint, mirrored here for the tab row.
    /// </summary>
    [Fact]
    public void PanelClientSource_WireTabGesture_CompletedDrag_CommitsThroughPostPageMove()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function wireTabGesture(btn, index)");

        var pointerUpBody = ExtractJsFunctionBody(body, "btn.addEventListener('pointerup', e => ");

        Assert.Contains("if (wasDragging)", pointerUpBody, StringComparison.Ordinal);
        Assert.Contains("postPageMove(index, target)", pointerUpBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// tabIndexAtPoint is the tab row's own 1-D analogue of the grid's
    /// slotIndexAtPoint - a plain x-only containment check, since a tab row
    /// only moves left/right.
    /// </summary>
    [Fact]
    public void PanelClientSource_TabIndexAtPoint_IsAOneDimensionalContainmentCheck()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function tabIndexAtPoint(rects, x)");

        Assert.Contains("x >= r.rect.left && x <= r.rect.right", body, StringComparison.Ordinal);
        Assert.DoesNotContain("r.rect.top", body, StringComparison.Ordinal);
        Assert.DoesNotContain("r.rect.bottom", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The add-page flow: name first (via the lightweight #pageNameDialog
    /// parallel, not #renameDialog itself - see that sheet's own HTML
    /// comment for why), then the template picker, then the actual POST -
    /// pinned as the ORDER the three calls appear in, since collapsing the
    /// two-step flow into one call would silently create a page with no
    /// name confirmation.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenAddPageFlow_AsksForANameFirst_ThenOpensTheTemplatePicker()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openAddPageFlow()");

        Assert.Contains("openPageNameDialog(", body, StringComparison.Ordinal);
        var nameCallIndex = body.IndexOf("openPageNameDialog(", StringComparison.Ordinal);
        var pickerCallIndex = body.IndexOf("openPageTemplatePicker()", StringComparison.Ordinal);
        Assert.True(pickerCallIndex > nameCallIndex, "Expected the template picker to open only inside the name dialog's own commit handler.");
    }

    /// <summary>
    /// Cancelling the template picker (Close) must abandon the whole flow -
    /// no page created, and the name already entered discarded - rather than
    /// leaving a half-finished add pending for the next unrelated rung tap
    /// to pick up.
    /// </summary>
    [Fact]
    public void PanelClientSource_PageTemplatePickerClose_AbandonsThePendingNewPageName()
    {
        var content = ReadPanelClientSource();
        var handlerStart = content.IndexOf("el('pageTemplatePickerClose').addEventListener", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "Expected a close handler for #pageTemplatePicker.");
        var body = content.Substring(handlerStart, content.IndexOf("\n});", handlerStart) - handlerStart);

        Assert.Contains("pendingNewPageName = null;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actual creation call: POSTs the pending name and the tapped
    /// rung's own templateId to __API_PAGE_ADD__, and on success switches
    /// straight to the new page (the index the server itself reports) and
    /// reloads - never a page index the client computed on its own.
    /// </summary>
    [Fact]
    public void PanelClientSource_OnPageTemplateRungTap_PostsToPageAdd_AndSwitchesToTheServerReportedIndex()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function onPageTemplateRungTap(templateId)");

        Assert.Contains("'__API_PAGE_ADD__'", body, StringComparison.Ordinal);
        Assert.Contains("currentPage = body.pageIndex;", body, StringComparison.Ordinal);
        Assert.Contains("loadPanel();", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rename control posts to __API_PAGE_RENAME__ for the page whose
    /// settings sheet is open (pageSettingsPageIndex), not necessarily
    /// currentPage - a commander can rename a page without switching to it
    /// first, the same "not necessarily currentPage" property the long-press
    /// itself has.
    /// </summary>
    [Fact]
    public void PanelClientSource_PageSettingsRename_PostsToPageRename_ForTheOpenSettingsPageIndex()
    {
        var content = ReadPanelClientSource();
        var handlerStart = content.IndexOf("el('pageSettingsRename').addEventListener", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "Expected a click handler on #pageSettingsRename.");
        var body = content.Substring(handlerStart, content.IndexOf("\n});", handlerStart) - handlerStart);

        Assert.Contains("'__API_PAGE_RENAME__'", body, StringComparison.Ordinal);
        Assert.Contains("page: index", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// [2026-09-15] Delete a page outright (ref/docs/panels-and-pages.md),
    /// reversing this file's own earlier "no delete-page control" decision.
    /// The button exists, is wired to __API_PAGE_DELETE__ for the page
    /// whose settings sheet is open (pageSettingsPageIndex), and confirms
    /// before sending anything.
    /// </summary>
    [Fact]
    public void PanelClientSource_PageSettingsDelete_ExistsAndPostsToPageDelete_ForTheOpenSettingsPageIndex()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("id=\"pageSettingsDelete\"", content, StringComparison.Ordinal);

        var handlerStart = content.IndexOf("el('pageSettingsDelete').addEventListener", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "Expected a click handler on #pageSettingsDelete.");
        var body = content.Substring(handlerStart, content.IndexOf("\n});", handlerStart) - handlerStart);

        Assert.Contains("confirm(", body, StringComparison.Ordinal);
        Assert.Contains("'__API_PAGE_DELETE__'", body, StringComparison.Ordinal);
        Assert.Contains("page: pageSettingsPageIndex", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The visibility picker offers a CLOSED vocabulary - the tokens the
    /// shipped starter layout's own pages already use - never a free-text
    /// field, which would let a typo reach __API_PAGE_SHOWWHEN__ with no
    /// feedback beyond a reject-and-retry round trip.
    /// </summary>
    [Fact]
    public void PanelClientSource_VisibilityTokens_IsAClosedVocabulary_MatchingTheStarterLayoutsOwnTokens()
    {
        var content = ReadPanelClientSource();

        foreach (var token in new[] { "InMainShip", "InFighter", "InSrv", "Vessel:testbuggy", "Vessel:lander01", "OnFoot" })
        {
            Assert.Contains($"token: '{token}'", content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Toggling a token POSTs to __API_PAGE_SHOWWHEN__ with the page the
    /// settings sheet is open for, not currentPage - matching
    /// PageSettingsRename's own "not necessarily currentPage" property.
    ///
    /// <b>Superseded 2026-09-12 (was: asserted directly on
    /// <c>togglePageVisibilityToken</c>'s own body).</b> The exact-set
    /// duplicate-conflict fix (this same task) extracted the actual POST
    /// into a new <c>postShowWhen</c> helper so the confirm-before-force
    /// retry could reuse it, rather than duplicating the fetch call for both
    /// the first attempt and the forced retry. <c>togglePageVisibilityToken</c>
    /// itself now only computes <c>next</c> and delegates. The underlying
    /// claim - POSTed for the OPEN settings page, not currentPage - still
    /// holds; it is proven here in two steps (the delegation, then the
    /// helper's own request body) rather than in <c>togglePageVisibilityToken</c>'s
    /// body directly, because that body no longer contains the fetch call at
    /// all.
    /// </summary>
    [Fact]
    public void PanelClientSource_TogglePageVisibilityToken_PostsToPageShowWhen_ForTheOpenSettingsPageIndex()
    {
        var content = ReadPanelClientSource();
        var toggleBody = ExtractJsFunctionBody(content, "async function togglePageVisibilityToken(token)");
        var postShowWhenBody = ExtractJsFunctionBody(content, "async function postShowWhen(next, force, conflictPageIndex)");

        Assert.Contains("postShowWhen(next, false, null)", toggleBody, StringComparison.Ordinal);
        Assert.Contains("'__API_PAGE_SHOWWHEN__'", postShowWhenBody, StringComparison.Ordinal);
        Assert.Contains("page: pageSettingsPageIndex", postShowWhenBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// [2026-09-15] SUPERSEDED. This used to pin "no delete-page control
    /// anywhere in this file - explicitly out of scope for this feature per
    /// the brief that added it" (asserting DoesNotContain("__API_PAGE_DELETE__")
    /// and DoesNotContain("deletePage")). The user explicitly asked for a
    /// delete-page control; see
    /// PanelClientSource_PageSettingsDelete_ExistsAndPostsToPageDelete_ForTheOpenSettingsPageIndex
    /// below for the control this ruling reversal actually added.
    /// </summary>
    [Fact]
    public void PanelClientSource_DeletePageControl_IsNowOfferedOnThePageSettingsSheet()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("__API_PAGE_DELETE__", content, StringComparison.Ordinal);
        Assert.Contains("id=\"pageSettingsDelete\"", content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // "Press a key" (ref/docs/macros.md) - a second way to build a press
    // step, starting from a physical key rather than a control name. No
    // headless browser drives any of this (see this file's own remarks
    // above), so these are source-scan pins scoped to individual function
    // bodies, same discipline as the vessel-context section above.
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>stepKindOf</c> must recognize <c>pressKey</c> as its own kind - a
    /// step carrying that field falling through to the empty-string default
    /// would make <c>describeStep</c>/<c>renderStepEditor</c>/every other
    /// kind-keyed switch treat it as "unrecognised" instead.
    /// </summary>
    [Fact]
    public void PanelClientSource_StepKindOf_RecognizesPressKey()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function stepKindOf(step)");

        Assert.Contains("'pressKey'", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The step-kind picker's own "press a key" row must open the key picker
    /// rather than creating a step directly the way every other row does
    /// (<c>newStepOfKind</c> + push) - it is the one row whose outcome
    /// depends on what the reverse lookup finds, decided only once a key has
    /// actually been picked.
    ///
    /// [SUPERSEDED 2026-09-17] Before-value: <c>"function renderStepKindList()"</c>.
    /// The function gained a <c>ctx</c> parameter (<c>{ list, isTopLevel }</c>)
    /// once the arm editor needed to reuse it for an arm's own "add a step"
    /// flow, not just the top level - a mechanical signature change, not a
    /// behavior change to the claim this test makes.
    /// </summary>
    [Fact]
    public void PanelClientSource_StepKindList_PressKeyRow_OpensTheKeyPicker_RatherThanCreatingAStepDirectly()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepKindList(ctx)");

        Assert.Contains("openKeyPicker()", body, StringComparison.Ordinal);
        Assert.Contains("kind.kind === 'pressKey'", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // [2026-09-13] The real-keypress-capture redesign (ref/docs/macros.md):
    // replaces the old search-a-list-then-view-a-result-sheet flow. The
    // pins immediately above and below this block that guarded the removed
    // functions (loadKeyList, keyMatchesQuery, renderKeyPickerList,
    // onKeyPicked, #keyPickerList, #keyPickerSearch, #keyMatchSheet) are
    // retired along with that code - see git history for the removed
    // fixtures. These pins guard the replacement: #keyCaptureBox's keydown
    // handler and renderKeyCaptureResult.
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>preventDefault()</c> must run before anything else in the keydown
    /// handler - unconditionally, for every key, not only inside some
    /// conditional branch. F5 would otherwise reload the browser, Tab would
    /// move focus off the capture box, Space would scroll the page - and
    /// Escape must stay a normal capturable key rather than gaining special
    /// handling, which the negative pin just below this one checks
    /// separately.
    /// </summary>
    [Fact]
    public void PanelClientSource_KeyCaptureBoxKeydown_CallsPreventDefaultUnconditionally_BeforeAnyBranching()
    {
        var content = ReadPanelClientSource();
        var body = ExtractJsFunctionBody(content, "el('keyCaptureBox').addEventListener('keydown', async (event)");

        // The first statement inside the handler body (immediately after the
        // opening brace, modulo whitespace and any number of leading // line
        // comments) must be the preventDefault call - not merely present
        // somewhere in the body.
        var afterBrace = body.Substring(1).TrimStart();
        while (afterBrace.StartsWith("//"))
        {
            var newlineIndex = afterBrace.IndexOf('\n');
            Assert.True(newlineIndex >= 0, "Expected a statement after the leading comment block.");
            afterBrace = afterBrace.Substring(newlineIndex + 1).TrimStart();
        }

        Assert.StartsWith("event.preventDefault();", afterBrace);
        Assert.DoesNotContain("if (event.code", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Escape must never be special-cased anywhere in this picker's own
    /// code - it stays a normal capturable key, and Cancel (a real button in
    /// the result area) is the only way to close the picker without picking
    /// a key. A future edit adding an Escape shortcut back in would defeat
    /// the "every key, including Escape, is capturable" design.
    /// </summary>
    [Fact]
    public void PanelClientSource_KeyCapture_NeverSpecialCasesEscape()
    {
        var content = ReadPanelClientSource();

        Assert.DoesNotContain("'Escape'", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Escape\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fetch call queries by <c>domCode</c>, the raw
    /// <c>KeyboardEvent.code</c> string - never the old <c>key</c> query
    /// parameter name the server no longer accepts.
    /// </summary>
    [Fact]
    public void PanelClientSource_KeyCaptureBoxKeydown_FetchesByDomCode_NotKey()
    {
        var content = ReadPanelClientSource();
        var body = ExtractJsFunctionBody(content, "el('keyCaptureBox').addEventListener('keydown', async (event)");

        Assert.Contains("'__API_ACTION_FOR_KEY__?domCode=' + encodeURIComponent(domCode)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("?key=", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The old list-rendering machinery this redesign replaces must no
    /// longer exist in the built page at all - not renamed, not left dead
    /// alongside the new flow. loadKeyList/keyList themselves are NOT part
    /// of this: they still feed keyLabelFor for an EXISTING pressKey step's
    /// label (describeStep, the step editor), just from loadMacrosPane now
    /// instead of the picker - see the dedicated
    /// LoadKeyList_IsCalledFromLoadMacrosPane_NotFromTheKeyCapturePicker pin.
    /// </summary>
    [Fact]
    public void PanelClientSource_OldKeyListMachinery_NoLongerExists()
    {
        var content = ReadPanelClientSource();

        Assert.DoesNotContain("function keyMatchesQuery(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("function renderKeyPickerList()", content, StringComparison.Ordinal);
        Assert.DoesNotContain("function onKeyPicked(", content, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"keyPickerList\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"keyPickerSearch\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"keyMatchSheet\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// keyLabelFor (describeStep, the step editor) still needs keyList
    /// populated to show an EXISTING pressKey step's label - the key-capture
    /// picker resolves a key from one keydown now and never needed the full
    /// list, so loadKeyList's only remaining call site is loadMacrosPane,
    /// not openKeyPicker. Losing this call site silently turns "the label
    /// eventually loads" into "the label never loads" - exactly the
    /// regression this pin exists to catch.
    /// </summary>
    [Fact]
    public void PanelClientSource_LoadKeyList_IsCalledFromLoadMacrosPane_NotFromTheKeyCapturePicker()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("function loadKeyList()", content, StringComparison.Ordinal);

        var macrosPaneBody = ExtractJsFunctionBody(content, "async function loadMacrosPane()");
        Assert.Contains("loadKeyList()", macrosPaneBody, StringComparison.Ordinal);

        var openKeyPickerBody = ExtractJsFunctionBody(content, "function openKeyPicker()");
        Assert.DoesNotContain("loadKeyList", openKeyPickerBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The match branch of the capture flow offers BOTH options, never
    /// picking one for the commander: "use the matched control" is built
    /// through <c>newStepOfKind('press')</c> - the EXISTING control-picker
    /// step shape, not a new code path, so the resulting step heals itself
    /// on a later rebind - and "use the raw key anyway" is still offered
    /// alongside it as a real <c>pressKey</c> step, since a match is only
    /// ever an offer, not a decision made on the commander's behalf (they
    /// may have a real reason to want the raw key regardless).
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderKeyCaptureResult_MatchBranch_OffersBothThePressStepAndThePressKeyStep()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderKeyCaptureResult(domCode, response, outcome)");
        var matchArm = ExtractArm(body, "} else if (response.matched) {");

        Assert.Contains("newStepOfKind('press')", matchArm, StringComparison.Ordinal);
        Assert.Contains("step.press = response.matched", matchArm, StringComparison.Ordinal);
        Assert.Contains("{ pressKey: response.resolvedKey, repeat: 1 }", matchArm, StringComparison.Ordinal);
    }

    /// <summary>
    /// The genuine no-match branch (fetch succeeded, server said
    /// <c>matched: false</c>): "use anyway" creates a real <c>pressKey</c>
    /// step carrying <c>response.resolvedKey</c> - the <c>Key_*</c> name the
    /// server actually resolved, not the raw domCode.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderKeyCaptureResult_NoMatchBranch_CreatesAPressKeyStepFromResolvedKey()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderKeyCaptureResult(domCode, response, outcome)");

        Assert.Contains("{ pressKey: response.resolvedKey, repeat: 1 }", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The "couldn't check" branch has no <c>response</c> to read a
    /// resolved key from, so it falls back to the raw <c>domCode</c> the
    /// browser captured - distinct from the no-match branch above, which
    /// pins the server's resolved name specifically.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderKeyCaptureResult_FailedBranch_CreatesAPressKeyStepFromRawDomCode()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderKeyCaptureResult(domCode, response, outcome)");
        var failedArm = ExtractArm(body, "} else if (outcome === 'failed') {");

        Assert.Contains("{ pressKey: domCode, repeat: 1 }", failedArm, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 400 "unrecognized key" outcome (domCode unmappable server-side) has
    /// no valid <c>Key_*</c> name to build any step from at all - this arm
    /// must offer no "use anyway" button, only the message.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderKeyCaptureResult_UnrecognizedBranch_OffersNoUseAnywayButton()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderKeyCaptureResult(domCode, response, outcome)");
        var unrecognizedArm = ExtractArm(body, "if (outcome === 'unrecognized') {");

        Assert.DoesNotContain("pickButton(", unrecognizedArm, StringComparison.Ordinal);
        Assert.DoesNotContain("macroDraft.steps.push", unrecognizedArm, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cancel is appended unconditionally, after all four outcome branches,
    /// so it appears in every result - and it only closes the sheet, never
    /// touching <c>macroDraft.steps</c>.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderKeyCaptureResult_CancelClosesTheSheet_WithoutTouchingMacroDraft()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderKeyCaptureResult(domCode, response, outcome)");

        var cancelIndex = body.IndexOf("pickButton('Cancel'", StringComparison.Ordinal);
        Assert.True(cancelIndex >= 0, "Expected a Cancel button in the capture result.");

        // Scope to the Cancel call's own arrow-function body, not the whole
        // function, so this can't be satisfied by macroDraft.steps.push
        // sitting in one of the other branches above it.
        var cancelCallStart = body.IndexOf('(', cancelIndex + "pickButton('Cancel'".Length);
        var cancelSlice = body.Substring(cancelIndex, body.IndexOf("));", cancelCallStart) + 3 - cancelIndex);

        Assert.Contains("popSheet('keyPicker')", cancelSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("macroDraft.steps", cancelSlice, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The step editor's per-step "Hold (seconds)" field. stepDurationMs()
    // already reads step.holdMs generically, and PressStep/PressKeyStep
    // already carry a Hold the runner executes - this closes the one
    // client-side gap, so these are source-scan pins over the field itself
    // rather than over MacroRunner, which was already correct.
    // ------------------------------------------------------------------

    /// <summary>
    /// The field must be added to the SAME conditional arm that already
    /// renders Repeat for both <c>press</c> and <c>pressKey</c> - not a
    /// separate per-kind block, which a future third kind sharing this
    /// arm could silently miss.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepEditor_AddsHoldFieldToTheSamePressAndPressKeyArm_AsRepeat()
    {
        var content = ReadPanelClientSource();

        var arm = ExtractArm(content, "if (kind === 'press' || kind === 'pressKey') {");

        Assert.Contains("numberField(step.repeat || 1, 1, v => { step.repeat = v; })", arm, StringComparison.Ordinal);
        Assert.Contains("macroField('Hold (seconds)', HELP_HOLD, holdSecondsField(step))", arm, StringComparison.Ordinal);
    }

    /// <summary>
    /// Seconds in the UI, milliseconds on the wire: a valid, positive input
    /// is rounded and multiplied by 1000 into <c>step.holdMs</c> - the exact
    /// field <c>stepDurationMs()</c> already reads (<c>step.holdMs || hold</c>)
    /// and <c>MacroDefinition.OptionalPositiveInt</c> requires as a positive
    /// integer when present at all, so a fractional or truncated value would
    /// either misreport the duration estimate or fail to save outright.
    /// </summary>
    [Fact]
    public void PanelClientSource_HoldSecondsField_WritesHoldMsAsWholeMillisecondsFromSeconds()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function holdSecondsField(step)");

        Assert.Contains("const parsed = parseFloat(input.value);", body, StringComparison.Ordinal);
        Assert.Contains("step.holdMs = Math.round(parsed * 1000);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Blank means "no override", not zero: an empty, non-numeric, or
    /// non-positive input DELETES <c>step.holdMs</c> rather than writing a
    /// falsy value that would still fail <c>OptionalPositiveInt</c>'s
    /// positive-integer check on save. This is what keeps a step with no
    /// hold typed in falling back to the commander's configured default
    /// hold, exactly as it does today with the field absent entirely.
    /// </summary>
    [Fact]
    public void PanelClientSource_HoldSecondsField_BlankOrNonPositiveInput_ClearsHoldMsRatherThanWritingZero()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function holdSecondsField(step)");

        Assert.Contains("if (input.value.trim() === '' || isNaN(parsed) || parsed <= 0) {", body, StringComparison.Ordinal);
        Assert.Contains("delete step.holdMs;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The field's initial value round-trips the same way in reverse -
    /// divided by 1000 back into seconds - and reads blank rather than "0"
    /// when the step has no <c>holdMs</c> at all, matching the help text's
    /// own "leave blank" instruction.
    /// </summary>
    [Fact]
    public void PanelClientSource_HoldSecondsField_ShowsBlank_NotZero_WhenStepHasNoHoldMs()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function holdSecondsField(step)");

        Assert.Contains("input.value = step.holdMs ? String(step.holdMs / 1000) : '';", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The t64 label-overflow fallback (2026-09-12, live-tested on a tablet:
    // "Nomad Dock/Launch" spilled past its button's edge on the 64-button
    // grid). layoutGrid's shrink loop floors at MIN_PX and used to stop
    // decrementing there regardless of whether the label still overflowed -
    // no headless browser in this suite, so these are source-scan pins, same
    // discipline as the rest of this file.
    // ------------------------------------------------------------------

    /// <summary>
    /// The CSS fallback itself: a dedicated class, not a change to the base
    /// <c>.slot</c> rule - scoping it this way is what keeps every button
    /// that fits comfortably (every layout other than a cramped one like
    /// t64) completely unaffected. <c>overflow: hidden</c> alone, never
    /// <c>text-overflow: ellipsis</c> - labels on this page wrap across
    /// multiple lines (<c>.slot</c>'s own <c>white-space: pre-line</c>),
    /// and ellipsis only ever applies to a single unwrapped line.
    /// </summary>
    [Fact]
    public void PanelClientSource_LabelClippedClass_HidesOverflow_NeverEllipsis()
    {
        var content = ReadPanelClientSource();

        var rule = ExtractCssRuleBody(content, ".slot.labelClipped");
        Assert.Contains("overflow: hidden", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("text-overflow", rule, StringComparison.Ordinal);

        // And the base rule must NOT carry it unconditionally - if it did,
        // every button on every layout would be clipped, not just the ones
        // that genuinely bottom out.
        var baseSlotRule = ExtractCssRuleBody(content, "\n  .slot {");
        Assert.DoesNotContain("overflow: hidden", baseSlotRule, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The condition that gates the class, stated exactly.</b> A button
    /// only qualifies when its OWN shrink loop exits with <c>px</c> at or
    /// below <c>MIN_PX</c> AND the overflow check still reads true at that
    /// size - not merely because <c>px</c> reached the floor (which also
    /// happens for a button that fits exactly at MIN_PX with no overflow
    /// left over).
    /// </summary>
    [Fact]
    public void PanelClientSource_LayoutGrid_FlagsOverflowingAtFloor_OnlyWhenStillOverflowingAtOrBelowMinPx()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        Assert.Contains("const overflowingAtFloor = [];", body, StringComparison.Ordinal);
        Assert.Contains(
            "if (px <= MIN_PX && (b.scrollHeight > b.clientHeight + OVERFLOW_TOLERANCE_PX || b.scrollWidth > b.clientWidth + OVERFLOW_TOLERANCE_PX)) {",
            body, StringComparison.Ordinal);
        Assert.Contains("overflowingAtFloor.push(b);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The shrink loop's own overflow check uses the SAME named tolerance
    /// as the floor check above it</b> - not a re-typed magic number that
    /// could silently drift out of sync. Both checks exist to answer one
    /// question ("does this button's label still overflow its padded box by
    /// more than a comfortable margin?") and if they ever disagreed, a
    /// button could stop shrinking under one threshold while being flagged
    /// clipped under the other. (2026-09-18: this tolerance replaced a bare
    /// "+ 1" that only guaranteed no literal overflow, not any visible
    /// margin beyond padPx - see the constant's own comment.)
    /// </summary>
    [Fact]
    public void PanelClientSource_LayoutGrid_ShrinkLoopAndFloorCheck_UseTheSameOverflowToleranceConstant()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        Assert.Contains("const OVERFLOW_TOLERANCE_PX = 6;", body, StringComparison.Ordinal);
        Assert.Contains(
            "while (px > MIN_PX && (b.scrollHeight > b.clientHeight + OVERFLOW_TOLERANCE_PX || b.scrollWidth > b.clientWidth + OVERFLOW_TOLERANCE_PX)) {",
            body, StringComparison.Ordinal);
        Assert.Contains(
            "if (px <= MIN_PX && (b.scrollHeight > b.clientHeight + OVERFLOW_TOLERANCE_PX || b.scrollWidth > b.clientWidth + OVERFLOW_TOLERANCE_PX)) {",
            body, StringComparison.Ordinal);

        // No bare "+ 1" tolerance survives anywhere in this function - the
        // exact regression this fallback closed.
        Assert.DoesNotContain("clientHeight + 1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("clientWidth + 1", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The class is applied to exactly that list, and only after the
    /// uniform final size has already been set on every button.</b> Applying
    /// it earlier (or to a different collection, such as <c>made</c>
    /// unconditionally) would either clip a button that fits fine at the
    /// shared size, or leave a still-overflowing one unclipped - both are the
    /// exact regression this fallback exists to close. Ordering matters the
    /// same way it does for <c>applyLive</c>'s cached-copy pin above: the
    /// clip has to land on the button's FINAL font size, not a per-button
    /// intermediate one the uniform pass is about to overwrite.
    /// </summary>
    [Fact]
    public void PanelClientSource_LayoutGrid_AppliesLabelClipped_OnlyToTheFlaggedList_AfterTheUniformFitIsSet()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function layoutGrid(data)");

        const string uniformFitStatement = "for (const b of made) b.style.fontSize = fit + 'px';";
        const string clipStatement = "for (const b of overflowingAtFloor) b.classList.add('labelClipped');";

        Assert.Contains(uniformFitStatement, body, StringComparison.Ordinal);
        Assert.Contains(clipStatement, body, StringComparison.Ordinal);

        var uniformFitIndex = body.IndexOf(uniformFitStatement, StringComparison.Ordinal);
        var clipIndex = body.IndexOf(clipStatement, StringComparison.Ordinal);
        Assert.True(clipIndex > uniformFitIndex,
            "Expected the labelClipped class to be applied AFTER the uniform fit size is set on every button.");

        // Never applied to every button drawn this pass - only to the ones
        // the floor-and-still-overflowing check actually flagged.
        Assert.DoesNotContain("for (const b of made) b.classList.add('labelClipped');", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Setting a page's vessel-context ShowWhen to a set another page
    // already claims (live-tested 2026-09-12: a second page took the exact
    // same tokens as an existing one with no warning, and the first page's
    // own setting silently stuck around too, leaving it permanently
    // unreachable by auto-switch). No headless browser in this suite, same
    // "source-scan pin, not driven behaviour" caveat as the rest of this
    // file - the server half (the exact-set conflict detection, the
    // force-clears-the-other-page edit) IS driven end to end, in
    // PageEditEndpointTests.
    // ------------------------------------------------------------------

    /// <summary>
    /// <b>The confirm-before-force flow, as a property of the actual retry
    /// path.</b> A conflict response must trigger a named <c>confirm()</c>
    /// dialog BEFORE any forced resubmit - not after, and not a resubmit
    /// that skips asking. Declining must return without ever sending
    /// <c>force: true</c> to the server; the take-over resubmit is the one
    /// and only place <c>force: true</c> is threaded into the request body,
    /// so a version that force-submitted unconditionally on any conflict
    /// (skipping the ask) would still satisfy a bare search for
    /// <c>force: true</c> without this ordering check.
    /// </summary>
    [Fact]
    public void PanelClientSource_PostShowWhen_AsksBeforeForcing_AndOnlySendsForceTrueInTheRetry()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postShowWhen(next, force, conflictPageIndex)");

        const string confirmCall = "if (!confirm(`Page \"${body.conflict.pageName}\" already auto-switches to this vessel context. Take it over from that page?`)) return;";
        const string retryCall = "await postShowWhen(next, true, body.conflict.pageIndex);";

        Assert.Contains(confirmCall, body, StringComparison.Ordinal);
        Assert.Contains(retryCall, body, StringComparison.Ordinal);

        var conflictCheckIndex = body.IndexOf("if (body && body.conflict) {", StringComparison.Ordinal);
        var confirmIndex = body.IndexOf(confirmCall, StringComparison.Ordinal);
        var retryIndex = body.IndexOf(retryCall, StringComparison.Ordinal);

        Assert.True(conflictCheckIndex >= 0, "Expected postShowWhen to branch on a conflict field in the response.");
        Assert.True(confirmIndex > conflictCheckIndex, "Expected the confirm dialog to run inside the conflict branch.");
        Assert.True(retryIndex > confirmIndex, "Expected the forced retry to be sent only after the confirm call.");

        // The initial POST (outside this conflict branch) must never itself
        // carry force: true - only the retry above does.
        var bodyBeforeConflictBranch = body.Substring(0, conflictCheckIndex);
        Assert.DoesNotContain("force: true", bodyBeforeConflictBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of declining: no state is applied, and no re-render
    /// happens, for a request that never gets past the confirm's <c>return</c>.
    /// Pinned as a positional fact - the state assignment and
    /// <c>renderPageVisibilityPicker()</c> call both have to sit AFTER the
    /// conflict branch's own early return, or a decline would still leave
    /// the toggle looking applied.
    /// </summary>
    [Fact]
    public void PanelClientSource_PostShowWhen_DecliningTheConfirm_NeverAppliesStateOrRenders()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postShowWhen(next, force, conflictPageIndex)");

        var conflictReturnIndex = body.IndexOf(
            "if (!confirm(`Page \"${body.conflict.pageName}\" already auto-switches to this vessel context. Take it over from that page?`)) return;",
            StringComparison.Ordinal);
        var stateAssignIndex = body.IndexOf("pageSettingsShowWhen = next;", StringComparison.Ordinal);
        var renderIndex = body.IndexOf("renderPageVisibilityPicker();", StringComparison.Ordinal);

        Assert.True(conflictReturnIndex >= 0);
        Assert.True(stateAssignIndex > conflictReturnIndex, "Expected the applied state to be set AFTER the decline's early return.");
        Assert.True(renderIndex > conflictReturnIndex, "Expected the re-render to happen AFTER the decline's early return.");
    }

    /// <summary>
    /// <b>The new behaviour this task adds: refreshing when the page just
    /// cleared out from under someone is the one currently on screen.</b>
    /// This must be the SAME existing "refresh if editing the current page"
    /// check, extended with an <c>||</c>, not a second parallel
    /// <c>loadPanel()</c> call - a second mechanism could drift out of
    /// agreement with the first (e.g. refresh twice, or refresh on the
    /// wrong condition) in a way a single extended check structurally
    /// cannot.
    /// </summary>
    [Fact]
    public void PanelClientSource_PostShowWhen_RefreshesIfEitherTheEditedPage_OrTheClearedConflictPage_IsCurrentPage()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postShowWhen(next, force, conflictPageIndex)");

        const string refreshCheck = "if (pageSettingsPageIndex === currentPage || conflictPageIndex === currentPage) await loadPanel();";
        Assert.Contains(refreshCheck, body, StringComparison.Ordinal);

        // Exactly one loadPanel() call in this function - the single
        // extended check above, not a second call site elsewhere in the
        // body that would make this a parallel mechanism rather than an
        // extension of the existing one.
        var firstLoadPanel = body.IndexOf("loadPanel()", StringComparison.Ordinal);
        var lastLoadPanel = body.LastIndexOf("loadPanel()", StringComparison.Ordinal);
        Assert.Equal(firstLoadPanel, lastLoadPanel);
    }

    /// <summary>
    /// The threading itself: the conflicting page's index from the
    /// FIRST response must reach the retry call as <c>conflictPageIndex</c>,
    /// so the refresh check above has something non-null to compare against
    /// currentPage after a forced save (whose own response carries no
    /// conflict field to read it back from).
    /// </summary>
    [Fact]
    public void PanelClientSource_ToggleShowWhen_StartsEveryRequestWithForceFalse_AndNoConflictIndexYet()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function togglePageVisibilityToken(token)");

        Assert.Contains("await postShowWhen(next, false, null);", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // [2026-09-13] pressUntil's second gate: a single JOURNAL event
    // (condJournal), mutually exclusive with the status condition list
    // (cond) the pressUntil/require/waitFor editor block already shared.
    // MacroDefinition.ParsePressUntil (Core, already changed, out of scope
    // here) requires exactly one of 'cond'/'condJournal' at load - these
    // pins guard the client half of that: the new control only appears for
    // pressUntil, and picking either gate actually clears the other in the
    // step model, not just in what happens to render.
    // ------------------------------------------------------------------

    /// <summary>
    /// The new journal-condition control (<c>'Or wait for the game to
    /// log'</c>) must exist specifically inside <c>renderStepEditor</c>'s
    /// <c>kind === 'pressUntil'</c> handling, not merely somewhere in the
    /// file - a needle scoped to the whole 2,300+-line source would be
    /// satisfied by an unrelated string with the same words. require/
    /// waitFor still only support status conditions server-side, so this
    /// asserts the control sits in a block gated on <c>'pressUntil'</c>
    /// specifically, distinct from the shared require/waitFor/pressUntil
    /// status-condition block above it.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepEditor_PressUntilOnly_OffersAJournalConditionControl()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        Assert.Contains("Or wait for the game to log", body, StringComparison.Ordinal);
        Assert.Contains("openTokenPicker('journal', token => {", body, StringComparison.Ordinal);
        Assert.Contains("step.condJournal = token.startsWith(JOURNAL_TOKEN_PREFIX)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shared status-condition block above the new control must stop
    /// rendering for a pressUntil step once a journal condition is chosen -
    /// otherwise a commander could see (and edit) the status list beneath a
    /// journal condition that has already replaced it, which is exactly the
    /// "can't build something the server will reject" case the brief calls
    /// out. Pinned as the literal guard expression, not merely that both
    /// blocks exist independently.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepEditor_StatusConditionBlock_HidesForPressUntilOnceAJournalConditionIsSet()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        Assert.Contains(
            "if (kind === 'require' || kind === 'waitFor' || (kind === 'pressUntil' && !step.condJournal)) {",
            body,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Picking a journal condition must <c>delete</c> the status condition
    /// field from the step model, not merely leave it empty - an empty
    /// array is still a PRESENT 'cond' key, which fails
    /// MacroDefinition.ParsePressUntil's "exactly one of cond/condJournal"
    /// check just as surely as having both populated. Pins the actual
    /// mutation inside the journal-picker callback, not just that a
    /// 'delete' keyword appears somewhere in the function.
    /// </summary>
    [Fact]
    public void PanelClientSource_PickingAJournalCondition_DeletesTheStatusConditionField_NotJustEmptiesIt()
    {
        // renderStepEditor's waitForEdge block also calls
        // openTokenPicker('journal', ...) twice (succeed/failOn), so the
        // pin anchors on the line that assigns step.condJournal from the
        // returned token - unique to the new pressUntil control - and
        // checks the very next statement, rather than matching the first
        // openTokenPicker('journal', ...) call in the file (waitForEdge's,
        // which must NOT touch step.cond at all).
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        const string assignThenDelete =
            "step.condJournal = token.startsWith(JOURNAL_TOKEN_PREFIX) ? token.slice(JOURNAL_TOKEN_PREFIX.length) : token;\n" +
            "          delete step.cond;";
        Assert.Contains(assignThenDelete, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reverse: removing an already-chosen journal condition (the chip
    /// built from it) must <c>delete step.condJournal</c> and restore an
    /// empty status condition list, which is what makes the shared block's
    /// guard above (<c>!step.condJournal</c>) show the status editor again
    /// - "a way to remove it and go back to picking a status condition
    /// instead," per the brief.
    /// </summary>
    [Fact]
    public void PanelClientSource_RemovingTheJournalCondition_DeletesItAndRestoresAnEmptyStatusConditionList()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        Assert.Contains("delete step.condJournal;", body, StringComparison.Ordinal);
        Assert.Contains("step.cond = [];", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>describeStep</c>'s <c>pressUntil</c> case must branch on which
    /// condition type is actually present, describing a journal condition
    /// with wording distinct from the status-condition sentence
    /// (<c>tokenListLabel(step.cond)</c>) rather than always describing the
    /// status list regardless of which gate the step actually uses -
    /// otherwise a pressUntil step built with a journal condition would be
    /// described as gated on "nothing chosen yet" even though it has a real
    /// gate.
    /// </summary>
    [Fact]
    public void PanelClientSource_DescribeStep_PressUntilCase_BranchesOnWhichConditionTypeIsPresent()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function describeStep(step)");
        var pressUntilCase = ExtractJsFunctionBody(body, "case 'pressUntil':");

        Assert.Contains("step.condJournal", pressUntilCase, StringComparison.Ordinal);
        Assert.Contains("tokenListLabel(step.cond)", pressUntilCase, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // The "show what each step did after a macro runs" toggle
    // (ref/docs/macro-builder.md's "What the builder shows after a run"),
    // Macros settings pane. Same "no headless browser, these are source-scan
    // pins" ceiling as the rest of this file - the server-side round trip
    // (PanelSettingsStore/PanelSettingsEndpoint) is driven end to end
    // elsewhere.
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts the substring between two markers, so a markup pin can
    /// assert about ONE pane's own HTML rather than the whole 2,300-line
    /// file - the same reason <see cref="ExtractJsFunctionBody"/> scopes
    /// JavaScript assertions to a single function body.
    /// </summary>
    private static string ExtractBetween(string content, string startMarker, string endMarker)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{startMarker}' to appear in the page source.");

        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Expected '{endMarker}' to appear after '{startMarker}'.");

        return content.Substring(start, end - start);
    }

    /// <summary>
    /// The checkbox must live in <c>paneMacros</c> specifically, not
    /// <c>panePanels</c> (which already has its own, unrelated
    /// <c>#mergeExpandToggle</c>) - a copy-paste of the wrong pane id would
    /// satisfy a bare whole-file <c>Contains</c> just as well as the correct
    /// placement does.
    /// </summary>
    [Fact]
    public void PanelClientSource_ShowMacroStepResultsToggle_LivesInThePaneMacrosPane_NotPanePanels()
    {
        var content = ReadPanelClientSource();

        var macrosPane = ExtractBetween(content, "id=\"paneMacros\"", "id=\"paneTiming\"");
        Assert.Contains("id=\"showMacroStepResultsToggle\"", macrosPane, StringComparison.Ordinal);
        Assert.Contains("id=\"showMacroStepResultsHelp\"", macrosPane, StringComparison.Ordinal);

        var panelsPane = ExtractBetween(content, "id=\"panePanels\"", "id=\"paneBindings\"");
        Assert.DoesNotContain("id=\"showMacroStepResultsToggle\"", panelsPane, StringComparison.Ordinal);
    }

    /// <summary>
    /// The auto-switch toggle must live in <c>panePanels</c> specifically -
    /// same reasoning as <see cref="PanelClientSource_ShowMacroStepResultsToggle_LivesInThePaneMacrosPane_NotPanePanels"/>,
    /// reversed: a copy-paste into <c>paneMacros</c> instead would satisfy a
    /// bare whole-file <c>Contains</c> just as well as the correct placement
    /// does.
    /// </summary>
    [Fact]
    public void PanelClientSource_AutoSwitchEnabledToggle_LivesInThePanePanelsPane_NotPaneMacros()
    {
        var content = ReadPanelClientSource();

        var panelsPane = ExtractBetween(content, "id=\"panePanels\"", "id=\"paneBindings\"");
        Assert.Contains("id=\"autoSwitchEnabledToggle\"", panelsPane, StringComparison.Ordinal);
        Assert.Contains("id=\"autoSwitchEnabledHelp\"", panelsPane, StringComparison.Ordinal);

        var macrosPane = ExtractBetween(content, "id=\"paneMacros\"", "id=\"paneTiming\"");
        Assert.DoesNotContain("id=\"autoSwitchEnabledToggle\"", macrosPane, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tap-to-open, never hover - same discipline as
    /// <see cref="BuildPage_MergeExpandHelp_IsTapToOpen_NeverHover"/> et al.
    /// </summary>
    [Fact]
    public void PanelClientSource_ShowMacroStepResultsHelp_IsTapToOpen()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("showMacroStepResultsHelp').addEventListener('click'", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The setting is loaded into BOTH the checkbox and the module-level
    /// cache <c>showMacroStepResults</c> that the two press call sites below
    /// actually gate on - loading only the checkbox's visual state and
    /// forgetting to update the cached value would leave the toggle looking
    /// right while every press still opened the sheet regardless.
    /// </summary>
    [Fact]
    public void PanelClientSource_LoadPanelSettings_UpdatesBothTheCheckboxAndTheCachedFlag()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function loadPanelSettings()");

        Assert.Contains("el('showMacroStepResultsToggle').checked = settings.showMacroStepResults;", body, StringComparison.Ordinal);
        Assert.Contains("showMacroStepResults = settings.showMacroStepResults;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The POST includes both settings together - <c>PanelSettingsEndpoint.Request</c>
    /// takes the whole object, not a per-field partial update, so a POST
    /// that sent only <c>showMacroStepResults</c> would silently reset
    /// <c>mergeExpand</c> to whatever the server's default happened to
    /// deserialize a missing field as.
    /// </summary>
    [Fact]
    public void PanelClientSource_PostPanelSettings_SendsBothFieldsTogether()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postPanelSettings()");

        Assert.Contains("mergeExpand: el('mergeExpandToggle').checked", body, StringComparison.Ordinal);
        Assert.Contains("showMacroStepResults: el('showMacroStepResultsToggle').checked", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The relocated auto-switch toggle is loaded from the same response as
    /// <c>mergeExpand</c>/<c>showMacroStepResults</c> - it needs no extra
    /// cached-variable step the way <c>showMacroStepResults</c> does, since
    /// nothing else in the client reads this flag; seeding the checkbox is
    /// the whole job.
    /// </summary>
    [Fact]
    public void PanelClientSource_LoadPanelSettings_SeedsTheAutoSwitchEnabledCheckbox()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function loadPanelSettings()");

        Assert.Contains("el('autoSwitchEnabledToggle').checked = settings.autoSwitchEnabled;", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The POST includes all three settings together - same
    /// always-send-everything reasoning as
    /// <see cref="PanelClientSource_PostPanelSettings_SendsBothFieldsTogether"/>:
    /// a POST that omitted <c>autoSwitchEnabled</c> would silently reset it
    /// to the server's default.
    /// </summary>
    [Fact]
    public void PanelClientSource_PostPanelSettings_SendsAutoSwitchEnabledToo()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function postPanelSettings()");

        Assert.Contains("autoSwitchEnabled: el('autoSwitchEnabledToggle').checked", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The simple <c>mergeExpandToggle</c> wiring shape - a bare
    /// <c>addEventListener('change', postPanelSettings)</c> with no extra
    /// cached-variable step, unlike <c>showMacroStepResultsToggle</c>'s own
    /// listener.
    /// </summary>
    [Fact]
    public void PanelClientSource_AutoSwitchEnabledToggle_PostsOnChange()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("el('autoSwitchEnabledToggle').addEventListener('change', postPanelSettings);", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tap-to-open, never hover - same discipline as
    /// <see cref="PanelClientSource_ShowMacroStepResultsHelp_IsTapToOpen"/>.
    /// </summary>
    [Fact]
    public void PanelClientSource_AutoSwitchEnabledHelp_IsTapToOpen()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("autoSwitchEnabledHelp').addEventListener('click'", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The help copy has to name the one fact the user asked to have
    /// surfaced explicitly: each paired device carries its own independent
    /// copy of this setting, not a machine-wide one.
    /// </summary>
    [Fact]
    public void PanelClientSource_AutoSwitchEnabledHelp_MentionsPerDeviceIndependence()
    {
        var content = ReadPanelClientSource();

        Assert.Contains("Each paired device has its own copy of this setting.", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The gate itself.</b> Since O28 (2026-09-17) the read-back has
    /// exactly one call site - <c>onMacroFinished</c>, fed by the live
    /// channel - so the setting gates it there, and the unguarded call must
    /// not exist anywhere in that handler (the exact one-word
    /// "simplification" that would silently reinstate the sheet regardless
    /// of the setting).
    ///
    /// [2026-09-17 - SUPERSEDED. This was
    /// <c>PanelClientSource_BothPressCallSites_GateShowMacroRunResultOnTheSetting</c>,
    /// pinning <c>if (showMacroStepResults) showMacroRunResult(body);</c> in
    /// BOTH <c>onSlotTap</c> and <c>onSlotLongPress</c>. Those two call sites
    /// no longer exist (the press response carries no steps to show); the
    /// gate moved with the call. The old expectation is recorded here
    /// rather than deleted.]
    /// </summary>
    [Fact]
    public void PanelClientSource_TheOneReadBackCallSite_GatesShowMacroRunResultOnTheSetting()
    {
        var onFinished = ExtractJsFunctionBody(ReadPanelClientSource(), "function onMacroFinished(finished)");

        Assert.Contains("if (showMacroStepResults) showMacroRunResult(finished);", onFinished, StringComparison.Ordinal);
        Assert.DoesNotContain("  showMacroRunResult(finished);", onFinished, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same replay lockup <c>bindingsChanged</c>'s and <c>layoutChanged</c>'s
    /// cached-copy pins guard against, for the fifth edge field: the cached
    /// copy must have <c>macroFinished</c> cleared too, or <c>renderPanel</c>'s
    /// unconditional replay of <c>lastLiveState</c> at the end of every
    /// <c>loadPanel()</c> would re-fire the finish toast and re-open the
    /// run-result sheet on the next unrelated grid rebuild - a resize, an
    /// orientation change, a rebind - for a macro that finished minutes ago.
    /// </summary>
    [Fact]
    public void PanelClientSource_ApplyLive_ClearsMacroFinishedOnTheCachedCopy_BeforeTheEdgeHandlerRuns()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function applyLive(state)");

        const string cacheStatement = "lastLiveState = { ...state, bindingsChanged: false, switchToPage: null, layoutChanged: false, macroFinished: null, themeChanged: false };";
        Assert.Contains(cacheStatement, body, StringComparison.Ordinal);

        var cacheIndex = body.IndexOf(cacheStatement, StringComparison.Ordinal);
        var finishedCheckIndex = body.IndexOf("if (state.macroFinished) onMacroFinished(state.macroFinished);", StringComparison.Ordinal);

        Assert.True(finishedCheckIndex >= 0, "Expected the macroFinished edge check to be present.");
        Assert.True(cacheIndex < finishedCheckIndex, "Expected the cached copy to be assigned before the macroFinished check.");
    }

    // -----------------------------------------------------------------
    // The import/recovery chooser's delete button
    // (ref/docs/layout-import.md's discard half)
    // -----------------------------------------------------------------

    /// <summary>
    /// <b>The confirmation guard, pinned by position, not just by
    /// presence.</b> The commander re-emphasized directly that this MUST ask
    /// before deleting anything irreversible - a future edit could keep the
    /// literal <c>confirm(...)</c> call somewhere in this function while
    /// moving it after the <c>fetch</c>, which would satisfy a
    /// presence-only assertion while deleting first and asking never. This
    /// asserts <c>confirm(</c> appears strictly before <c>fetch(</c> in the
    /// function body, which an edit reordering the two would turn red.
    /// </summary>
    [Fact]
    public void PanelClientSource_OnImportDiscard_ConfirmsBeforeItEverFetches()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function onImportDiscard(candidate)");

        var confirmIndex = body.IndexOf("confirm(", StringComparison.Ordinal);
        var fetchIndex = body.IndexOf("fetch(", StringComparison.Ordinal);

        Assert.True(confirmIndex >= 0, "Expected onImportDiscard to call confirm(...).");
        Assert.True(fetchIndex >= 0, "Expected onImportDiscard to call fetch(...).");
        Assert.True(confirmIndex < fetchIndex, "Expected confirm(...) to run before fetch(...) in onImportDiscard.");
    }

    /// <summary>
    /// The confirmation text names the candidate and says the action cannot
    /// be undone - matching the plan's exact wording, which matters here
    /// because unlike <c>onImportPick</c>'s own confirm, there really is no
    /// undo for this one.
    /// </summary>
    [Fact]
    public void PanelClientSource_OnImportDiscard_ConfirmTextNamesTheCandidate_AndSaysItCannotBeUndone()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function onImportDiscard(candidate)");

        Assert.Contains("Permanently delete the buttons saved for", body, StringComparison.Ordinal);
        Assert.Contains("This cannot be undone.", body, StringComparison.Ordinal);
        Assert.Contains("${candidate.name}", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cancelled confirm must never reach the network at all - an early
    /// return, not a flag checked deeper in the function (which a later edit
    /// could accidentally route around).
    /// </summary>
    [Fact]
    public void PanelClientSource_OnImportDiscard_CancellingTheConfirmReturnsImmediately()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function onImportDiscard(candidate)");

        Assert.Contains("if (!confirm(", body, StringComparison.Ordinal);
        var guardLine = body.Split('\n').First(line => line.Contains("if (!confirm(", StringComparison.Ordinal));
        Assert.Contains("return", guardLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// The delete button is gated on <c>!importIsPostPair</c>
    /// (<c>renderImportList</c>'s own second argument) - deliberately absent
    /// from the post-pairing recovery sheet, per the plan's "Deliberately
    /// NOT shown" section: the moment right after pairing is for choosing
    /// what to recover, not for an irreversible delete in the same breath.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenImportSheet_PassesShowDeleteAsNotPostPair()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openImportSheet(candidates, postPair)");

        Assert.Contains("renderImportList(candidates, !importIsPostPair);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The renderer itself only draws the delete button when told to - the
    /// gate is enforced at the render call site above, but a defect here
    /// (e.g. always drawing it regardless of the flag) would show the delete
    /// affordance on the post-pairing sheet even with that call site correct.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderImportList_OnlyAddsTheDeleteButtonWhenShowDeleteIsTrue()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderImportList(candidates, showDelete)");

        Assert.Contains("if (showDelete)", body, StringComparison.Ordinal);
        Assert.Contains("onImportDiscard(candidate)", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // [2026-09-17] The macro builder's advisory notes recurse into a
    // branch step's arms (ref/docs/macros.md's "A known, accepted side
    // effect" note, closed by this dispatch). `disembark` now carries every
    // one of its presses and requires inside a `branch`'s `then`/`else`
    // arms rather than at the top level, so a scan that only ever looked at
    // `macroDraft.steps` directly went silent for it. Source-scan pins, per
    // this file's own "no headless browser in this suite" note.
    // ------------------------------------------------------------------

    /// <summary>
    /// The per-step note scan (unbound action, missing control, the
    /// <c>pressUntil</c> toggle warning, the five-second duration note) must
    /// recurse into a branch's own arms - not stop at the top level, which
    /// would leave a step buried in an arm with no note at all.
    /// </summary>
    [Fact]
    public void PanelClientSource_CollectStepNotes_RecursesIntoBothBranchArms()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function collectStepNotes(steps, notes, labelPrefix)");

        Assert.Contains("collectStepNotes(step.then, notes,", body, StringComparison.Ordinal);
        Assert.Contains("collectStepNotes(step['else'], notes,", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A step inside an arm is labelled distinctly from a plain top-level
    /// step - naming the branch's own condition and which arm, so a
    /// commander reading "presses UI_Down. ..." in the notes list knows
    /// which of possibly several similar steps it is, rather than a bare
    /// step number that could belong to either arm or the top level.
    /// </summary>
    [Fact]
    public void PanelClientSource_CollectStepNotes_LabelsAnArmStepWithWhichBranchAndWhichArm()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function collectStepNotes(steps, notes, labelPrefix)");

        Assert.Contains("number + '\\'s ' + tokenListLabel(step.branch) + ' branch'", body, StringComparison.Ordinal);
        Assert.Contains("number + '\\'s otherwise branch'", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>renderMacroNotes</c> itself must actually call the recursive
    /// scanner over the top-level steps - proving the function above exists
    /// is not enough if nothing wires it in.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderMacroNotes_CallsTheRecursiveStepScanner()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderMacroNotes()");

        Assert.Contains("collectStepNotes(macroDraft.steps, notes, null);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole reason for this dispatch: <c>pressesSomething</c>/
    /// <c>checksFirst</c> must count a press/require found inside EITHER arm
    /// of a branch, not just at the top level - closing exactly the gap
    /// <c>ref/docs/macros.md</c> named for <c>disembark</c>, whose presses
    /// and requires all now live inside a branch's arms.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderMacroNotes_PressesSomethingAndChecksFirst_UseTheRecursiveMatcher()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderMacroNotes()");

        Assert.Contains(
            "const pressesSomething = macroHasStepMatching(macroDraft.steps, s => ['press', 'pressUntil', 'gotoLeftPanelTab'].includes(stepKindOf(s)));",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "const checksFirst = macroHasStepMatching(macroDraft.steps, s => stepKindOf(s) === 'require');",
            body,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The matcher's own recursion: a step matching the predicate anywhere
    /// in either arm counts, exactly as a top-level match would.
    /// </summary>
    [Fact]
    public void PanelClientSource_MacroHasStepMatching_RecursesIntoBothBranchArms()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function macroHasStepMatching(steps, predicate)");

        Assert.Contains("macroHasStepMatching(step.then, predicate)", body, StringComparison.Ordinal);
        Assert.Contains("macroHasStepMatching(step['else'], predicate)", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // [2026-09-17] The interactive arm editor (plans/expressive-kindling-
    // starfish.md's top section): a branch step's then/else arms are now
    // genuinely add/remove/reorder/edit-able, through the same step-list/
    // step-editor machinery the top level uses, parameterized around a
    // small { list, isTopLevel } context rather than the bare
    // macroDraft.steps global the single-list builder never needed to name.
    // ------------------------------------------------------------------

    /// <summary>
    /// The core of the design: ONE list renderer serves both the top level
    /// and an arm, genuinely parameterized - not a second, copy-pasted
    /// function hardcoded to a different array. Proven by reading ctx.list/
    /// ctx.isTopLevel out of the body (the parameter actually used) and
    /// confirming macroDraft.steps does not appear literally inside it (the
    /// tell a copy-pasted, still-hardcoded second renderer would leave).
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepList_IsGenuinelyParameterized_NotACopyPastedSecondRenderer()
    {
        var content = ReadPanelClientSource();
        var body = ExtractJsFunctionBody(content, "function renderStepList(ctx)");

        Assert.Contains("ctx.list", body, StringComparison.Ordinal);
        Assert.Contains("ctx.isTopLevel", body, StringComparison.Ordinal);
        Assert.DoesNotContain("macroDraft.steps", body, StringComparison.Ordinal);

        // There is exactly one step-list-rendering function in the file, not
        // a renderStepList plus a second hand-written renderArmStepList (or
        // similar) sitting beside it doing the same job for the arm case.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(content, @"function renderStepList\("));
    }

    /// <summary>
    /// stepMoveButton and the row's own remove handler take the same ctx
    /// renderStepList does, splicing ctx.list rather than a hardcoded
    /// macroDraft.steps - the other half of "genuinely parameterized",
    /// since a list renderer that took ctx but still spliced the global
    /// directly would look parameterized while still only ever working on
    /// the top level.
    /// </summary>
    [Fact]
    public void PanelClientSource_StepMoveButton_AndRemoveHandler_SpliceCtxList_NotMacroDraftSteps()
    {
        var content = ReadPanelClientSource();

        var moveBody = ExtractJsFunctionBody(content, "function stepMoveButton(ctx, index, delta, label)");
        Assert.Contains("ctx.list.splice(index, 1)", moveBody, StringComparison.Ordinal);
        Assert.Contains("ctx.list.splice(target, 0, moved)", moveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("macroDraft.steps", moveBody, StringComparison.Ordinal);

        var listBody = ExtractJsFunctionBody(content, "function renderStepList(ctx)");
        Assert.Contains("ctx.list.splice(index, 1);", listBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// refreshStepLists always re-renders the TOP level, even when told
    /// about a change to the arm - describeStep's branch case reads
    /// step.then/step['else'].length, so an add/remove inside an arm
    /// changes what the top-level row for that branch step itself says
    /// (e.g. "otherwise 3 steps" becoming "otherwise 4 steps"). A version
    /// that only re-rendered ctx's own list would leave that count stale
    /// until the commander happened to trigger some other top-level
    /// re-render.
    /// </summary>
    [Fact]
    public void PanelClientSource_RefreshStepLists_AlwaysRendersTopLevel_EvenForAnArmChange()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function refreshStepLists(ctx)");

        Assert.Contains("renderStepList(topCtx);", body, StringComparison.Ordinal);
        Assert.Contains("if (!ctx.isTopLevel) renderStepList(ctx);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// openStepEditor resolves which array a step lives in from the ctx it
    /// is given, not always macroDraft.steps - editingContext carries the
    /// SAME list reference ctx names, so renderStepEditor (below) can read
    /// the right step back whether it was opened from the top level or from
    /// inside an arm.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenStepEditor_ResolvesFromCtx_NotAlwaysMacroDraftSteps()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openStepEditor(ctx, index)");

        Assert.Contains("editingContext = { list: ctx.list, index };", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// renderStepEditor reads the step it is editing out of
    /// editingContext.list[editingContext.index] - never macroDraft.steps
    /// directly, which would silently break editing a step that lives
    /// inside an arm (it would read/write the wrong array, or the wrong
    /// index into the top-level one).
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepEditor_ReadsFromEditingContext_NotMacroDraftStepsDirectly()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        Assert.Contains("const step = editingContext.list[editingContext.index];", body, StringComparison.Ordinal);
        Assert.DoesNotContain("macroDraft.steps[", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one place outside renderStepEditor/openStepEditor that used to
    /// read the editing index directly: filling in a macro step's chosen
    /// control from the action picker. Same requirement - must resolve
    /// through editingContext, not macroDraft.steps[editingStepIndex] (the
    /// pre-arm-editor shape), or picking a control for a step living inside
    /// an arm would silently write into the wrong step.
    /// </summary>
    [Fact]
    public void PanelClientSource_OnActionPicked_MacroStepBranch_ResolvesFromEditingContext()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function onActionPicked(action)");

        Assert.Contains("const step = editingContext.list[editingContext.index];", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// renderStepKindList offers 'branch' at the top level (ctx.isTopLevel)
    /// but filters it back out whenever the picker is opened from inside an
    /// arm - the client-side half of the one-level nesting cap the engine
    /// itself enforces at load time. Belt-and-suspenders UX on top of
    /// MacroDefinition.ParseBranch's own rejection, not the only guard.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepKindList_OffersBranchOnlyAtTopLevel()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepKindList(ctx)");

        Assert.Contains(
            "macroVocabulary.stepKinds.filter(k => ctx.isTopLevel || k.kind !== 'branch')",
            body,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The "add a step" row's own click handler pushes onto ctx.list (and
    /// opens the new step through the same ctx) - never macroDraft.steps
    /// directly, which would send a step added from inside an arm's picker
    /// to the top level instead.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepKindList_AddStepHandler_PushesOntoCtxList()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepKindList(ctx)");

        Assert.Contains("ctx.list.push(step);", body, StringComparison.Ordinal);
        Assert.Contains("refreshStepLists(ctx);", body, StringComparison.Ordinal);
        Assert.Contains("openStepEditor(ctx, ctx.list.length - 1);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("macroDraft.steps.push", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The "Press a key" flow's four outcome buttons (renderKeyCaptureResult)
    /// all push onto pendingAddCtx.list - the list "add a step" was actually
    /// opened against - rather than macroDraft.steps. This flow leaves
    /// #stepKindPicker (and ctx as a local variable) behind before it knows
    /// what step it is building, which is why pendingAddCtx exists as a
    /// module-level handoff rather than a parameter here.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderKeyCaptureResult_PushesOntoPendingAddCtx_NotMacroDraftSteps()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderKeyCaptureResult(domCode, response, outcome)");

        Assert.Equal(
            4,
            System.Text.RegularExpressions.Regex.Matches(body, System.Text.RegularExpressions.Regex.Escape("pendingAddCtx.list.push(step);")).Count);
        Assert.DoesNotContain("macroDraft.steps.push", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// openArmEditor points armCtx at the real step.then/step['else'] array
    /// (creating whichever is missing on first use), never a detached copy -
    /// mutating it through the arm editor must be mutating the same array
    /// macroDraft (and therefore Save) will see.
    /// </summary>
    [Fact]
    public void PanelClientSource_OpenArmEditor_PointsArmCtxAtTheRealStepArray()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function openArmEditor(step, armField)");

        Assert.Contains("if (!step[armField]) step[armField] = [];", body, StringComparison.Ordinal);
        Assert.Contains("armCtx = { list: step[armField], isTopLevel: false };", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The branch step's own fields open the arm editor against step.then/
    /// step['else'] specifically - the "Then" button must never be wired to
    /// the "else" arm or vice versa, which would silently swap which steps
    /// run for which side of the condition.
    /// </summary>
    [Fact]
    public void PanelClientSource_RenderStepEditor_BranchButtons_OpenTheCorrectArm()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "function renderStepEditor()");

        Assert.Contains("openArmEditor(step, 'then');", body, StringComparison.Ordinal);
        Assert.Contains("openArmEditor(step, 'else');", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Cold first-launch viewport race (a commander-reported screenshot:
    // adjacent button labels overlapping on the very first paired-page
    // load in portrait, gone after rotating the device twice, never
    // recurring). ref/docs/web-client.md's "Resize and orientation
    // handling" section names the mechanism this guards: panelUrl() reads
    // innerWidth/innerHeight synchronously, and a cold mobile WebView can
    // report a stale value at that exact instant, so the server computes
    // cols/cellWidth for the WRONG viewport and layoutGrid()'s text-fit
    // pass sizes labels against it. Same "no headless browser, source-scan
    // pin only" ceiling as the rest of this file - nothing here can
    // simulate the actual mobile-browser race, only that boot() contains
    // the self-correction.
    // ------------------------------------------------------------------

    /// <summary>
    /// The double-requestAnimationFrame wait must happen BEFORE the first
    /// <c>loadPanel()</c> call in <c>boot()</c>, not after - waiting after
    /// the first fetch has already gone out with a possibly-stale viewport
    /// does nothing for the request that matters.
    /// </summary>
    [Fact]
    public void PanelClientSource_Boot_WaitsForDoubleRequestAnimationFrame_BeforeTheFirstLoadPanelCall()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function boot()");

        const string rafWait = "await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));";
        Assert.Contains(rafWait, body, StringComparison.Ordinal);

        var rafIndex = body.IndexOf(rafWait, StringComparison.Ordinal);
        var firstLoadPanelIndex = body.IndexOf("await loadPanel();", StringComparison.Ordinal);

        Assert.True(firstLoadPanelIndex >= 0, "Expected boot() to still call loadPanel().");
        Assert.True(rafIndex < firstLoadPanelIndex, "Expected the double-rAF wait to run before the first loadPanel() call.");
    }

    /// <summary>
    /// The second line of defense: exactly one automatic follow-up check,
    /// scheduled through the same <c>scheduleReload</c> path a real
    /// rotation already uses, AFTER the first <c>loadPanel()</c> call
    /// resolves. Not a second direct <c>loadPanel()</c> call and not a
    /// loop - reusing <c>scheduleReload</c> is what keeps this a no-op on
    /// an already-correct load, the same debounce a resize already relies
    /// on for that guarantee.
    /// </summary>
    [Fact]
    public void PanelClientSource_Boot_SchedulesExactlyOneAutomaticFollowUpReload_AfterTheFirstLoadPanelCall()
    {
        var body = ExtractJsFunctionBody(ReadPanelClientSource(), "async function boot()");

        Assert.Contains("scheduleReload(400);", body, StringComparison.Ordinal);

        var firstLoadPanelIndex = body.IndexOf("await loadPanel();", StringComparison.Ordinal);
        var scheduleReloadIndex = body.IndexOf("scheduleReload(400);", StringComparison.Ordinal);

        Assert.True(firstLoadPanelIndex >= 0, "Expected boot() to still call loadPanel().");
        Assert.True(scheduleReloadIndex > firstLoadPanelIndex, "Expected the follow-up scheduleReload to run after the first loadPanel() call.");

        // Never a loop and never a second bare loadPanel() call sitting
        // beside the scheduled one - boot() itself must call loadPanel()
        // exactly once; the follow-up goes through scheduleReload only.
        var loadPanelCount = 0;
        var searchFrom = 0;
        while (true)
        {
            var idx = body.IndexOf("loadPanel()", searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;
            loadPanelCount++;
            searchFrom = idx + 1;
        }

        Assert.Equal(1, loadPanelCount);
    }
}
