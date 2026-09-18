using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Theme;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Folders;

/// <summary>
/// Pins the SERVER half of folders: <see cref="SlotEditEndpoint.MakeFolder"/>,
/// the two things <see cref="PageEditEndpoint.DeletePage"/> had to learn
/// (a last-page floor that only counts real pages, and clearing the one slot
/// that pointed at a page being deleted), and what
/// <see cref="PanelEndpoint.BuildResponse"/> tells the client about pages it
/// must not draw a tab for.
///
/// The invariant most of these exist to protect is the commander's own
/// ruling: <b>nothing an ordinary slot edit does may destroy a folder's
/// interior page.</b> Clearing or reassigning a folder button leaves an
/// intact, unreferenced orphan; only an explicit delete of the interior page
/// itself removes it.
/// </summary>
public class FolderEndpointTests
{
    private static readonly IDiagnosticLog Log = new DiagnosticRingBuffer(50);

    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": { "LandingGearToggle": { "label": "GEAR", "category": "ship" } }
        }
        """;

    private const string BindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle>
                <Primary Device="Keyboard" Key="Key_G" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
        </Root>
        """;

    private static readonly HudTheme Theme = new(
        new HudColor(1, 1, 1), new HudColor(2, 2, 2), new HudColor(3, 3, 3),
        new HudColor(4, 4, 4), new HudColor(5, 5, 5), new HudColor(6, 6, 6),
        HudThemeSource.Stock, Array.Empty<string>());

    private static LayoutSlot FolderSlot(int index, string id) => new(index, null, null, null, null, FolderPage: id);

    private static LayoutPage Interior(string name, string id, params LayoutSlot[] slots) =>
        new(name, "t6", slots, Array.Empty<LayoutSlot>(), ShowWhen: null, Id: id, IsFolderOwned: true);

    private static LayoutPage TopLevel(string name, params LayoutSlot[] slots) =>
        new(name, "t6", slots, Array.Empty<LayoutSlot>());

    // ------------------------------------------------------------------
    // SlotEditEndpoint.MakeFolder
    // ------------------------------------------------------------------

    [Fact]
    public void MakeFolder_OnAnEmptySlot_AppendsAFolderOwnedPage_AndWritesAFolderOnlySlot()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP") });

        var result = SlotEditEndpoint.MakeFolder(layout, 0, 2, "THRUST", "t6");

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var edited = result.Layout!;
        Assert.Equal(2, edited.Pages.Count);

        var newPage = edited.Pages[1];
        Assert.Equal("THRUST", newPage.Name);
        Assert.Equal("t6", newPage.TemplateId);
        Assert.True(newPage.IsFolderOwned);
        Assert.False(string.IsNullOrWhiteSpace(newPage.Id));
        Assert.Empty(newPage.Slots);

        var slot = Assert.Single(edited.Pages[0].Slots);
        Assert.Equal(2, slot.Index);
        Assert.Equal(newPage.Id, slot.FolderPage);
        Assert.Null(slot.Action);
        Assert.Null(slot.Macro);

        // The index the client navigates straight into.
        Assert.Equal(1, result.PageIndex);
    }

    /// <summary>
    /// Every id minted here must be distinct, or two folder buttons would
    /// open the same interior and <see cref="LayoutValidator"/>'s
    /// no-shared-target rule would refuse the very layout this endpoint just
    /// produced. A constant id (or one derived from the page name, which is
    /// mutable and non-unique) is exactly the mistake the stable-id design
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void MakeFolder_TwiceOnOnePage_MintsTwoDifferentIds_AndTheResultValidates()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP") });

        var first = SlotEditEndpoint.MakeFolder(layout, 0, 0, "ONE", "t6");
        var second = SlotEditEndpoint.MakeFolder(first.Layout!, 0, 1, "TWO", "t6");

        var edited = second.Layout!;
        Assert.NotEqual(edited.Pages[1].Id, edited.Pages[2].Id);
        var validation = LayoutValidator.ValidateForLoad(edited);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
    }

    /// <summary>
    /// Same reasoning <see cref="SlotEditEndpoint.Assign"/> spells out for
    /// changing what a button does: a latch or a hold is a MODE one key is
    /// fired in, and a folder fires no key at all - carrying either across
    /// would leave a key held down with nothing left on the button able to
    /// release it.
    /// </summary>
    [Fact]
    public void MakeFolder_OverAHeldOrLatchedSlot_DropsTheGestureAndTheLongPress()
    {
        var page = TopLevel("SHIP", new LayoutSlot(0, "LandingGearToggle", null, "GEAR", new LongPressAction("LandingGearToggle", null), Latch: true));
        var layout = new Layout(1, new[] { page });

        var result = SlotEditEndpoint.MakeFolder(layout, 0, 0, "THRUST", "t6");

        var slot = Assert.Single(result.Layout!.Pages[0].Slots);
        Assert.False(slot.Latch);
        Assert.False(slot.Hold);
        Assert.Null(slot.LongPress);
        Assert.Null(slot.Action);
        Assert.NotNull(slot.FolderPage);
    }

    [Fact]
    public void MakeFolder_UnknownTemplateId_IsRefused_AndChangesNothing()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP") });

        var result = SlotEditEndpoint.MakeFolder(layout, 0, 0, "THRUST", "t99");

        Assert.Equal(SlotEditEndpoint.EditOutcome.UnknownTemplate, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void MakeFolder_SlotIndexOutsideTheTemplatesRange_IsRefused()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP") });

        var result = SlotEditEndpoint.MakeFolder(layout, 0, 6, "THRUST", "t6");

        Assert.Equal(SlotEditEndpoint.EditOutcome.IndexOutOfRange, result.Outcome);
    }

    [Fact]
    public void MakeFolder_NoSuchPage_IsRefused()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP") });

        var result = SlotEditEndpoint.MakeFolder(layout, 5, 0, "THRUST", "t6");

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
    }

    /// <summary>
    /// THE ruling this whole feature was built around. Clearing the button
    /// leaves the interior page present, still marked folder-owned, still
    /// holding every button on it - just unreferenced.
    /// </summary>
    [Fact]
    public void Clear_OnAFolderSlot_LeavesTheInteriorPageIntactAndUnreferenced()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa", new LayoutSlot(0, "LandingGearToggle", null, null, null)),
        });

        var result = SlotEditEndpoint.Clear(layout, 0, 0);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var edited = result.Layout!;
        Assert.Equal(2, edited.Pages.Count);
        Assert.True(edited.Pages[1].IsFolderOwned);
        Assert.Single(edited.Pages[1].Slots);
        Assert.Empty(edited.Pages[0].Slots);
        Assert.DoesNotContain(edited.Pages.SelectMany(p => p.Slots), s => s.FolderPage is not null);
    }

    [Fact]
    public void Assign_OverAFolderSlot_LeavesTheInteriorPageIntact_AndDropsTheFolderReference()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa", new LayoutSlot(0, "LandingGearToggle", null, null, null)),
        });

        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var edited = result.Layout!;
        var slot = Assert.Single(edited.Pages[0].Slots);
        Assert.Equal("LandingGearToggle", slot.Action);
        Assert.Null(slot.FolderPage);
        Assert.Single(edited.Pages[1].Slots);
        Assert.True(LayoutValidator.ValidateForLoad(edited).IsValid);
    }

    /// <summary>
    /// A move changes WHERE a button is, never what it does - the same
    /// asymmetry against Assign that <see cref="SlotEditEndpoint.Move"/>
    /// already documents for Latch. The folder reference has to ride across
    /// the swap untouched or dragging a folder would silently orphan its own
    /// interior page.
    /// </summary>
    [Fact]
    public void Move_CarriesAFolderReferenceAcrossTheSwapUntouched()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa"), new LayoutSlot(3, "LandingGearToggle", null, null, null)),
            Interior("THRUST", "aaa"),
        });

        var result = SlotEditEndpoint.Move(layout, 0, 0, 3);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slots = result.Layout!.Pages[0].Slots;
        Assert.Equal("aaa", slots.Single(s => s.Index == 3).FolderPage);
        Assert.Equal("LandingGearToggle", slots.Single(s => s.Index == 0).Action);
        Assert.True(LayoutValidator.ValidateForLoad(result.Layout!).IsValid);
    }

    // ------------------------------------------------------------------
    // PageEditEndpoint.DeletePage
    // ------------------------------------------------------------------

    /// <summary>
    /// The floor exists so a layout can never end up with no page to draw.
    /// A folder's interior is not a page the commander can navigate to
    /// directly, so it cannot stand in for the last real one - a layout of
    /// "one real page plus three folders" must still refuse to delete the
    /// real page.
    /// </summary>
    [Fact]
    public void DeletePage_LastRealPage_IsRefusedEvenWhenFolderPagesRemain()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = PageEditEndpoint.DeletePage(layout, 0);

        Assert.Equal(PageEditEndpoint.EditOutcome.CannotDeleteLastPage, result.Outcome);
    }

    /// <summary>
    /// The other side of the same rule, and the one the commander's
    /// "delete this folder and everything in it" action actually needs: with
    /// exactly one real page and one folder, deleting the FOLDER must work -
    /// a floor counted over the raw page list would refuse it and leave the
    /// folder undeletable.
    /// </summary>
    [Fact]
    public void DeletePage_AFolderPage_IsAllowedEvenWhenOnlyOneRealPageRemains()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var result = PageEditEndpoint.DeletePage(layout, 1);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Single(result.Layout!.Pages);
    }

    [Fact]
    public void DeletePage_AFolderPage_ClearsTheOneSlotThatPointedAtIt()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa"), new LayoutSlot(1, "LandingGearToggle", null, null, null)),
            TopLevel("SRV"),
            Interior("THRUST", "aaa"),
        });

        var result = PageEditEndpoint.DeletePage(layout, 2);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        var edited = result.Layout!;
        Assert.DoesNotContain(edited.Pages.SelectMany(p => p.Slots), s => s.FolderPage is not null);
        // The neighbouring ordinary button is untouched - only the dangling
        // one is removed.
        Assert.Equal("LandingGearToggle", Assert.Single(edited.Pages[0].Slots).Action);
        Assert.True(LayoutValidator.ValidateForLoad(edited).IsValid, string.Join("; ", LayoutValidator.ValidateForLoad(edited).Errors));
    }

    /// <summary>
    /// A PARKED folder button (its index fell outside a shrunken template)
    /// still holds a reference that would dangle. Cleaning only the active
    /// slots would leave a layout that fails validation on the very next
    /// save, with nothing on screen to explain it.
    /// </summary>
    [Fact]
    public void DeletePage_AFolderPage_AlsoClearsAParkedSlotThatPointedAtIt()
    {
        var owner = new LayoutPage("SHIP", "t6", Array.Empty<LayoutSlot>(), new[] { FolderSlot(9, "aaa") }, null, null, false);
        var layout = new Layout(1, new[] { owner, TopLevel("SRV"), Interior("THRUST", "aaa") });

        var result = PageEditEndpoint.DeletePage(layout, 2);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Empty(result.Layout!.Pages[0].Parked);
    }

    [Fact]
    public void DeletePage_AnOrdinaryPage_NeverTouchesAnUnrelatedFolderSlot()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            TopLevel("SRV"),
            Interior("THRUST", "aaa"),
        });

        var result = PageEditEndpoint.DeletePage(layout, 1);

        Assert.Equal(PageEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal("aaa", Assert.Single(result.Layout!.Pages[0].Slots).FolderPage);
    }

    // ------------------------------------------------------------------
    // PanelEndpoint
    // ------------------------------------------------------------------

    private static (LunaPanel.Core.Catalogue.Catalogue Catalogue, IReadOnlyList<CataloguePickerEntry> Merge, IReadOnlySet<string> Known) PanelInputs()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindings = BindingsFile.Parse(BindingsXml);
        Assert.True(bindings.Success, bindings.Error);
        return (catalogue, CatalogueMerger.Merge(catalogue, bindings.File!), bindings.File!.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal));
    }

    private static PanelEndpoint.PanelResponse Build(Layout layout, int pageIndex)
    {
        var (catalogue, merge, known) = PanelInputs();
        var result = PanelEndpoint.BuildResponse(layout, pageIndex, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);
        Assert.Equal(PanelEndpoint.BuildOutcome.Ok, result.Outcome);
        return result.Response!;
    }

    /// <summary>
    /// A folder's interior is reached through its button, never through a
    /// tab - the commander's own ruling. Excluding it breaks the old
    /// "array position == page index" assumption the tab row was built on,
    /// which is what <c>PageIndices</c> exists to repair: the tab at
    /// position <c>i</c> addresses page <c>pageIndices[i]</c>, not <c>i</c>.
    /// </summary>
    [Fact]
    public void BuildResponse_PageNamesExcludesFolderPages_AndPageIndicesGivesTheirRealIndices()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
            TopLevel("SRV"),
        });

        var response = Build(layout, 0);

        Assert.Equal(new[] { "SHIP", "SRV" }, response.PageNames);
        Assert.Equal(new[] { 0, 2 }, response.PageIndices);
    }

    [Fact]
    public void BuildResponse_ALayoutWithNoFolders_HasPageIndicesMatchingPositionExactly()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP"), TopLevel("SRV"), TopLevel("FOOT") });

        var response = Build(layout, 0);

        Assert.Equal(new[] { 0, 1, 2 }, response.PageIndices);
        Assert.Equal(response.PageNames.Count, response.PageIndices.Count);
    }

    [Fact]
    public void BuildResponse_FolderSlot_CarriesIsFolderAndTheResolvedPageIndex()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SRV"),
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var response = Build(layout, 1);

        var slot = Assert.Single(response.Slots);
        Assert.True(slot.IsFolder);
        Assert.Equal(2, slot.FolderPageIndex);
        Assert.Equal("THRUST", slot.Label);
        Assert.Equal("Ok", slot.Status);
    }

    [Fact]
    public void BuildResponse_AnOrdinarySlot_IsNotAFolder_AndCarriesNoFolderPageIndex()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP", new LayoutSlot(0, "LandingGearToggle", null, null, null)) });

        var slot = Assert.Single(Build(layout, 0).Slots);

        Assert.False(slot.IsFolder);
        Assert.Null(slot.FolderPageIndex);
    }

    /// <summary>
    /// The signal the client uses to replace the whole tab row with the
    /// "up one level" control. Set ONLY while the requested page is a
    /// folder's interior - on the parent page itself it must be absent, or
    /// the tabs would never come back.
    /// </summary>
    [Fact]
    public void BuildResponse_ViewingAFolderPage_ReportsTheOwningPageByIndexAndName()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SRV"),
            TopLevel("SHIP", FolderSlot(4, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var response = Build(layout, 2);

        Assert.Equal(1, response.ParentPageIndex);
        Assert.Equal("SHIP", response.ParentPageName);
    }

    [Fact]
    public void BuildResponse_ViewingAnOrdinaryPage_ReportsNoParent()
    {
        var layout = new Layout(1, new[]
        {
            TopLevel("SHIP", FolderSlot(0, "aaa")),
            Interior("THRUST", "aaa"),
        });

        var response = Build(layout, 0);

        Assert.Null(response.ParentPageIndex);
        Assert.Null(response.ParentPageName);
    }

    /// <summary>
    /// An orphaned interior (its folder button cleared) is still reachable by
    /// index - an in-flight client, or the page the commander was standing on
    /// when they cleared the button elsewhere. It must report no parent
    /// rather than inventing one, and must not throw.
    /// </summary>
    [Fact]
    public void BuildResponse_ViewingAnOrphanedFolderPage_ReportsNoParent()
    {
        var layout = new Layout(1, new[] { TopLevel("SHIP"), Interior("THRUST", "aaa") });

        var response = Build(layout, 1);

        Assert.Null(response.ParentPageIndex);
        Assert.Null(response.ParentPageName);
    }
}
