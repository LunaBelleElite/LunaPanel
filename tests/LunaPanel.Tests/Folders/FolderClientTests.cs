using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Folders;

/// <summary>
/// The client half of folders, pinned the only way anything on that page can
/// be pinned in this suite: as static content (<c>ref/docs/web-client.md</c>'s
/// "What's tested, and what plainly isn't"). There is no headless browser
/// here, so nothing below drives a tap - these assert that the page a
/// commander is served contains the navigation branch, the replaced tab row,
/// the three folder affordances and the route, not that tapping them works.
///
/// Every needle is scoped to ONE function body or ONE CSS rule. A needle
/// searched across the whole ~6,000-line page would be satisfied by a comment
/// or by an unrelated handler, which is the difference between a pin and a
/// decoration.
/// </summary>
public class FolderClientTests
{
    private static string CssRuleBody(string html, string selector)
    {
        var start = html.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected selector '{selector}' in the page.");
        var open = html.IndexOf('{', start);
        var close = html.IndexOf('}', open);
        Assert.True(close > open, $"Expected a body for '{selector}'.");
        return html.Substring(open, close - open);
    }

    private static string JsFunctionBody(string html, string declaration)
    {
        var start = html.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{declaration}' in the page.");
        var open = html.IndexOf('{', start + declaration.Length);
        Assert.True(open >= 0, $"Expected a body after '{declaration}'.");

        var depth = 0;
        for (var i = open; i < html.Length; i++)
        {
            if (html[i] == '{')
            {
                depth++;
            }
            else if (html[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return html.Substring(open, i - open + 1);
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after '{declaration}'.");
    }

    /// <summary>
    /// The navigation branch, and specifically that it sits BEFORE the
    /// degraded-slot check. A folder is not a keystroke: it has no binding to
    /// be missing, so anything that routed it through the "this button is not
    /// set up yet" refusal first would make an entirely healthy folder
    /// untappable the moment its own status was ever anything but Ok.
    /// </summary>
    [Fact]
    public void BuildPage_OnSlotTap_NavigatesIntoAFolderBeforeCheckingStatus()
    {
        var body = JsFunctionBody(PanelClientEndpoint.BuildPage(), "async function onSlotTap(slot)");

        var folderBranch = body.IndexOf("slot.isFolder", StringComparison.Ordinal);
        var statusCheck = body.IndexOf("slot.status !== 'Ok'", StringComparison.Ordinal);

        Assert.True(folderBranch >= 0, "onSlotTap must branch on slot.isFolder.");
        Assert.True(statusCheck >= 0, "onSlotTap must still refuse a degraded slot.");
        Assert.True(folderBranch < statusCheck, "The folder branch must come before the degraded-slot refusal.");
        Assert.Contains("requestPageSwitch(slot.folderPageIndex)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder-owned page is excluded from <c>pageNames</c>, which destroys
    /// the old "array position == page index" assumption every tab was wired
    /// with. Each tab must address <c>pageIndices[i]</c> instead - otherwise
    /// tapping the second tab of a layout whose first page owns a folder
    /// switches to the folder's interior.
    /// </summary>
    [Fact]
    public void BuildPage_RenderTabs_AddressesEachTabByItsRealPageIndex_NotItsPositionInTheRow()
    {
        var body = JsFunctionBody(PanelClientEndpoint.BuildPage(), "function renderTabs(data)");

        // The ASSIGNMENT, not merely the name. Rewritten after it failed to
        // bite: the first version asserted that "data.pageIndices[" appeared
        // in this function and that realIndex was the thing handed around -
        // and the mutation `const realIndex = index;` left BOTH true (the
        // phrase also occurs in this function's own comment, and the
        // variable kept its name), so the pin passed over exactly the defect
        // it exists to catch. Predicted 1 red, got 0. What has to be pinned
        // is where realIndex comes FROM.
        Assert.Contains("const realIndex = data.pageIndices[index];", body, StringComparison.Ordinal);
        // And that nothing downstream quietly goes back to the loop
        // position: the gesture wiring (tap-to-switch and drag-to-reorder),
        // the active-tab test, and the drag-target registry all consume it.
        Assert.Contains("wireTabGesture(b, realIndex)", body, StringComparison.Ordinal);
        Assert.Contains("realIndex === data.pageIndex", body, StringComparison.Ordinal);
        Assert.Contains("tabButtons.push({ index: realIndex", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The commander's explicit choice: inside a folder the ordinary tabs are
    /// REPLACED, not supplemented. Pinned positionally - the up-bar branch
    /// must return before the ordinary tab loop is ever reached - because a
    /// version that drew the up control and then fell through into the normal
    /// row would satisfy any "contains tabUp" assertion while showing exactly
    /// what was ruled out.
    /// </summary>
    [Fact]
    public void BuildPage_RenderTabs_InsideAFolder_ReplacesTheWholeTabRowWithTheUpControl()
    {
        var body = JsFunctionBody(PanelClientEndpoint.BuildPage(), "function renderTabs(data)");

        var guard = body.IndexOf("data.parentPageIndex", StringComparison.Ordinal);
        var upControl = body.IndexOf("tabUp", StringComparison.Ordinal);
        var folderLabel = body.IndexOf("tabFolderName", StringComparison.Ordinal);
        var ordinaryTabs = body.IndexOf("data.pageNames.forEach", StringComparison.Ordinal);

        Assert.True(guard >= 0, "renderTabs must consult data.parentPageIndex.");
        Assert.True(upControl >= 0, "renderTabs must build the .tabUp control.");
        Assert.True(folderLabel >= 0, "renderTabs must build the current-folder name label.");
        Assert.True(ordinaryTabs >= 0, "renderTabs must still draw ordinary tabs outside a folder.");
        Assert.True(upControl < ordinaryTabs, "The up control must be built in the early branch, before the ordinary tab row.");

        // The early branch must actually END the render - a bare `return;`
        // between the up control and the ordinary loop.
        var earlyReturn = body.IndexOf("return;", upControl, StringComparison.Ordinal);
        Assert.True(earlyReturn >= 0 && earlyReturn < ordinaryTabs, "The folder tab row must return before the ordinary tab row is drawn.");

        Assert.Contains("data.parentPageName", body, StringComparison.Ordinal);
        Assert.Contains("requestPageSwitch(data.parentPageIndex)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-tappable label, per the commander's ruling - the current folder's
    /// name is information, not a control. A <c>&lt;button&gt;</c> here would
    /// read as "tap me to go somewhere" on a touch surface and go nowhere.
    /// </summary>
    [Fact]
    public void BuildPage_TheCurrentFolderNameLabel_IsNotAButton()
    {
        var body = JsFunctionBody(PanelClientEndpoint.BuildPage(), "function renderTabs(data)");

        var labelAt = body.IndexOf("tabFolderName", StringComparison.Ordinal);
        // The element created immediately before the class is applied - read
        // backwards from the class assignment to the nearest createElement.
        var created = body.LastIndexOf("createElement(", labelAt, StringComparison.Ordinal);
        Assert.True(created >= 0, "Expected the folder-name label to be created with document.createElement.");
        var createdTag = body.Substring(created, 30);
        Assert.DoesNotContain("'button'", createdTag, StringComparison.Ordinal);
    }

    /// <summary>
    /// Same discoverability treatment <c>.slot.has-long-press</c> and
    /// <c>.slot.has-latch</c> already get: a folder has to be
    /// distinguishable from a plain button before it is tapped.
    /// </summary>
    [Fact]
    public void BuildPage_FolderSlot_GetsItsOwnVisualAffordance()
    {
        var html = PanelClientEndpoint.BuildPage();

        var body = CssRuleBody(html, ".slot.is-folder");
        Assert.True(body.Length > 2, "Expected .slot.is-folder to declare something.");
        Assert.Contains("is-folder", JsFunctionBody(html, "function layoutGrid(data)"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty-slot sheet's third choice (<c>ref/docs/editor.md</c>), and
    /// the two a folder slot gets instead of the action/macro controls.
    /// </summary>
    [Fact]
    public void BuildPage_SlotSheet_CarriesTheThreeFolderControls()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains("id=\"slotSheetMakeFolder\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheetOpenFolder\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"slotSheetDeleteFolder\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The slot sheet's own shape rule: a folder fires no key, so every
    /// key-shaped control has to be hidden on it - the same courtesy a macro
    /// slot already gets from <c>isMacroSlot</c>, and for the same reason
    /// (the server refusal stays; the button being absent is the courtesy).
    /// </summary>
    [Fact]
    public void BuildPage_OpenSlotSheet_HidesEveryKeyShapedControlOnAFolderSlot()
    {
        var body = JsFunctionBody(PanelClientEndpoint.BuildPage(), "function openSlotSheet(index, slot)");

        Assert.Contains("const isFolderSlot = !!(slot && slot.isFolder)", body, StringComparison.Ordinal);
        // One assertion per control, each naming its own element. Rewritten
        // after it failed to bite: the first version asserted the shared
        // condition text "!slot || isMacroSlot || isFolderSlot" once, and
        // three controls produce that same text - so removing isFolderSlot
        // from the LATCH line left the HOLD line still satisfying the
        // needle, and the pin stayed green over a folder slot that now
        // offered a latch toggle. Predicted 2 reds, got 1.
        Assert.Contains("el('slotSheetLatch').classList.toggle('hidden', !slot || isMacroSlot || isFolderSlot);", body, StringComparison.Ordinal);
        Assert.Contains("el('slotSheetHold').classList.toggle('hidden', !slot || isMacroSlot || isFolderSlot);", body, StringComparison.Ordinal);
        Assert.Contains("el('slotSheetLongPress').classList.toggle('hidden', !slot || isFolderSlot);", body, StringComparison.Ordinal);
        // The three folder controls are each shown/hidden from this same
        // flag, never left permanently visible - asserted as whole lines for
        // the same reason as above, and because "Make this a folder" in
        // particular must be offered on an EMPTY slot only: offering it on an
        // occupied one would read as a way to convert a button, which is not
        // what it does (it replaces it, orphaning nothing but the action).
        Assert.Contains("el('slotSheetMakeFolder').classList.toggle('hidden', !!slot);", body, StringComparison.Ordinal);
        Assert.Contains("el('slotSheetOpenFolder').classList.toggle('hidden', !isFolderSlot);", body, StringComparison.Ordinal);
        Assert.Contains("el('slotSheetDeleteFolder').classList.toggle('hidden', !isFolderSlot);", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ruling this feature is most likely to lose by accident: Clear must
    /// keep the folder's interior page. The two verbs therefore hit two
    /// DIFFERENT routes - Clear posts to the slot-clear route (which cannot
    /// remove a page at all), and only the explicit delete posts to the
    /// page-delete route. A single shared handler is exactly how "clear" would
    /// quietly start destroying pages full of buttons.
    /// </summary>
    [Fact]
    public void BuildPage_ClearingAFolderButton_UsesTheSlotClearRoute_WhileDeletingAFolderUsesThePageDeleteRoute()
    {
        var html = PanelClientEndpoint.BuildPage();

        var clearHandler = JsFunctionBody(html, "el('slotSheetClear').addEventListener('click', async ()");
        Assert.Contains(ApiPaths.SlotClear, clearHandler, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiPaths.PageDelete, clearHandler, StringComparison.Ordinal);

        var deleteHandler = JsFunctionBody(html, "el('slotSheetDeleteFolder').addEventListener('click', async ()");
        Assert.Contains(ApiPaths.PageDelete, deleteHandler, StringComparison.Ordinal);
        Assert.Contains("confirm(", deleteHandler, StringComparison.Ordinal);
    }

    /// <summary>
    /// The new route reaches the page as a real path, substituted from
    /// <see cref="ApiPaths"/> rather than typed as a second literal - the same
    /// "one place this is spelled" discipline every other <c>__API_*__</c>
    /// token gets.
    /// </summary>
    [Fact]
    public void BuildPage_SubstitutesTheMakeFolderRoute_LeavingNoPlaceholderBehind()
    {
        var html = PanelClientEndpoint.BuildPage();

        Assert.Contains(ApiPaths.SlotMakeFolder, html, StringComparison.Ordinal);
        Assert.DoesNotContain("__API_SLOT_MAKE_FOLDER__", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// A defect folders INTRODUCE into an existing flow, fixed in the same
    /// change rather than left to be found later. Deleting the page you are
    /// standing on used to clamp <c>currentPage</c> to
    /// <c>pageCount - 1</c> - and <c>pageCount</c> counts folder pages,
    /// which have no tab. With any folder page sitting after the deleted
    /// one, that clamp lands on a folder's interior: a page with no tab and
    /// an "up one level" bar, shown after deleting something else entirely.
    /// The replacement clamps to a real page, via the same
    /// <c>pageIndices</c> array the tab row is built from.
    /// </summary>
    [Fact]
    public void BuildPage_DeletingTheCurrentPage_ClampsToARealPage_NeverToAFolderInterior()
    {
        var body = JsFunctionBody(
            PanelClientEndpoint.BuildPage(),
            "el('pageSettingsDelete').addEventListener('click', async ()");

        Assert.DoesNotContain("Math.min(deletedIndex, body.pageCount - 1)", body, StringComparison.Ordinal);
        Assert.Contains("lastPanelData.pageIndices", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Renaming a folder renames its interior PAGE, not the slot's label
    /// override - a folder's name and its page's name are the same thing
    /// (a real folder's mental model), and two independently-editable names
    /// for one object is how they end up disagreeing on screen.
    /// </summary>
    [Fact]
    public void BuildPage_RenamingAFolder_GoesToThePageRenameRoute_NotTheSlotLabelRoute()
    {
        var html = PanelClientEndpoint.BuildPage();
        var body = JsFunctionBody(html, "el('slotSheetRename').addEventListener('click', ()");

        Assert.Contains("isFolder", body, StringComparison.Ordinal);
        Assert.Contains(ApiPaths.PageRename, body, StringComparison.Ordinal);
    }
}
