using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="LayoutSpiller.ChangeTemplate"/>: shrinking spills surplus
/// slots forward onto further pages (compacted, in original reading order),
/// growing pulls them back and removes any page left empty by that, no slot
/// is ever destroyed, and the function is pure - matching
/// <c>ref/docs/panels-and-pages.md</c>'s "Spill, not park, as what a
/// commander normally meets".
/// </summary>
public class LayoutSpillerTests
{
    private static PanelTemplate Template(string id)
    {
        Assert.True(Templates.TryGet(id, out var template), $"Expected template '{id}' to exist.");
        return template!;
    }

    private static LayoutSlot Slot(int index, string action) => new(index, action, null, null, null);

    /// <summary>A page with every index 0..(count-1) assigned - the dense, no-gaps case the "12/12/6" example describes.</summary>
    private static LayoutPage DensePage(string name, string templateId, int count) =>
        new(name, templateId, Enumerable.Range(0, count).Select(i => Slot(i, $"Action{i}")).ToArray(), Array.Empty<LayoutSlot>());

    [Fact]
    public void ChangeTemplate_Shrink_T30ToT12_Spills12_12_6_InOriginalReadingOrder()
    {
        var layout = new Layout(1, new[] { DensePage("SHIP", "t30", 30) });

        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t12"));

        Assert.Equal(3, result.Pages.Count);
        Assert.All(result.Pages, p => Assert.Equal("t12", p.TemplateId));
        Assert.Equal(12, result.Pages[0].Slots.Count);
        Assert.Equal(12, result.Pages[1].Slots.Count);
        Assert.Equal(6, result.Pages[2].Slots.Count);

        // Original reading order preserved: page 0 keeps Action0..Action11,
        // page 1 gets Action12..Action23, page 2 gets the remaining 6.
        Assert.Equal(Enumerable.Range(0, 12).Select(i => $"Action{i}"), result.Pages[0].Slots.OrderBy(s => s.Index).Select(s => s.Action));
        Assert.Equal(Enumerable.Range(12, 12).Select(i => $"Action{i}"), result.Pages[1].Slots.OrderBy(s => s.Index).Select(s => s.Action));
        Assert.Equal(Enumerable.Range(24, 6).Select(i => $"Action{i}"), result.Pages[2].Slots.OrderBy(s => s.Index).Select(s => s.Action));

        // Compacted to sequential indices on each page, not the original
        // absolute index - this is what turns 18 surplus slots into
        // "12 / 12 / 6" rather than a sparse page 1 with slots at 12..29.
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, result.Pages[2].Slots.OrderBy(s => s.Index).Select(s => s.Index));

