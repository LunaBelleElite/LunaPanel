using System.Text.Json;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.Layouts;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="PressEndpoint.Evaluate"/> directly - kept free of any
/// ASP.NET or Win32 type, same discipline as <see cref="PanelEndpointTests"/>.
/// This is the "should we even try to fire" decision; actually injecting the
/// resolved chord is <see cref="Server.Input.ChordPresser"/>'s job, covered
/// by <c>ChordPresserTests</c>.
/// </summary>
public class PressEndpointTests
{
    private const string CatalogueJson = """
        {
          "catalogueVersion": 1,
          "categories": [ { "id": "ship", "label": "SHIP" } ],
          "actions": {
            "LandingGearToggle": { "label": "GEAR", "category": "ship" },
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

    private static (BindingsFile Bindings, IReadOnlyList<LunaPanel.Core.Catalogue.CataloguePickerEntry> Merge, IReadOnlySet<string> KnownActionNames) BuildInputs()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindingsResult = BindingsFile.Parse(BindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        var bindings = bindingsResult.File!;
        var merge = LunaPanel.Core.Catalogue.CatalogueMerger.Merge(catalogue, bindings);
        var known = bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        return (bindings, merge, known);
    }

    private static Layout LayoutWith(string templateId, params LayoutSlot[] slots) =>
        new(1, new List<LayoutPage> { new("SHIP", templateId, slots, Array.Empty<LayoutSlot>()) });

    [Fact]
    public void Evaluate_BoundAction_CanFire_AndResolvesTheChord()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.Null(result.Reason);
        Assert.NotNull(result.Chord);
        Assert.Equal("G", result.Chord!.DisplayText);
        Assert.Equal("LandingGearToggle", result.Action);
    }

    [Fact]
    public void Evaluate_UnboundAction_Refuses_WithSensibleReason_AndNoChord()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NotUsable, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_UnknownAction_Refuses_AsNotUsable()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "TotallyFictionalAction", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NotUsable, result.Outcome);
        Assert.Contains("TotallyFictionalAction", result.Reason);
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_DegradedMacroSlot_NeverFires_AndNamesTheMacroInTheReason()
    {
        // Exactly the starter layout's slot 0: a macro id that does not
        // exist anywhere. This is the deliberate, real proof the degraded-
        // slot machinery refuses to fire end to end - not a hypothetical.
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t30", new LayoutSlot(0, null, "request-docking", null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NotUsable, result.Outcome);
        Assert.Contains("request-docking", result.Reason);
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_KnownMacroSlot_CanFire_ReturnsTheMacroId_NoChord()
    {
        var (bindings, merge, known) = BuildInputs();
        var macros = new MacroKnowledge(
            new HashSet<string>(StringComparer.Ordinal) { "disembark" },
            new HashSet<string>(StringComparer.Ordinal));
        var layout = LayoutWith("t30", new LayoutSlot(0, null, "disembark", null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, macros, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.Null(result.Reason);
        Assert.Null(result.Chord);
        Assert.Equal("disembark", result.MacroId);
        Assert.Null(result.Action); // a macro slot names no single action
    }

    [Fact]
    public void Evaluate_LongPress_KnownMacro_CanFire_ReturnsTheMacroId_NoChord()
    {
        var (bindings, merge, known) = BuildInputs();
        var macros = new MacroKnowledge(
            new HashSet<string>(StringComparer.Ordinal) { "disembark" },
            new HashSet<string>(StringComparer.Ordinal));
        var longPress = new LongPressAction(null, "disembark");
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, longPress));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, macros, known, longPress: true);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.Null(result.Chord);
        Assert.Equal("disembark", result.MacroId);
    }

    [Fact]
    public void Evaluate_NoSuchSlotIndexOnThePage_Refuses_AsNoSuchSlot()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 5, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NoSuchSlot, result.Outcome);
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_PageIndexOutOfRange_Refuses_AsNoSuchPage()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 3, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_AnnotationSaysOk_ButLiveBindingsDisagree_RefusesWithActionableUnboundMessage()
    {
        // Defensive path only, not reachable through ServerHostBuilder's own
        // wiring (catalogueMerge and bindings are always built from the same
        // read there) - but Evaluate still guards against the two
        // disagreeing rather than assuming it can never happen. This is the
        // one path that actually reaches PressEndpoint's own hardcoded
        // "not currently bound" refusal string (ref/docs/editor.md's task
        // 3) - LayoutAnnotator's Unbound reason (checked first) never lets
        // this branch fire once catalogueMerge and bindings genuinely agree.
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var boundBindings = BindingsFile.Parse(BindingsXml).File!; // LandingGearToggle -> Key_G
        var merge = LunaPanel.Core.Catalogue.CatalogueMerger.Merge(catalogue, boundBindings);
        var known = boundBindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        const string unboundBindingsXml = """
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
                <LandingGearToggle>
                    <Primary Device="{NoDevice}" Key="" />
                    <Secondary Device="{NoDevice}" Key="" />
                </LandingGearToggle>
            </Root>
            """;
        var liveUnboundBindings = BindingsFile.Parse(unboundBindingsXml).File!;
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, liveUnboundBindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NotUsable, result.Outcome);
        Assert.Contains("Set a key for it in the game's Controls options", result.Reason);
        Assert.Contains("nothing to change here", result.Reason);
        Assert.Null(result.Chord);
    }

    // -------------------------------------------------------------------
    // Long-press (ref/docs/editor.md's long-press wiring). longPress:true
    // evaluates the slot's LongPressAction instead of its primary action/
    // macro - degrading independently, exactly like PanelEndpoint's own
    // response does.
    // -------------------------------------------------------------------

    [Fact]
    public void Evaluate_LongPress_BoundAction_CanFire_AndResolvesItsOwnChord()
    {
        var (bindings, merge, known) = BuildInputs();
        var longPress = new LongPressAction("LandingGearToggle", null);
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, longPress));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known, longPress: true);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.NotNull(result.Chord);
        Assert.Equal("G", result.Chord!.DisplayText);
        Assert.Equal("LandingGearToggle", result.Action);
    }

    [Fact]
    public void Evaluate_LongPress_UnboundAction_Refuses_AsNotUsable()
    {
        var (bindings, merge, known) = BuildInputs();
        var longPress = new LongPressAction("ToggleCargoScoop", null);
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, longPress));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known, longPress: true);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NotUsable, result.Outcome);
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_LongPress_SlotHasNone_Refuses_AsNoLongPress_NeverFallsBackToPrimary()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known, longPress: true);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NoLongPress, result.Outcome);
        Assert.Null(result.Chord);
    }

    [Fact]
    public void Evaluate_LongPressDegraded_PrimaryEvaluationIsUnaffected_StillCanFire()
    {
        var (bindings, merge, known) = BuildInputs();
        var longPress = new LongPressAction("ToggleCargoScoop", null); // unbound
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, longPress));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known, longPress: false);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.Equal("G", result.Chord!.DisplayText);
    }

    [Fact]
    public void Evaluate_ParkedSlotAtThatIndex_IsNotFireable_TreatedAsNoSuchSlot()
    {
        // A parked slot is not addressable by /api/press at all - it has no
        // active cell on the current template.
        var (bindings, merge, known) = BuildInputs();
        var page = new LayoutPage(
            "SHIP",
            "t6",
            new List<LayoutSlot>(),
            new List<LayoutSlot> { new(0, "LandingGearToggle", null, null, null) });
        var layout = new Layout(1, new List<LayoutPage> { page });

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.NoSuchSlot, result.Outcome);
    }

    // -------------------------------------------------------------------
    // The hold gesture (ref/docs/latching-keys.md's hold-to-thrust
    // extension) - the "should we even try to fire" decision reports
    // whether a slot uses it, exactly as Latch already does. Actually
    // holding/releasing the key is LatchRegistry.Hold/Release's job
    // (LatchRegistryTests), same split as latching.
    // -------------------------------------------------------------------

    [Fact]
    public void Evaluate_HoldSlot_ReportsHold()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null, Hold: true));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.True(result.Hold);
        Assert.False(result.Latch);
    }

    [Fact]
    public void Evaluate_OrdinarySlot_ReportsNoHold()
    {
        var (bindings, merge, known) = BuildInputs();
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        Assert.False(PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known).Hold);
    }

    /// <summary>
    /// The arm, not the string - the hold flag lives on the SLOT, and a
    /// long-press carries none of its own, so a long-press on a holding
    /// button is an ordinary tap of the long-press action. Mirrors
    /// <c>Evaluate_LongPressOfALatchSlot_IsNotItselfALatch</c> exactly.
    /// </summary>
    [Fact]
    public void Evaluate_LongPressOfAHoldSlot_IsNotItselfAHold()
    {
        var (bindings, merge, known) = BuildInputs();
        var longPress = new LongPressAction("LandingGearToggle", null);
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, longPress, Hold: true));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known, longPress: true);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.Equal("LandingGearToggle", result.Action);
        Assert.False(result.Hold);
    }

    /// <summary>
    /// [2026-09-12] <b>Verifies the wire interface before anything else
    /// depends on it</b> (this project's own "verify interfaces before
    /// planning or testing" rule): no <c>JsonStringEnumConverter</c> is
    /// registered project-wide (every other enum in this codebase crosses
    /// the wire server-to-client only, via <c>.ToString()</c>), so
    /// <see cref="PressEndpoint.HoldPhase"/> carries its own
    /// <c>[JsonConverter]</c> attribute rather than relying on global
    /// options this request could never reach through a real
    /// <c>ReadFromJsonAsync</c> call. Pinned against the exact lowercase
    /// strings the client actually sends
    /// (<c>PanelClientEndpoint.postHoldPhase</c>) - a converter that only
    /// accepted the PascalCase enum member names would silently 400 every
    /// hold press the real client ever sends.
    /// </summary>
    [Theory]
    [InlineData("down", PressEndpoint.HoldPhase.Down)]
    [InlineData("up", PressEndpoint.HoldPhase.Up)]
    public void Request_HoldField_DeserializesFromTheLowercaseStringTheClientSends(string wireValue, PressEndpoint.HoldPhase expected)
    {
        var json = $$"""{ "page": 0, "slot": 0, "hold": "{{wireValue}}" }""";

        var request = JsonSerializer.Deserialize<PressEndpoint.Request>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.Equal(expected, request!.Hold);
    }

    [Fact]
    public void Request_HoldField_AbsentFromTheBody_DeserializesAsNull_MatchingAPlainTapRequest()
    {
        var json = """{ "page": 0, "slot": 0 }""";

        var request = JsonSerializer.Deserialize<PressEndpoint.Request>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.Null(request!.Hold);
    }
}
