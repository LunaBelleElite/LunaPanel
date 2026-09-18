using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Theme;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="PanelEndpoint.BuildResponse"/> directly - kept free of
/// any ASP.NET type, same discipline as <see cref="HealthEndpointTests"/>/
/// <see cref="PairEndpointTests"/>.
/// </summary>
public class PanelEndpointTests
{
    private static readonly IDiagnosticLog Log = new DiagnosticRingBuffer(50);

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

    private const string BindingsXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle>
                <Primary Device="Keyboard" Key="Key_G" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
            <ToggleCargoScoop>
                <Primary Device="{NoDevice}" Key="" />
                <Secondary Device="{NoDevice}" Key="" />
            </ToggleCargoScoop>
        </Root>
        """;

    private static readonly HudTheme Theme = new(
        new HudColor(1, 1, 1),
        new HudColor(2, 2, 2),
        new HudColor(3, 3, 3),
        new HudColor(4, 4, 4),
        new HudColor(5, 5, 5),
        new HudColor(6, 6, 6),
        HudThemeSource.Stock,
        Array.Empty<string>());

    private static (LunaPanel.Core.Catalogue.Catalogue Catalogue, BindingsFile Bindings, IReadOnlyList<LunaPanel.Core.Catalogue.CataloguePickerEntry> Merge, IReadOnlySet<string> KnownActionNames) BuildInputs()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindingsResult = BindingsFile.Parse(BindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        var bindings = bindingsResult.File!;
        var merge = LunaPanel.Core.Catalogue.CatalogueMerger.Merge(catalogue, bindings);
        var known = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        return (catalogue, bindings, merge, known);
    }

    private static Layout LayoutWith(string templateId, params LayoutSlot[] slots) =>
        new(1, new List<LayoutPage> { new("SHIP", templateId, slots, Array.Empty<LayoutSlot>()) });

    [Fact]
    public void BuildResponse_ValidPage_ReturnsTemplateGeometryAndCellSize_ForThePortraitViewport()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(PanelEndpoint.BuildOutcome.Ok, result.Outcome);
        var response = result.Response!;
        Assert.Equal("t6", response.TemplateId);
        Assert.Equal(2, response.Cols); // t6 portrait is 2x3 (see Templates)
        Assert.Equal(3, response.Rows);
        Assert.True(response.CellWidth > 0);
        Assert.True(response.CellHeight > 0);
        Assert.Contains(response.Verdict, new[] { "Comfortable", "Compact", "TooSmall" });
    }

    [Fact]
    public void BuildResponse_LandscapeViewport_UsesLandscapeGeometry_NotPortrait()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 640, 360, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(PanelEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Equal(3, result.Response!.Cols); // t6 landscape is 3x2 - swapped from portrait
        Assert.Equal(2, result.Response.Rows);
    }

    [Fact]
    public void BuildResponse_BoundAction_Slot_HasOkStatus_CuratedLabel_AndDisplayChord()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal(0, slot.Index);
        Assert.Equal("GEAR", slot.Label);
        Assert.Equal("Ok", slot.Status);
        Assert.Equal("G", slot.Chord);
    }

    [Fact]
    public void BuildResponse_UnboundAction_Slot_HasNullChord()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal("Unbound", slot.Status);
        Assert.Null(slot.Chord);
    }

    // -------------------------------------------------------------------
    // Long-press wiring (ref/docs/editor.md's long-press wiring). A slot
    // with no LongPress at all carries a null LongPress DTO; one WITH a
    // LongPress carries its own status/reason/chord, degrading
    // independently of the primary - a working primary must not be
    // disabled just because its long-press is unbound, and vice versa.
    // -------------------------------------------------------------------

    [Fact]
    public void BuildResponse_SlotWithNoLongPress_LongPressDtoIsNull()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Null(slot.LongPress);
    }

    [Fact]
    public void BuildResponse_LongPressBoundAction_HasOkStatus_AndItsOwnDisplayChord()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var longPress = new LongPressAction("LandingGearToggle", null);
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, longPress));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.NotNull(slot.LongPress);
        Assert.Equal("Ok", slot.LongPress!.Status);
        Assert.Equal("G", slot.LongPress.Chord);
    }

    [Fact]
    public void BuildResponse_LongPressUnboundAction_DegradesIndependently_PrimaryStillFine()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        // Primary (LandingGearToggle) is bound; long-press (ToggleCargoScoop)
        // is not - the primary must read Ok regardless.
        var longPress = new LongPressAction("ToggleCargoScoop", null);
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, longPress));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal("Ok", slot.Status);
        Assert.Equal("G", slot.Chord);
        Assert.NotNull(slot.LongPress);
        Assert.Equal("Unbound", slot.LongPress!.Status);
        Assert.Null(slot.LongPress.Chord);
    }

    [Fact]
    public void BuildResponse_PrimaryUnbound_ButLongPressBound_LongPressStillOk()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var longPress = new LongPressAction("LandingGearToggle", null);
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, longPress));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal("Unbound", slot.Status);
        Assert.NotNull(slot.LongPress);
        Assert.Equal("Ok", slot.LongPress!.Status);
        Assert.Equal("G", slot.LongPress.Chord);
    }

    [Fact]
    public void BuildResponse_MacroSlot_ChordIsAlwaysNull_EvenWhenOk()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, null, "pip-preset-weapons-engines", null, null));
        var macros = new MacroKnowledge(new HashSet<string> { "pip-preset-weapons-engines" }, new HashSet<string>());

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, macros, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal("Ok", slot.Status);
        Assert.Null(slot.Chord); // macros carry no single chord in this response shape
    }

    /// <summary>
    /// The field that closed <c>ref/docs/editor.md</c>'s "roughest edge in
    /// the feature": until 2026-09-09 this response gave the client no way
    /// to tell an action slot from a macro one, so the slot sheet offered
    /// the latch toggle on both and the commander found out which was which
    /// by tapping it and reading a refusal.
    ///
    /// Both directions are asserted in one place deliberately. A response
    /// that reported <em>every</em> slot as a macro slot would satisfy the
    /// macro half on its own and hide the latch control everywhere, which
    /// is a bigger regression than the edge it was fixing.
    /// </summary>
    [Fact]
    public void BuildResponse_ReportsWhichSlotsNameAMacro_AndWhichNameAnAction()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith(
            "t6",
            new LayoutSlot(0, null, "pip-preset-weapons-engines", null, null),
            new LayoutSlot(1, "LandingGearToggle", null, null, null));
        var macros = new MacroKnowledge(new HashSet<string> { "pip-preset-weapons-engines" }, new HashSet<string>());

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, macros, known, Theme, null, Log);

        var slots = result.Response!.Slots;
        Assert.Equal("pip-preset-weapons-engines", Assert.Single(slots, s => s.Index == 0).MacroId);
        Assert.Null(Assert.Single(slots, s => s.Index == 1).MacroId);
    }

    /// <summary>
    /// Intent, never resolution: the id reported is the one the layout
    /// stored, and it is reported whether or not the macro still exists.
    /// A slot naming a deleted macro is exactly the case the slot sheet
    /// most needs to identify - it is the one showing "unknown macro", and
    /// hiding the latch control there is still right.
    /// </summary>
    [Fact]
    public void BuildResponse_ReportsAMacroIdEvenWhenNoSuchMacroExists()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, null, "user-deadbeef0000", null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal("UnknownMacro", slot.Status);
        Assert.Equal("user-deadbeef0000", slot.MacroId);
    }

    [Fact]
    public void BuildResponse_DegradedMacroSlot_ReflectsMacroDegradedStatus_AndReason()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, null, "request-docking", null, null));
        var macros = MacroKnowledge.Empty; // "request-docking" is unrecognized entirely

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, macros, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal("UnknownMacro", slot.Status);
        Assert.Contains("request-docking", slot.Reason);
    }

    [Fact]
    public void BuildResponse_ParkedSlots_AreNeverIncludedInTheResponse()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var page = new LayoutPage(
            "SHIP",
            "t6",
            new List<LayoutSlot> { new(0, "LandingGearToggle", null, null, null) },
            new List<LayoutSlot> { new(4, "ToggleCargoScoop", null, null, null) });
        var layout = new Layout(1, new List<LayoutPage> { page });

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var slot = Assert.Single(result.Response!.Slots);
        Assert.Equal(0, slot.Index);
    }

    [Fact]
    public void BuildResponse_SlotsAreOrderedByIndex_RegardlessOfLayoutOrder()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith(
            "t6",
            new LayoutSlot(3, "ToggleCargoScoop", null, null, null),
            new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(new[] { 0, 3 }, result.Response!.Slots.Select(s => s.Index));
    }

    [Fact]
    public void BuildResponse_ThemeCss_ContainsAllSixVariables_WithTheGivenTheme()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var css = result.Response!.ThemeCss;
        Assert.Contains("--lp-ground: #010101;", css);
        Assert.Contains("--lp-frame: #020202;", css);
        Assert.Contains("--lp-text: #030303;", css);
        Assert.Contains("--lp-accent: #040404;", css);
        Assert.Contains("--lp-lit: #050505;", css);
        Assert.Contains("--lp-dim: #060606;", css);
    }

    // -----------------------------------------------------------------
    // The allowance figures (ref/docs/panels-and-pages.md's "Tabs" -
    // "the client should ask the server for its allowances rather than
    // restating them in JavaScript") and page navigation fields (tab row).
    // -----------------------------------------------------------------

    [Fact]
    public void BuildResponse_HeaderStripFramePaddingAndTabStripHeight_MatchCellSizeEstimatorsAllowancesForTheSameViewport()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        var allowances = CellSizeEstimator.Allowances(360, 640);
        Assert.Equal(allowances.HeaderStrip, result.Response!.HeaderStrip);
        Assert.Equal(allowances.FramePadding, result.Response.FramePadding);
        Assert.Equal(allowances.TabStripHeight, result.Response.TabStripHeight);
    }

    [Fact]
    public void BuildResponse_SinglePageLayout_ReportsPageIndexZero_PageCountOne_AndItsOwnName()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(0, result.Response!.PageIndex);
        Assert.Equal(1, result.Response.PageCount);
        Assert.Equal(new[] { "SHIP" }, result.Response.PageNames);
    }

    /// <summary>
    /// [2026-09-12] Pins <see cref="PanelEndpoint.PanelResponse.ShowWhen"/>:
    /// a context-free page (no <c>showWhen</c> at all, like this fixture's
    /// "SHIP") reports an EMPTY list, never <see langword="null"/> - the
    /// visibility picker relies on always having an array to check tokens
    /// against.
    /// </summary>
    [Fact]
    public void BuildResponse_PageWithNoShowWhen_ReportsAnEmptyShowWhenList_NeverNull()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.NotNull(result.Response!.ShowWhen);
        Assert.Empty(result.Response.ShowWhen);
    }

    /// <summary>
    /// Pins the other half of the same field: a page that DOES declare a
    /// context reports its tokens verbatim, in order.
    /// </summary>
    [Fact]
    public void BuildResponse_PageWithShowWhen_ReportsItsTokensVerbatim()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var pages = new List<LayoutPage>
        {
            new("SRV", "t6", new[] { new LayoutSlot(0, "ToggleCargoScoop", null, null, null) }, Array.Empty<LayoutSlot>(), new[] { "InSrv", "Vessel:testbuggy" }),
        };
        var layout = new Layout(1, pages);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(new[] { "InSrv", "Vessel:testbuggy" }, result.Response!.ShowWhen);
    }

    [Fact]
    public void BuildResponse_MultiPageLayout_ReportsTheRequestedPageIndex_AndEveryPagesName()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var pages = new List<LayoutPage>
        {
            new("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>()),
            new("SRV", "t6", new[] { new LayoutSlot(0, "ToggleCargoScoop", null, null, null) }, Array.Empty<LayoutSlot>()),
        };
        var layout = new Layout(1, pages);

        var result = PanelEndpoint.BuildResponse(layout, 1, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(1, result.Response!.PageIndex);
        Assert.Equal(2, result.Response.PageCount);
        Assert.Equal(new[] { "SHIP", "SRV" }, result.Response.PageNames);
    }

    [Fact]
    public void BuildResponse_UnknownTemplateId_ReturnsUnknownTemplateOutcome_AndNoResponse()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("not-a-real-template", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(PanelEndpoint.BuildOutcome.UnknownTemplate, result.Outcome);
        Assert.Null(result.Response);
    }

    [Fact]
    public void BuildResponse_PageIndexOutOfRange_ReturnsNoSuchPageOutcome_AndNoResponse()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 1, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, null, Log);

        Assert.Equal(PanelEndpoint.BuildOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Response);
    }

    // -----------------------------------------------------------------
    // Lit state (LitStateResolver, wired through here). LandingGearToggle
    // carries "lit": ["LandingGearDown"] in CatalogueJson above -
    // StatusVocabulary maps that to Flags bit 2 (see StatusVocabularyTests).
    // -----------------------------------------------------------------

    private static StatusSnapshot SnapshotWithFlags(uint flags) => new(flags, null, null, GameRunning: true, SignedIn: true);

    [Fact]
    public void BuildResponse_NoSnapshot_SlotWithLitCondition_ReportsNotLit()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, snapshot: null, Log);

        Assert.Equal("Off", Assert.Single(result.Response!.Slots).Lit);
    }

    [Fact]
    public void BuildResponse_SnapshotSatisfiesLitCondition_SlotReportsLit()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));
        var gearDown = SnapshotWithFlags(1u << 2); // LandingGearDown

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, gearDown, Log);

        Assert.Equal("Full", Assert.Single(result.Response!.Slots).Lit);
    }

    [Fact]
    public void BuildResponse_SnapshotDoesNotSatisfyLitCondition_SlotReportsNotLit()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));
        var gearUp = SnapshotWithFlags(0u);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, gearUp, Log);

        Assert.Equal("Off", Assert.Single(result.Response!.Slots).Lit);
    }

    [Fact]
    public void BuildResponse_ActionWithNoLitCondition_ReportsNotLit_EvenWithASnapshot()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        // ToggleCargoScoop carries no "lit" entry in CatalogueJson.
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, null));
        var anySnapshot = SnapshotWithFlags(uint.MaxValue);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, anySnapshot, Log);

        Assert.Equal("Off", Assert.Single(result.Response!.Slots).Lit);
    }

    [Fact]
    public void BuildResponse_MacroSlot_NeverLit_EvenWithASnapshot()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, null, "pip-preset-weapons-engines", null, null));
        var macros = new MacroKnowledge(new HashSet<string> { "pip-preset-weapons-engines" }, new HashSet<string>());
        var anySnapshot = SnapshotWithFlags(uint.MaxValue);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, macros, known, Theme, anySnapshot, Log);

        Assert.Equal("Off", Assert.Single(result.Response!.Slots).Lit);
    }

    /// <summary>
    /// Guards against a routing defect distinct from every single-slot test
    /// above: each slot's Lit must come from ITS OWN action, not from
    /// whichever slot happens to be first/at index 0 - a wrong-key lookup
    /// into the per-index lit dictionary would read as correct against any
    /// test with only one slot on the page.
    /// </summary>
    [Fact]
    public void BuildResponse_MultipleSlots_EachSlotsLitComesFromItsOwnAction_NotAnotherSlots()
    {
        var (catalogue, _, merge, known) = BuildInputs();
        var layout = LayoutWith(
            "t6",
            new LayoutSlot(0, "ToggleCargoScoop", null, null, null), // no lit condition
            new LayoutSlot(2, "LandingGearToggle", null, null, null)); // lit: LandingGearDown
        var gearDown = SnapshotWithFlags(1u << 2);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, gearDown, Log);

        Assert.Equal("Off", result.Response!.Slots.Single(s => s.Index == 0).Lit);
        Assert.Equal("Full", result.Response.Slots.Single(s => s.Index == 2).Lit);
    }

    // ---------------------------------------------------------------
    // The levelled 'lit' shape (ref/docs/lit-state.md), routed all the way
    // through BuildResponse - the wiring itself, not LitStateResolver's own
    // behaviour (see LitStateResolverTests) or Catalogue.Parse's own
    // shape-detection (see CatalogueTests).
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

    private static (LunaPanel.Core.Catalogue.Catalogue Catalogue, IReadOnlyList<LunaPanel.Core.Catalogue.CataloguePickerEntry> Merge, IReadOnlySet<string> KnownActionNames) BuildLevelledInputs()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(LevelledCatalogueJson);
        var bindingsResult = BindingsFile.Parse("<Root PresetName=\"Custom\" MajorVersion=\"4\" MinorVersion=\"2\"></Root>");
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        var merge = LunaPanel.Core.Catalogue.CatalogueMerger.Merge(catalogue, bindingsResult.File!);
        return (catalogue, merge, new HashSet<string>(StringComparer.Ordinal));
    }

    [Fact]
    public void BuildResponse_LevelledLitCondition_HighBeam_SlotReportsFull()
    {
        var (catalogue, merge, known) = BuildLevelledInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "HeadlightsBuggyButton", null, null, null));
        var lightsOnHighBeam = SnapshotWithFlags((1u << 8) | (1u << 31));

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, lightsOnHighBeam, Log);

        Assert.Equal("Full", Assert.Single(result.Response!.Slots).Lit);
    }

    [Fact]
    public void BuildResponse_LevelledLitCondition_LowBeam_SlotReportsPartial()
    {
        var (catalogue, merge, known) = BuildLevelledInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "HeadlightsBuggyButton", null, null, null));
        var lightsOnLowBeam = SnapshotWithFlags(1u << 8);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, lightsOnLowBeam, Log);

        Assert.Equal("Partial", Assert.Single(result.Response!.Slots).Lit);
    }

    [Fact]
    public void BuildResponse_LevelledLitCondition_LightsOff_SlotReportsOff()
    {
        var (catalogue, merge, known) = BuildLevelledInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "HeadlightsBuggyButton", null, null, null));
        var lightsOff = SnapshotWithFlags(0u);

        var result = PanelEndpoint.BuildResponse(layout, 0, 360, 640, catalogue, merge, MacroKnowledge.Empty, known, Theme, lightsOff, Log);

        Assert.Equal("Off", Assert.Single(result.Response!.Slots).Lit);
    }
}
