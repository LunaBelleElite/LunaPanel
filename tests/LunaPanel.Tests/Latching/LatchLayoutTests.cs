using LunaPanel.Core.Bindings;
using LunaPanel.Core.Layouts;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// Everything a latch has to survive between the commander setting it and
/// the key going down: the layout file it is stored in, the validator that
/// decides whether it is a legal thing to have asked for, the edit verb that
/// sets it, and the press evaluation that reports it. Kept in one file
/// because these are four steps of one path, and a break anywhere along it
/// produces the same symptom (a button that quietly stops latching).
/// </summary>
public class LatchLayoutTests
{
    // -------------------------------------------------------------------
    // The layout file.
    // -------------------------------------------------------------------

    [Fact]
    public void LayoutJson_LatchSlot_RoundTrips()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "SecondaryFire", null, null, null, Latch: true) }, Array.Empty<LayoutSlot>()),
        });

        var parsed = LayoutJson.Parse(LayoutJson.Serialize(layout));

        Assert.True(parsed.Success, parsed.Error);
        Assert.True(parsed.Layout!.Pages[0].Slots[0].Latch);
    }

    /// <summary>
    /// A layout written before latching existed must keep meaning exactly
    /// what it did, and one written after must not gain a field it does not
    /// use - both directions of "an absent latch and a false latch are the
    /// same statement", the same discipline <c>showWhen</c> already follows.
    /// </summary>
    [Fact]
    public void LayoutJson_NonLatchSlot_WritesNoLatchProperty_AndParsesAbsentAsFalse()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>()),
        });

        var json = LayoutJson.Serialize(layout);

        Assert.DoesNotContain("latch", json, StringComparison.OrdinalIgnoreCase);

        var parsed = LayoutJson.Parse(json);
        Assert.True(parsed.Success, parsed.Error);
        Assert.False(parsed.Layout!.Pages[0].Slots[0].Latch);
    }

    [Fact]
    public void LayoutJson_ExplicitFalseLatch_ParsesAsFalse()
    {
        var parsed = LayoutJson.Parse("""
            { "schemaVersion": 1, "pages": [ { "name": "SHIP", "templateId": "t6",
              "slots": [ { "index": 0, "action": "LandingGearToggle", "latch": false } ], "parked": [] } ] }
            """);

        Assert.True(parsed.Success, parsed.Error);
        Assert.False(parsed.Layout!.Pages[0].Slots[0].Latch);
    }

    [Fact]
    public void LayoutJson_NonBooleanLatch_IsReportedRatherThanSilentlyIgnored()
    {
        var parsed = LayoutJson.Parse("""
            { "schemaVersion": 1, "pages": [ { "name": "SHIP", "templateId": "t6",
              "slots": [ { "index": 0, "action": "LandingGearToggle", "latch": "yes" } ], "parked": [] } ] }
            """);

        Assert.False(parsed.Success);
        Assert.Contains("'latch' is not a boolean", parsed.Error!, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The validator.
    // -------------------------------------------------------------------

    [Fact]
    public void Validator_LatchOnAnActionSlot_IsAccepted()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "SecondaryFire", null, null, null, Latch: true) }, Array.Empty<LayoutSlot>()),
        });

        Assert.True(LayoutValidator.ValidateForLoad(layout).IsValid);
    }

    [Fact]
    public void Validator_LatchOnAMacroSlot_IsRejected_BecauseThereIsNoOneKeyToHold()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, null, "request-docking", null, null, Latch: true) }, Array.Empty<LayoutSlot>()),
        });

        var result = LayoutValidator.ValidateForLoad(layout);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("only an action can be latched", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_MacroSlotWithoutALatch_IsStillAccepted()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, null, "request-docking", null, null) }, Array.Empty<LayoutSlot>()),
        });

        Assert.True(LayoutValidator.ValidateForLoad(layout).IsValid);
    }

    // -------------------------------------------------------------------
    // The edit verb.
    // -------------------------------------------------------------------

    private static Layout LayoutWith(params LayoutSlot[] slots) =>
        new(1, new[] { new LayoutPage("SHIP", "t6", slots, Array.Empty<LayoutSlot>()) });

    [Fact]
    public void SetLatch_On_ThenOff_RoundTripsThroughTheSameRoute()
    {
        var layout = LayoutWith(new LayoutSlot(0, "SecondaryFire", null, null, null));

        var on = SlotEditEndpoint.SetLatch(layout, 0, 0, latch: true);
        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, on.Outcome);
        Assert.True(on.Layout!.Pages[0].Slots[0].Latch);

        var off = SlotEditEndpoint.SetLatch(on.Layout!, 0, 0, latch: false);
        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, off.Outcome);
        Assert.False(off.Layout!.Pages[0].Slots[0].Latch);
    }

    [Fact]
    public void SetLatch_MacroSlot_RefusesWithInvalidRequest_RatherThanSavingSomethingTheValidatorWouldReject()
    {
        var layout = LayoutWith(new LayoutSlot(0, null, "request-docking", null, null));

        var result = SlotEditEndpoint.SetLatch(layout, 0, 0, latch: true);

        Assert.Equal(SlotEditEndpoint.EditOutcome.InvalidRequest, result.Outcome);
        Assert.Null(result.Layout);
    }

    /// <summary>
    /// Turning latching OFF is never refused, even for a slot that should
    /// not have had it on - a layout hand-edited into an illegal state must
    /// still be recoverable from the panel rather than only from a text
    /// editor.
    /// </summary>
    [Fact]
    public void SetLatch_Off_IsAllowedEvenOnAMacroSlot_SoAnIllegalStateIsRecoverable()
    {
        var layout = LayoutWith(new LayoutSlot(0, null, "request-docking", null, null, Latch: true));

        var result = SlotEditEndpoint.SetLatch(layout, 0, 0, latch: false);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.False(result.Layout!.Pages[0].Slots[0].Latch);
    }

    [Fact]
    public void SetLatch_EmptySlot_RefusesWithNoSuchSlot()
    {
        var layout = LayoutWith(new LayoutSlot(0, "SecondaryFire", null, null, null));

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchSlot, SlotEditEndpoint.SetLatch(layout, 0, 4, latch: true).Outcome);
    }

    [Fact]
    public void SetLatch_NoSuchPage_Refuses()
    {
        var layout = LayoutWith(new LayoutSlot(0, "SecondaryFire", null, null, null));

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, SlotEditEndpoint.SetLatch(layout, 7, 0, latch: true).Outcome);
    }

    [Fact]
    public void SetLatch_LeavesTheSlotsLabelAndLongPressAlone()
    {
        var layout = LayoutWith(new LayoutSlot(0, "SecondaryFire", null, "FIRE", new LongPressAction("LandingGearToggle", null)));

        var result = SlotEditEndpoint.SetLatch(layout, 0, 0, latch: true);

        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.Equal("FIRE", slot.Label);
        Assert.Equal("LandingGearToggle", slot.LongPress!.Action);
    }

    /// <summary>
    /// The deliberate asymmetry with long-press: a long-press is a separate
    /// action that survives a reassignment, but a latch is the MODE the
    /// primary is fired in. Carrying it over would mean reassigning a
    /// latched secondary-fire button to landing gear silently leaves the
    /// gear key held down - exactly the "keystroke that will not stop" this
    /// feature's release rules exist to prevent.
    /// </summary>
    [Fact]
    public void Assign_OverAnExistingLatch_ClearsTheLatch_ButKeepsTheLongPress()
    {
        var layout = LayoutWith(new LayoutSlot(0, "SecondaryFire", null, null, new LongPressAction("ToggleCargoScoop", null), Latch: true));

        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.False(slot.Latch);
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
    public void Evaluate_LatchSlot_ReportsLatch()
    {
        var (bindings, merge, known) = PressInputs();
        var layout = LayoutWith(new LayoutSlot(0, "LandingGearToggle", null, null, null, Latch: true));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.True(result.Latch);
    }

    [Fact]
    public void Evaluate_OrdinarySlot_ReportsNoLatch()
    {
        var (bindings, merge, known) = PressInputs();
        var layout = LayoutWith(new LayoutSlot(0, "LandingGearToggle", null, null, null));

        Assert.False(PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known).Latch);
    }

    /// <summary>
    /// The arm, not the string. The latch flag lives on the SLOT, and a
    /// long-press carries none of its own - so a long-press on a latching
    /// button is an ordinary tap of the long-press action. Reading the
    /// slot's flag in this branch would hold a completely different key
    /// down, and every "does a latch slot latch" assertion would still pass.
    /// </summary>
    [Fact]
    public void Evaluate_LongPressOfALatchSlot_IsNotItselfALatch()
    {
        var (bindings, merge, known) = PressInputs();
        var layout = LayoutWith(new LayoutSlot(0, "LandingGearToggle", null, null, new LongPressAction("ToggleCargoScoop", null), Latch: true));

        var result = PressEndpoint.Evaluate(layout, 0, 0, bindings, merge, MacroKnowledge.Empty, known, longPress: true);

        Assert.Equal(PressEndpoint.EvaluationOutcome.CanFire, result.Outcome);
        Assert.Equal("ToggleCargoScoop", result.Action);
        Assert.False(result.Latch);
    }
}
