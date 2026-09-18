using LunaPanel.Core.Bindings;
using LunaPanel.Core.Layouts;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// The hold gesture's own path (<c>ref/docs/latching-keys.md</c>'s
/// hold-to-thrust extension) - the layout file, the validator, the edit
/// verb, and the press evaluation - exact mirror of
/// <c>LatchLayoutTests</c>, plus the mutual-exclusivity rule that is new
/// with this feature: a slot fires exactly one of tap-to-latch or
/// press-and-hold, never both.
/// </summary>
public class HoldLayoutTests
{
    // -------------------------------------------------------------------
    // The layout file.
    // -------------------------------------------------------------------

    [Fact]
    public void LayoutJson_HoldSlot_RoundTrips()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "ForwardThrust", null, null, null, Hold: true) }, Array.Empty<LayoutSlot>()),
        });

        var parsed = LayoutJson.Parse(LayoutJson.Serialize(layout));

        Assert.True(parsed.Success, parsed.Error);
        Assert.True(parsed.Layout!.Pages[0].Slots[0].Hold);
    }

    /// <summary>
    /// Same "an absent hold and a false hold are the same statement"
    /// discipline <c>LatchLayoutTests</c> already pins for latch.
    /// </summary>
    [Fact]
    public void LayoutJson_NonHoldSlot_WritesNoHoldProperty_AndParsesAbsentAsFalse()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>()),
        });

        var json = LayoutJson.Serialize(layout);

        Assert.DoesNotContain("hold", json, StringComparison.OrdinalIgnoreCase);

        var parsed = LayoutJson.Parse(json);
        Assert.True(parsed.Success, parsed.Error);
        Assert.False(parsed.Layout!.Pages[0].Slots[0].Hold);
    }

    [Fact]
    public void LayoutJson_NonBooleanHold_IsReportedRatherThanSilentlyIgnored()
    {
        var parsed = LayoutJson.Parse("""
            { "schemaVersion": 1, "pages": [ { "name": "SHIP", "templateId": "t6",
              "slots": [ { "index": 0, "action": "LandingGearToggle", "hold": "yes" } ], "parked": [] } ] }
            """);

        Assert.False(parsed.Success);
        Assert.Contains("'hold' is not a boolean", parsed.Error!, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The validator.
    // -------------------------------------------------------------------

    [Fact]
    public void Validator_HoldOnAnActionSlot_IsAccepted()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "ForwardThrust", null, null, null, Hold: true) }, Array.Empty<LayoutSlot>()),
        });

        Assert.True(LayoutValidator.ValidateForLoad(layout).IsValid);
    }

    [Fact]
    public void Validator_HoldOnAMacroSlot_IsRejected_BecauseThereIsNoOneKeyToHold()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, null, "request-docking", null, null, Hold: true) }, Array.Empty<LayoutSlot>()),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("only an action can be held", StringComparison.Ordinal));
    }

    /// <summary>
    /// The rule this feature actually adds: a slot fires exactly one of a
    /// tap-to-toggle latch or a press-and-hold, never both at once - the two
    /// gestures mean something different over the same one key.
    /// </summary>
    [Fact]
    public void Validator_LatchAndHoldBothTrueOnOneSlot_IsRejected()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "ForwardThrust", null, null, null, Latch: true, Hold: true) }, Array.Empty<LayoutSlot>()),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("cannot be both latched and held", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_LatchAloneOrHoldAlone_NeitherTripsTheMutualExclusivityRule()
    {
        var latchOnly = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "ForwardThrust", null, null, null, Latch: true) }, Array.Empty<LayoutSlot>()),
        });
        var holdOnly = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "ForwardThrust", null, null, null, Hold: true) }, Array.Empty<LayoutSlot>()),
        });

        Assert.True(LayoutValidator.ValidateForLoad(latchOnly).IsValid);
        Assert.True(LayoutValidator.ValidateForLoad(holdOnly).IsValid);
    }

    // -------------------------------------------------------------------
    // The edit verb.
    // -------------------------------------------------------------------

    private static Layout LayoutWith(params LayoutSlot[] slots) =>
        new(1, new[] { new LayoutPage("SHIP", "t6", slots, Array.Empty<LayoutSlot>()) });

    [Fact]
    public void SetHold_On_ThenOff_RoundTripsThroughTheSameRoute()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, null, null));

        var on = SlotEditEndpoint.SetHold(layout, 0, 0, hold: true);
        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, on.Outcome);
        Assert.True(on.Layout!.Pages[0].Slots[0].Hold);

        var off = SlotEditEndpoint.SetHold(on.Layout!, 0, 0, hold: false);
        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, off.Outcome);
        Assert.False(off.Layout!.Pages[0].Slots[0].Hold);
    }

    [Fact]
    public void SetHold_MacroSlot_RefusesWithInvalidRequest()
    {
        var layout = LayoutWith(new LayoutSlot(0, null, "request-docking", null, null));

        var result = SlotEditEndpoint.SetHold(layout, 0, 0, hold: true);

        Assert.Equal(SlotEditEndpoint.EditOutcome.InvalidRequest, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void SetHold_EmptySlot_RefusesWithNoSuchSlot()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, null, null));

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchSlot, SlotEditEndpoint.SetHold(layout, 0, 4, hold: true).Outcome);
    }

    [Fact]
    public void SetHold_NoSuchPage_Refuses()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, null, null));

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, SlotEditEndpoint.SetHold(layout, 7, 0, hold: true).Outcome);
    }

    /// <summary>
    /// The "vice versa" the plan calls for: turning holding ON implies
    /// latching turns OFF, the same way <see cref="SlotEditEndpoint.SetLatch"/>
    /// clears Hold from the other side - a slot fires exactly one of the two
    /// gestures over the same key.
    /// </summary>
    [Fact]
    public void SetHold_On_ClearsAnExistingLatch()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, null, null, Latch: true));

        var result = SlotEditEndpoint.SetHold(layout, 0, 0, hold: true);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.True(slot.Hold);
        Assert.False(slot.Latch);
    }

    /// <summary>The other direction, already covered from SetLatch's own side, restated here for symmetry.</summary>
    [Fact]
    public void SetLatch_On_ClearsAnExistingHold()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, null, null, Hold: true));

        var result = SlotEditEndpoint.SetLatch(layout, 0, 0, latch: true);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.True(slot.Latch);
        Assert.False(slot.Hold);
    }

    [Fact]
    public void SetHold_LeavesTheSlotsLabelAndLongPressAlone()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, "THRUST", new LongPressAction("LandingGearToggle", null)));

        var result = SlotEditEndpoint.SetHold(layout, 0, 0, hold: true);

        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.Equal("THRUST", slot.Label);
        Assert.Equal("LandingGearToggle", slot.LongPress!.Action);
    }

    /// <summary>
    /// Same asymmetry <see cref="SlotEditEndpoint.Assign"/> already applies to
    /// Latch: reassigning a held button to a different action must not
    /// silently leave the OLD key held down forever.
    /// </summary>
    [Fact]
    public void Assign_OverAnExistingHold_ClearsTheHold_ButKeepsTheLongPress()
    {
        var layout = LayoutWith(new LayoutSlot(0, "ForwardThrust", null, null, new LongPressAction("ToggleCargoScoop", null), Hold: true));

        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.False(slot.Hold);
        Assert.Equal("ToggleCargoScoop", slot.LongPress!.Action);
    }

    // -------------------------------------------------------------------
    // The press evaluation.
    // -------------------------------------------------------------------

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
            <LandingGearToggle><Primary Device="Keyboard" Key="Key_G" /><Secondary Device="{NoDevice}" Key="" /></LandingGearToggle>
            <ToggleCargoScoop><Primary Device="Keyboard" Key="Key_H" /><Secondary Device="{NoDevice}" Key="" /></ToggleCargoScoop>
        </Root>
        """;

    private static (BindingsFile Bindings, IReadOnlyList<LunaPanel.Core.Catalogue.CataloguePickerEntry> Merge, IReadOnlySet<string> Known) PressInputs()
    {
        var catalogue = LunaPanel.Core.Catalogue.Catalogue.Parse(CatalogueJson);
        var bindingsResult = BindingsFile.Parse(BindingsXml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        var bindings = bindingsResult.File!;
        return (bindings, LunaPanel.Core.Catalogue.CatalogueMerger.Merge(catalogue, bindings), bindings.Elements.Select(e => e.Name).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void Evaluate_HoldSlot_ReportsHold()
    {
        var (bindings, merge, known) = PressInputs();
        var layout = LayoutWith(new LayoutSlot(0, "LandingGearToggle", null, null, null, Hold: true));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.True(result.Hold);
    }
}
