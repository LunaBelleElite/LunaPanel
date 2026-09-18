using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="LitStateResolver"/> directly. Kept apart from
/// <c>PanelEndpointTests</c>/<c>PanelLiveEndpointTests</c>, which pin that
/// each of those wires this resolver's output through correctly, but do not
/// re-sweep every one of this resolver's own cases.
/// </summary>
public class LitStateResolverTests
{
    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": {
            "LandingGearToggle": { "label": "GEAR", "category": "ship", "lit": ["LandingGearDown"] },
            "ToggleCargoScoop": { "label": "SCOOP", "category": "ship" }
          }
        }
        """;

    private static readonly LunaPanel.Core.Catalogue.Catalogue Catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);

    private static StatusSnapshot SnapshotWithFlags(uint flags) => new(flags, null, null, GameRunning: true, SignedIn: true);

    private static LayoutPage PageWith(params LayoutSlot[] slots) =>
        new("SHIP", "t6", slots, Array.Empty<LayoutSlot>());

    [Fact]
    public void Resolve_NoSnapshot_EverySlotReportsNotLit()
    {
        var page = PageWith(new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = LitStateResolver.Resolve(page, Catalogue, snapshot: null);

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_SnapshotSatisfiesLitCondition_ReportsLit()
    {
        var page = PageWith(new LayoutSlot(0, "LandingGearToggle", null, null, null));
        var gearDown = SnapshotWithFlags(1u << 2); // LandingGearDown (see StatusVocabularyTests)

        var result = LitStateResolver.Resolve(page, Catalogue, gearDown);

        Assert.Equal(SlotLitLevel.Full, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_SnapshotDoesNotSatisfyLitCondition_ReportsNotLit()
    {
        var page = PageWith(new LayoutSlot(0, "LandingGearToggle", null, null, null));
        var gearUp = SnapshotWithFlags(0u);

        var result = LitStateResolver.Resolve(page, Catalogue, gearUp);

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_ActionWithNoLitCondition_ReportsNotLit_EvenWithASnapshot()
    {
        var page = PageWith(new LayoutSlot(0, "ToggleCargoScoop", null, null, null));

        var result = LitStateResolver.Resolve(page, Catalogue, SnapshotWithFlags(uint.MaxValue));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_UnknownAction_ReportsNotLit_RatherThanThrowing()
    {
        var page = PageWith(new LayoutSlot(0, "SomeActionNotInTheCatalogue", null, null, null));

        var result = LitStateResolver.Resolve(page, Catalogue, SnapshotWithFlags(uint.MaxValue));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_MacroSlot_ReportsNotLit_EvenWithASnapshot()
    {
        var page = PageWith(new LayoutSlot(0, null, "some-macro", null, null));

        var result = LitStateResolver.Resolve(page, Catalogue, SnapshotWithFlags(uint.MaxValue));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_NeitherActionNorMacro_ReportsNotLit_RatherThanThrowing()
    {
        var page = PageWith(new LayoutSlot(0, null, null, null, null));

        var result = LitStateResolver.Resolve(page, Catalogue, SnapshotWithFlags(uint.MaxValue));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_OnlyActiveSlots_NeverIncludesParkedSlots()
    {
        var page = new LayoutPage(
            "SHIP",
            "t6",
            new List<LayoutSlot> { new(0, "LandingGearToggle", null, null, null) },
            new List<LayoutSlot> { new(4, "ToggleCargoScoop", null, null, null) });

        var result = LitStateResolver.Resolve(page, Catalogue, snapshot: null);

        Assert.Equal(new[] { 0 }, result.Select(s => s.Index));
    }

    [Fact]
    public void Resolve_OrdersByIndex_RegardlessOfInputOrder()
    {
        var page = PageWith(
            new LayoutSlot(3, "ToggleCargoScoop", null, null, null),
            new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = LitStateResolver.Resolve(page, Catalogue, snapshot: null);

        Assert.Equal(new[] { 0, 3 }, result.Select(s => s.Index));
    }

    [Fact]
    public void ResolveAndLog_NoFailures_LogsNothing()
    {
        var page = PageWith(new LayoutSlot(0, "LandingGearToggle", null, null, null));
        var log = new DiagnosticRingBuffer(10);

        LitStateResolver.ResolveAndLog(page, Catalogue, SnapshotWithFlags(1u << 2), log);

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void ResolveAndLog_SameResultsAsResolve_WhenNothingFails()
    {
        var page = PageWith(
            new LayoutSlot(0, "LandingGearToggle", null, null, null),
            new LayoutSlot(1, "ToggleCargoScoop", null, null, null));
        var log = new DiagnosticRingBuffer(10);
        var snapshot = SnapshotWithFlags(1u << 2);

        var plain = LitStateResolver.Resolve(page, Catalogue, snapshot);
        var logged = LitStateResolver.ResolveAndLog(page, Catalogue, snapshot, log);

        Assert.Equal(plain, logged);
    }

    // ---------------------------------------------------------------
    // The three-level shape (ref/docs/lit-state.md) - a levelled 'lit' list
    // routed all the way through the resolver, not just LitCondition.Evaluate
    // in isolation (see CatalogueTests for that).
    // ---------------------------------------------------------------

    private const string LevelledCatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "srv", "label": "SRV" } ],
          "actions": {
            "HeadlightsBuggyButton": {
              "label": "SRV Lights", "category": "srv",
              "lit": [
                { "when": ["LightsOn", "SrvHighBeam"], "level": "full" },
                { "when": ["LightsOn"], "level": "partial" }
              ]
            }
          }
        }
        """;

    private static readonly LunaPanel.Core.Catalogue.Catalogue LevelledCatalogue =
        LunaPanel.Core.Catalogue.Catalogue.Parse(LevelledCatalogueJson);

    [Fact]
    public void Resolve_LevelledLitCondition_HighBeam_ReportsFull()
    {
        var page = PageWith(new LayoutSlot(0, "HeadlightsBuggyButton", null, null, null));
        var lightsOnHighBeam = SnapshotWithFlags((1u << 8) | (1u << 31));

        var result = LitStateResolver.Resolve(page, LevelledCatalogue, lightsOnHighBeam);

        Assert.Equal(SlotLitLevel.Full, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_LevelledLitCondition_LowBeam_ReportsPartial()
    {
        var page = PageWith(new LayoutSlot(0, "HeadlightsBuggyButton", null, null, null));
        var lightsOnLowBeam = SnapshotWithFlags(1u << 8);

        var result = LitStateResolver.Resolve(page, LevelledCatalogue, lightsOnLowBeam);

        Assert.Equal(SlotLitLevel.Partial, Assert.Single(result).Level);
    }

    [Fact]
    public void Resolve_LevelledLitCondition_LightsOff_ReportsOff()
    {
        var page = PageWith(new LayoutSlot(0, "HeadlightsBuggyButton", null, null, null));
        var lightsOff = SnapshotWithFlags(0u);

        var result = LitStateResolver.Resolve(page, LevelledCatalogue, lightsOff);

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }
}