        // The origin page keeps its own name; spill-created pages get a
        // generated default (renameable later, not now).
        Assert.Equal("SHIP", result.Pages[0].Name);
        Assert.Equal("PANEL 2", result.Pages[1].Name);
        Assert.Equal("PANEL 3", result.Pages[2].Name);
    }

    [Fact]
    public void ChangeTemplate_Shrink_NoSlotIsEverDestroyed_TotalCountIsConserved()
    {
        var layout = new Layout(1, new[] { DensePage("SHIP", "t30", 30) });

        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t12"));

        var totalSlots = result.Pages.Sum(p => p.Slots.Count + p.Parked.Count);
        Assert.Equal(30, totalSlots);
        var allActions = result.Pages.SelectMany(p => p.Slots).Select(s => s.Action).OrderBy(a => a, StringComparer.Ordinal);
        var expected = Enumerable.Range(0, 30).Select(i => $"Action{i}").OrderBy(a => a, StringComparer.Ordinal);
        Assert.Equal(expected, allActions);
    }

    [Fact]
    public void ChangeTemplate_ShrinkThenGrowBack_ReproducesTheOriginalArrangement_ExactlyForADensePage()
    {
        var original = new Layout(1, new[] { DensePage("SHIP", "t30", 30) });

        var shrunk = LayoutSpiller.ChangeTemplate(original, 0, Template("t12"));
        var grownBack = LayoutSpiller.ChangeTemplate(shrunk, 0, Template("t30"));

        Assert.Equal(original.Pages.Count, grownBack.Pages.Count);
        var originalPage = original.Pages[0];
        var grownPage = grownBack.Pages[0];
        Assert.Equal(originalPage.TemplateId, grownPage.TemplateId);
        Assert.Equal(originalPage.Name, grownPage.Name);
        Assert.Equal(
            originalPage.Slots.OrderBy(s => s.Index).Select(s => (s.Index, s.Action)),
            grownPage.Slots.OrderBy(s => s.Index).Select(s => (s.Index, s.Action)));
        Assert.Empty(grownPage.Parked);
    }

    [Fact]
    public void ChangeTemplate_Grow_RemovesASpillPageLeftCompletelyEmpty()
    {
        var original = new Layout(1, new[] { DensePage("SHIP", "t30", 30) });
        var shrunkTo6 = LayoutSpiller.ChangeTemplate(original, 0, Template("t6"));
        // t6 holds 6 slots/page -> 30 slots spills across exactly 5 pages, none empty yet.
        Assert.Equal(5, shrunkTo6.Pages.Count);

        // Growing to t24 needs only 2 pages (24 + 6) for the same 30 slots -
        // the remaining 3 pages that t6 needed must be removed, not left
        // behind empty.
        var grownTo24 = LayoutSpiller.ChangeTemplate(shrunkTo6, 0, Template("t24"));

        Assert.Equal(2, grownTo24.Pages.Count);
        Assert.Equal(24, grownTo24.Pages[0].Slots.Count);
        Assert.Equal(6, grownTo24.Pages[1].Slots.Count);
    }

    [Fact]
    public void ChangeTemplate_OriginPageIsNeverRemoved_EvenWhenItEndsUpEmpty()
    {
        var emptyOrigin = new LayoutPage("SHIP", "t30", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>());
        var layout = new Layout(1, new[] { emptyOrigin });

        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t6"));

        var page = Assert.Single(result.Pages);
        Assert.Equal("t6", page.TemplateId);
        Assert.Empty(page.Slots);
    }

    [Fact]
    public void ChangeTemplate_AnExistingLaterPageWithADifferentTemplate_IsNeverTouched()
    {
        var independentPage = new LayoutPage("SRV", "t9", new[] { Slot(0, "SrvAction") }, Array.Empty<LayoutSlot>());
        var layout = new Layout(1, new[] { DensePage("SHIP", "t30", 30), independentPage });

        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t12"));

        // The origin's own spill created 3 pages (t12/t12/t12); the
        // independent t9 page must still be present, unchanged, after them.
        Assert.Equal(4, result.Pages.Count);
        var last = result.Pages[^1];
        Assert.Equal("SRV", last.Name);
        Assert.Equal("t9", last.TemplateId);
        Assert.Equal("SrvAction", Assert.Single(last.Slots).Action);
    }

    [Fact]
    public void ChangeTemplate_OriginsExistingParkedSlots_AreAbsorbedBackIntoTheSpillPool_NeverLeftParked()
    {
        var page = new LayoutPage(
            "SHIP",
            "t12",
            new[] { Slot(0, "Kept") },
            new[] { Slot(11, "WasParked") }); // stray parked slot from an earlier OFF-setting shrink
        var layout = new Layout(1, new[] { page });

        // Growing to t30 should pull the parked slot back into normal
        // circulation - spill's own output never carries a Parked list.
        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t30"));

        var resultPage = Assert.Single(result.Pages);
        Assert.Empty(resultPage.Parked);
        Assert.Equal(2, resultPage.Slots.Count);
        Assert.Contains(resultPage.Slots, s => s.Action == "Kept");
        Assert.Contains(resultPage.Slots, s => s.Action == "WasParked");
    }

    [Fact]
    public void ChangeTemplate_DoesNotMutateTheOriginalLayoutOrItsPages()
    {
        var original = new Layout(1, new[] { DensePage("SHIP", "t30", 30) });

        LayoutSpiller.ChangeTemplate(original, 0, Template("t12"));

        Assert.Single(original.Pages);
        Assert.Equal("t30", original.Pages[0].TemplateId);
        Assert.Equal(30, original.Pages[0].Slots.Count);
    }

    [Fact]
    public void ChangeTemplate_CalledTwiceOnTheSameInputs_ProducesTheSameResult()
    {
        var original = new Layout(1, new[] { DensePage("SHIP", "t30", 30) });

        var first = LayoutSpiller.ChangeTemplate(original, 0, Template("t12"));
        var second = LayoutSpiller.ChangeTemplate(original, 0, Template("t12"));

        Assert.Equal(first.Pages.Count, second.Pages.Count);
        for (var i = 0; i < first.Pages.Count; i++)
        {
            Assert.Equal(first.Pages[i].Slots.Select(s => (s.Index, s.Action)), second.Pages[i].Slots.Select(s => (s.Index, s.Action)));
        }
    }

    [Fact]
    public void ChangeTemplate_InvalidPageIndex_Throws()
    {
        var layout = new Layout(1, new[] { DensePage("SHIP", "t6", 6) });

        Assert.Throws<ArgumentOutOfRangeException>(() => LayoutSpiller.ChangeTemplate(layout, 1, Template("t6")));
        Assert.Throws<ArgumentOutOfRangeException>(() => LayoutSpiller.ChangeTemplate(layout, -1, Template("t6")));
    }

    /// <summary>
    /// A page's declared vessel context (<c>ref/docs/vessel-context.md</c>)
    /// belongs to the page the commander built, exactly as its NAME does -
    /// and this function preserves the origin page's name on the first
    /// resulting page. Losing the context on a template change would leave a
    /// commander's SRV page silently never appearing again, with a resize as
    /// the only clue.
    /// </summary>
    [Fact]
    public void ChangeTemplate_TheOriginPagesShowWhen_SurvivesASpill()
    {
        var origin = DensePage("NOMAD", "t30", 30) with { ShowWhen = new[] { "InSrv", "Vessel:lander01" } };
        var layout = new Layout(1, new[] { origin });

        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t12"));

        Assert.Equal(new[] { "InSrv", "Vessel:lander01" }, result.Pages[0].ShowWhen);
    }

    /// <summary>
    /// The other half, and the one that would otherwise be an accident: a
    /// page created by spilling is not a page the commander declared a
    /// context for, so it must be context-free rather than inheriting the
    /// origin's - which would give the SRV two matching pages and make the
    /// "first matching page wins" rule pick a half-empty overflow panel.
    /// </summary>
    [Fact]
    public void ChangeTemplate_ASpillCreatedPage_IsContextFree_NotACopyOfTheOrigins()
    {
        var origin = DensePage("NOMAD", "t30", 30) with { ShowWhen = new[] { "InSrv" } };
        var layout = new Layout(1, new[] { origin });

        var result = LayoutSpiller.ChangeTemplate(layout, 0, Template("t12"));

        Assert.Equal(3, result.Pages.Count);
        Assert.Null(result.Pages[1].ShowWhen);
        Assert.Null(result.Pages[2].ShowWhen);
    }
}
