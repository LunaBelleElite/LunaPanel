using LunaPanel.Core.Bindings;
using LunaPanel.Core.Catalogue;

namespace LunaPanel.Tests.Catalogue;

/// <summary>
/// Pins <see cref="CatalogueMerger.Merge"/>'s four-way classification:
/// curated+bound, curated+unbound (including the case where the action
/// isn't even present in the player's bindings file at all), uncurated+bound,
/// and uncurated+unbound (which must be omitted entirely).
/// </summary>
public class CatalogueMergerTests
{
    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": {
            "CuratedBoundAction": { "label": "BOUND", "category": "ship" },
            "CuratedUnboundAction": { "label": "UNBOUND", "category": "ship" }
          }
        }
        """;

    private const string BindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <CuratedBoundAction>
                <Primary Device="Keyboard" Key="Key_A" />
                <Secondary Device="{NoDevice}" Key="" />
            </CuratedBoundAction>
            <UncuratedBoundAction>
                <Primary Device="Keyboard" Key="Key_B" />
                <Secondary Device="{NoDevice}" Key="" />
            </UncuratedBoundAction>
            <UncuratedUnboundAction>
                <Primary Device="{NoDevice}" Key="" />
                <Secondary Device="{NoDevice}" Key="" />
            </UncuratedUnboundAction>
        </Root>
        """;

    private static IReadOnlyList<CataloguePickerEntry> BuildMerge()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindingsResult = BindingsFile.Parse(BindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);

        return CatalogueMerger.Merge(catalogue, bindingsResult.File!);
    }

    private static IReadOnlyList<CataloguePickerEntry> BuildMergeAll()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindingsResult = BindingsFile.Parse(BindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);

        return CatalogueMerger.MergeAll(catalogue, bindingsResult.File!);
    }

    [Fact]
    public void Merge_CuratedAndBound_ShowsCuratedLabelCategoryAndChord()
    {
        var entries = BuildMerge();
        var entry = Assert.Single(entries, e => e.ActionName == "CuratedBoundAction");

        Assert.True(entry.IsCurated);
        Assert.True(entry.IsBound);
        Assert.Equal("BOUND", entry.DisplayLabel);
        Assert.Equal("ship", entry.Category);
        Assert.Equal("A", entry.DisplayChord);
        Assert.Null(entry.UnboundReason);
    }

    [Fact]
    public void Merge_CuratedAndUnbound_AbsentFromBindingsFile_ShowsGreyedWithNotPresentReason()
    {
        var entries = BuildMerge();
        var entry = Assert.Single(entries, e => e.ActionName == "CuratedUnboundAction");

        Assert.True(entry.IsCurated);
        Assert.False(entry.IsBound);
        Assert.Equal("UNBOUND", entry.DisplayLabel);
        Assert.Equal("ship", entry.Category);
        Assert.Null(entry.DisplayChord);
        Assert.Equal(UnboundReason.NotPresent, entry.UnboundReason);
    }

    [Fact]
    public void Merge_UncuratedAndBound_ShowsPrettifiedFallbackLabelAndNoCategory()
    {
        var entries = BuildMerge();
        var entry = Assert.Single(entries, e => e.ActionName == "UncuratedBoundAction");

        Assert.False(entry.IsCurated);
        Assert.True(entry.IsBound);
        Assert.Equal("Uncurated Bound Action", entry.DisplayLabel);
        Assert.Null(entry.Category);
        Assert.Equal("B", entry.DisplayChord);
        Assert.Null(entry.UnboundReason);
    }

    [Fact]
    public void Merge_UncuratedAndUnbound_IsOmittedEntirely()
    {
        var entries = BuildMerge();
        Assert.DoesNotContain(entries, e => e.ActionName == "UncuratedUnboundAction");
    }

    [Fact]
    public void Merge_ProducesExactlyThreeEntries_NotFour()
    {
        // Sanity check that the omission above isn't accidentally producing
        // a fourth entry under a different name/shape.
        var entries = BuildMerge();
        Assert.Equal(3, entries.Count);
    }

    // -----------------------------------------------------------------
    // MergeAll - the "show everything" mode (ref/docs/editor.md,
    // 2026-09-07). Same fixture, so every Merge-side assertion above still
    // proves MergeAll didn't change Merge's own behaviour; these pin the
    // one thing that's actually different: the fourth, uncurated+unbound
    // row Merge omits is now present and marked unbound.
    // -----------------------------------------------------------------

    [Fact]
    public void MergeAll_UncuratedAndUnbound_IsIncluded_MarkedUnboundWithNoDeviceReason()
    {
        var entries = BuildMergeAll();
        var entry = Assert.Single(entries, e => e.ActionName == "UncuratedUnboundAction");

        Assert.False(entry.IsCurated);
        Assert.False(entry.IsBound);
        Assert.Equal("Uncurated Unbound Action", entry.DisplayLabel);
        Assert.Null(entry.Category);
        Assert.Null(entry.DisplayChord);
        Assert.Equal(UnboundReason.NoDevice, entry.UnboundReason);
    }

    [Fact]
    public void MergeAll_ProducesExactlyFourEntries_OneMoreThanMerge()
    {
        var entries = BuildMergeAll();
        Assert.Equal(4, entries.Count);
    }

    [Fact]
    public void MergeAll_StillIncludesEveryEntryMergeItselfProduces_Unchanged()
    {
        var mergeEntries = BuildMerge();
        var allEntries = BuildMergeAll();

        foreach (var expected in mergeEntries)
        {
            var actual = Assert.Single(allEntries, a => a.ActionName == expected.ActionName);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void MergeAll_CuratedAndUnbound_SameAsMerge_NotAffectedByExpandedMode()
    {
        var entry = Assert.Single(BuildMergeAll(), e => e.ActionName == "CuratedUnboundAction");

        Assert.True(entry.IsCurated);
        Assert.False(entry.IsBound);
        Assert.Equal(UnboundReason.NotPresent, entry.UnboundReason);
    }
}
