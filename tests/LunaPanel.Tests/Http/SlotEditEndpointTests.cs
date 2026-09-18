using LunaPanel.Core.Layouts;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="SlotEditEndpoint"/>'s three pure verbs directly - the
/// assign/rename/clear logic the on-device editor needs
/// (<c>ref/docs/editor.md</c>) - kept free of any ASP.NET type, same
/// discipline as <see cref="PanelTemplateEndpointTests"/>/<see cref="PressEndpointTests"/>.
/// Persisting the result (<c>LayoutStore.Save</c>) is the caller's job, not
/// this type's - these tests only prove the in-memory <see cref="Layout"/>
/// transformation itself.
/// </summary>
public class SlotEditEndpointTests
{
    private static LayoutSlot Slot(int index, string action, string? label = null, LongPressAction? longPress = null) =>
        new(index, action, null, label, longPress);

    private static LayoutSlot MacroSlot(int index, string macroId) => new(index, null, macroId, null, null);

    private static Layout OnePageLayout(string templateId, params LayoutSlot[] slots) =>
        new(1, new[] { new LayoutPage("SHIP", templateId, slots, Array.Empty<LayoutSlot>()) });

    // -------------------------------------------------------------------
    // Assign
    // -------------------------------------------------------------------

    [Fact]
    public void Assign_EmptySlot_AddsANewActiveSlotWithNoLabel()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Assign(layout, 0, 2, "LandingGearToggle", null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slot = Assert.Single(result.Layout!.Pages[0].Slots);
        Assert.Equal(2, slot.Index);
        Assert.Equal("LandingGearToggle", slot.Action);
        Assert.Null(slot.Macro);
        Assert.Null(slot.Label);
    }

    [Fact]
    public void Assign_AlreadyOccupiedSlot_ReplacesTheActionInPlace_NoDuplicateSlot()
    {
        var layout = OnePageLayout("t6", Slot(2, "ToggleCargoScoop"));

        var result = SlotEditEndpoint.Assign(layout, 0, 2, "LandingGearToggle", null, null);

        var slot = Assert.Single(result.Layout!.Pages[0].Slots);
        Assert.Equal("LandingGearToggle", slot.Action);
    }

    [Fact]
    public void Assign_WithLabel_StoresItAsTheOverride()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", null, "GEAR");

        Assert.Equal("GEAR", result.Layout!.Pages[0].Slots[0].Label);
    }

    [Fact]
    public void Assign_NullLabel_StoresNoOverride()
    {
        var layout = OnePageLayout("t6", Slot(0, "ToggleCargoScoop", label: "SCOOP"));

        // Re-assigning without a label clears any previous override - the
        // caller (the rename dialog) has already decided the final text;
        // this method never re-derives a default of its own.
        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", null, null);

        Assert.Null(result.Layout!.Pages[0].Slots[0].Label);
    }

    [Fact]
    public void Assign_ToAMacro_SetsMacroAndLeavesActionNull()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Assign(layout, 0, 0, null, "request-docking", null);

        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.Null(slot.Action);
        Assert.Equal("request-docking", slot.Macro);
    }

    [Fact]
    public void Assign_BothActionAndMacro_IsInvalidRequest_AndChangesNothing()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", "request-docking", null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.InvalidRequest, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Assign_NeitherActionNorMacro_IsInvalidRequest()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Assign(layout, 0, 0, null, null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.InvalidRequest, result.Outcome);
    }

    [Fact]
    public void Assign_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Assign(layout, 1, 0, "LandingGearToggle", null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
    }

    [Fact]
    public void Assign_SlotIndexOutsideTheTemplatesActiveRange_ReturnsIndexOutOfRange()
    {
        var layout = OnePageLayout("t6"); // t6 has 6 slots: valid indices 0..5

        var result = SlotEditEndpoint.Assign(layout, 0, 6, "LandingGearToggle", null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.IndexOutOfRange, result.Outcome);
    }

    [Fact]
    public void Assign_PreservesAnExistingLongPress_WhenOnlyThePrimaryActionChanges()
    {
        var longPress = new LongPressAction("IncreaseEnginesPower", null);
        var layout = OnePageLayout("t6", Slot(0, "ToggleCargoScoop", longPress: longPress));

        var result = SlotEditEndpoint.Assign(layout, 0, 0, "LandingGearToggle", null, null);

        Assert.Same(longPress, result.Layout!.Pages[0].Slots[0].LongPress);
    }

    [Fact]
    public void Assign_DoesNotTouchOtherSlotsOnThePage()
    {
        var layout = OnePageLayout("t9", Slot(0, "ToggleCargoScoop"), Slot(1, "DeployHeatSink"));

        var result = SlotEditEndpoint.Assign(layout, 0, 2, "LandingGearToggle", null, null);

        Assert.Equal(3, result.Layout!.Pages[0].Slots.Count);
        Assert.Contains(result.Layout.Pages[0].Slots, s => s.Index == 0 && s.Action == "ToggleCargoScoop");
        Assert.Contains(result.Layout.Pages[0].Slots, s => s.Index == 1 && s.Action == "DeployHeatSink");
    }

    [Fact]
    public void Assign_DoesNotTouchOtherPages()
    {
        var page0 = new LayoutPage("SHIP", "t6", new[] { Slot(0, "ToggleCargoScoop") }, Array.Empty<LayoutSlot>());
        var page1 = new LayoutPage("SRV", "t6", new[] { Slot(0, "DeployHeatSink") }, Array.Empty<LayoutSlot>());
        var layout = new Layout(1, new[] { page0, page1 });

        var result = SlotEditEndpoint.Assign(layout, 0, 1, "LandingGearToggle", null, null);

        Assert.Equal("DeployHeatSink", result.Layout!.Pages[1].Slots[0].Action);
    }

    // -------------------------------------------------------------------
    // SetLabel (rename)
    // -------------------------------------------------------------------

    [Fact]
    public void SetLabel_ExistingSlot_SetsTheOverride()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLabel(layout, 0, 0, "Request\nDocking");

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Equal("Request\nDocking", result.Layout!.Pages[0].Slots[0].Label);
    }

    [Fact]
    public void SetLabel_NullOrWhitespace_ClearsTheOverride_FallsBackToCatalogueDefault()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle", label: "GEAR"));

        var result = SlotEditEndpoint.SetLabel(layout, 0, 0, "   ");

        Assert.Null(result.Layout!.Pages[0].Slots[0].Label);
    }

    [Fact]
    public void SetLabel_DoesNotChangeTheSlotsAction()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLabel(layout, 0, 0, "GEAR");

        Assert.Equal("LandingGearToggle", result.Layout!.Pages[0].Slots[0].Action);
    }

    [Fact]
    public void SetLabel_OnAMacroSlot_Works_SinceLabelIsIndependentOfActionVsMacro()
    {
        var layout = OnePageLayout("t6", MacroSlot(0, "request-docking"));

        var result = SlotEditEndpoint.SetLabel(layout, 0, 0, "DOCK");

        Assert.Equal("DOCK", result.Layout!.Pages[0].Slots[0].Label);
        Assert.Equal("request-docking", result.Layout.Pages[0].Slots[0].Macro);
    }

    [Fact]
    public void SetLabel_SlotDoesNotExist_ReturnsNoSuchSlot_AndDoesNotCreateOne()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.SetLabel(layout, 0, 0, "GEAR");

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchSlot, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void SetLabel_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLabel(layout, 1, 0, "GEAR");

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
    }

    // -------------------------------------------------------------------
    // Clear
    // -------------------------------------------------------------------

    [Fact]
    public void Clear_ExistingSlot_RemovesItFromActiveSlots()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.Clear(layout, 0, 0);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Empty(result.Layout!.Pages[0].Slots);
    }

    [Fact]
    public void Clear_AlreadyEmptySlot_IsIdempotent_StillOk()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.Clear(layout, 0, 0);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Empty(result.Layout!.Pages[0].Slots);
    }

    [Fact]
    public void Clear_DoesNotTouchTheParkedList()
    {
        var parked = new LayoutSlot(20, "IncreaseEnginesPower", null, null, null);
        var page = new LayoutPage("SHIP", "t6", new[] { Slot(0, "LandingGearToggle") }, new[] { parked });
        var layout = new Layout(1, new[] { page });

        var result = SlotEditEndpoint.Clear(layout, 0, 0);

        Assert.Single(result.Layout!.Pages[0].Parked);
    }

    [Fact]
    public void Clear_DoesNotTouchOtherSlotsOnThePage()
    {
        var layout = OnePageLayout("t9", Slot(0, "ToggleCargoScoop"), Slot(1, "DeployHeatSink"));

        var result = SlotEditEndpoint.Clear(layout, 0, 0);

        var remaining = Assert.Single(result.Layout!.Pages[0].Slots);
        Assert.Equal(1, remaining.Index);
    }

    [Fact]
    public void Clear_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.Clear(layout, 5, 0);

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Clear_DoesNotConfuseItselfWithRemovingAPage_PageCountIsUnchanged()
    {
        var page0 = new LayoutPage("SHIP", "t6", new[] { Slot(0, "LandingGearToggle") }, Array.Empty<LayoutSlot>());
        var page1 = new LayoutPage("SRV", "t6", new[] { Slot(0, "DeployHeatSink") }, Array.Empty<LayoutSlot>());
        var layout = new Layout(1, new[] { page0, page1 });

        var result = SlotEditEndpoint.Clear(layout, 0, 0);

        Assert.Equal(2, result.Layout!.Pages.Count);
        Assert.Equal("SRV", result.Layout.Pages[1].Name);
    }

    // -------------------------------------------------------------------
    // SetLongPress (ref/docs/editor.md's long-press wiring)
    // -------------------------------------------------------------------

    [Fact]
    public void SetLongPress_ExistingSlot_SetsTheLongPressAction()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, "IncreaseEnginesPower", null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var longPress = result.Layout!.Pages[0].Slots[0].LongPress;
        Assert.NotNull(longPress);
        Assert.Equal("IncreaseEnginesPower", longPress!.Action);
        Assert.Null(longPress.Macro);
    }

    [Fact]
    public void SetLongPress_ToAMacro_SetsMacroAndLeavesActionNull()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, null, "pips-engines");

        var longPress = result.Layout!.Pages[0].Slots[0].LongPress;
        Assert.NotNull(longPress);
        Assert.Null(longPress!.Action);
        Assert.Equal("pips-engines", longPress.Macro);
    }

    [Fact]
    public void SetLongPress_BothActionAndMacro_IsInvalidRequest_AndChangesNothing()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, "IncreaseEnginesPower", "pips-engines");

        Assert.Equal(SlotEditEndpoint.EditOutcome.InvalidRequest, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void SetLongPress_NullBoth_ClearsAnExistingLongPress()
    {
        var existing = new LongPressAction("IncreaseEnginesPower", null);
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle", longPress: existing));

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, null, null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        Assert.Null(result.Layout!.Pages[0].Slots[0].LongPress);
    }

    [Fact]
    public void SetLongPress_DoesNotChangeThePrimaryActionOrLabel()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle", label: "GEAR"));

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, "IncreaseEnginesPower", null);

        var slot = result.Layout!.Pages[0].Slots[0];
        Assert.Equal("LandingGearToggle", slot.Action);
        Assert.Equal("GEAR", slot.Label);
    }

    [Fact]
    public void SetLongPress_SlotDoesNotExist_ReturnsNoSuchSlot_AndDoesNotCreateOne()
    {
        var layout = OnePageLayout("t6");

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, "IncreaseEnginesPower", null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchSlot, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void SetLongPress_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout("t6", Slot(0, "LandingGearToggle"));

        var result = SlotEditEndpoint.SetLongPress(layout, 1, 0, "IncreaseEnginesPower", null);

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
    }

    [Fact]
    public void SetLongPress_DoesNotTouchOtherSlotsOnThePage()
    {
        var layout = OnePageLayout("t9", Slot(0, "ToggleCargoScoop"), Slot(1, "DeployHeatSink"));

        var result = SlotEditEndpoint.SetLongPress(layout, 0, 0, "IncreaseEnginesPower", null);

        Assert.Contains(result.Layout!.Pages[0].Slots, s => s.Index == 1 && s.Action == "DeployHeatSink" && s.LongPress == null);
    }

    // -------------------------------------------------------------------
    // Move (ref/docs/editor.md's "drag a button to a different slot") -
    // the verb behind the tablet's edit-mode drag gesture. The three
    // decisions this set of tests pins, all deliberate:
    //
    // 1. SWAP, never insert-and-push. Dragging A onto an occupied B
    //    exchanges the two. A push-along cascade has no defined direction
    //    on a 2D grid, and it can overflow the template's last index -
    //    which would mean either destroying a slot or inventing a parking
    //    rule, exactly the "interacts with spill and parking in ways nobody
    //    has thought through" editor.md flagged. A swap touches exactly two
    //    indices, can never overflow, and is its own undo.
    // 2. An EMPTY target is the degenerate swap - the source vacates and
    //    the target becomes occupied. Same code path, no special case.
    // 3. An EMPTY SOURCE is refused with NoSuchSlot, matching SetLabel/
    //    SetLongPress/SetLatch's own convention for "this verb acts on a
    //    slot that already exists" rather than inventing a
    //    swap-with-nothing-in-both-directions semantic no screen can reach.
    //
    // Cross-page moves are deliberately out of scope: this verb takes ONE
    // page index for both ends. See ref/docs/editor.md.
    // -------------------------------------------------------------------

    [Fact]
    public void Move_ToAnEmptySlot_MovesTheButton_LeavingTheSourceEmpty()
    {
        var layout = OnePageLayout("t6", Slot(1, "LandingGearToggle", label: "GEAR"));

        var result = SlotEditEndpoint.Move(layout, 0, 1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var moved = Assert.Single(result.Layout!.Pages[0].Slots);
        Assert.Equal(4, moved.Index);
        Assert.Equal("LandingGearToggle", moved.Action);
        Assert.Equal("GEAR", moved.Label);
    }

    [Fact]
    public void Move_ToAnOccupiedSlot_SwapsTheTwo_NeverPushesTheOtherAlong()
    {
        var layout = OnePageLayout("t6", Slot(1, "LandingGearToggle"), Slot(4, "ToggleCargoScoop"), Slot(5, "DeployHeatSink"));

        var result = SlotEditEndpoint.Move(layout, 0, 1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slots = result.Layout!.Pages[0].Slots;
        Assert.Equal("ToggleCargoScoop", slots.Single(s => s.Index == 1).Action);
        Assert.Equal("LandingGearToggle", slots.Single(s => s.Index == 4).Action);
        // The push-along answer would have shifted this one to index 6 (or
        // off the template entirely). A swap never touches a third slot.
        Assert.Equal("DeployHeatSink", slots.Single(s => s.Index == 5).Action);
        Assert.Equal(3, slots.Count);
    }

    [Fact]
    public void Move_CarriesEveryPropertyOfBothSlots_ChangingOnlyTheirIndices()
    {
        // A move changes WHERE a button is, never what it does or how it
        // fires - unlike Assign, which deliberately drops the latch because
        // it changes the primary action itself.
        var longPress = new LongPressAction("IncreaseEnginesPower", null);
        var source = new LayoutSlot(1, "LandingGearToggle", null, "GEAR", longPress, Latch: true);
        var target = new LayoutSlot(4, null, "request-docking", "DOCK", null, Latch: false);
        var layout = OnePageLayout("t6", source, target);

        var result = SlotEditEndpoint.Move(layout, 0, 1, 4);

        var atFour = result.Layout!.Pages[0].Slots.Single(s => s.Index == 4);
        Assert.Equal("LandingGearToggle", atFour.Action);
        Assert.Equal("GEAR", atFour.Label);
        Assert.True(atFour.Latch);
        Assert.Equal(longPress, atFour.LongPress);

        var atOne = result.Layout.Pages[0].Slots.Single(s => s.Index == 1);
        Assert.Equal("request-docking", atOne.Macro);
        Assert.Equal("DOCK", atOne.Label);
        Assert.Null(atOne.Action);
    }

    [Fact]
    public void Move_OntoItself_IsOk_AndChangesNothing()
    {
        var layout = OnePageLayout("t6", Slot(1, "LandingGearToggle", label: "GEAR"));

        var result = SlotEditEndpoint.Move(layout, 0, 1, 1);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var slot = Assert.Single(result.Layout!.Pages[0].Slots);
        Assert.Equal(1, slot.Index);
        Assert.Equal("LandingGearToggle", slot.Action);
        Assert.Equal("GEAR", slot.Label);
    }

    [Fact]
    public void Move_FromAnEmptySlot_ReturnsNoSuchSlot_AndChangesNothing()
    {
        var layout = OnePageLayout("t6", Slot(4, "ToggleCargoScoop"));

        var result = SlotEditEndpoint.Move(layout, 0, 1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchSlot, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Move_TargetIndexOutsideTheTemplate_ReturnsIndexOutOfRange()
    {
        // t6 has exactly six cells, so 6 is one past the end - and an index
        // outside [0, template.Slots) is also how a PARKED slot's index is
        // distinguished, which is what keeps a move from ever landing on
        // one and breaking LayoutValidator's active-or-parked rule.
        var layout = OnePageLayout("t6", Slot(1, "LandingGearToggle"));

        var result = SlotEditEndpoint.Move(layout, 0, 1, 6);

        Assert.Equal(SlotEditEndpoint.EditOutcome.IndexOutOfRange, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Move_SourceIndexOutsideTheTemplate_ReturnsIndexOutOfRange()
    {
        var layout = OnePageLayout("t6", Slot(1, "LandingGearToggle"));

        var result = SlotEditEndpoint.Move(layout, 0, -1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.IndexOutOfRange, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Move_PageIndexOutOfRange_ReturnsNoSuchPage()
    {
        var layout = OnePageLayout("t6", Slot(1, "LandingGearToggle"));

        var result = SlotEditEndpoint.Move(layout, 1, 1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Move_NeverTouchesParkedSlots_OrOtherPages()
    {
        var parked = new[] { new LayoutSlot(9, "DeployHeatSink", null, null, null) };
        var page = new LayoutPage("SHIP", "t6", new[] { Slot(1, "LandingGearToggle") }, parked);
        var other = new LayoutPage("SRV", "t6", new[] { Slot(0, "ToggleCargoScoop") }, Array.Empty<LayoutSlot>());
        var layout = new Layout(1, new[] { page, other });

        var result = SlotEditEndpoint.Move(layout, 0, 1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.Ok, result.Outcome);
        var moved = result.Layout!.Pages[0];
        Assert.Equal(9, Assert.Single(moved.Parked).Index);
        Assert.Equal("DeployHeatSink", moved.Parked[0].Action);
        Assert.Equal("SRV", result.Layout.Pages[1].Name);
        Assert.Equal(0, Assert.Single(result.Layout.Pages[1].Slots).Index);
    }

    [Fact]
    public void Move_KeepsThePagesSlotsOrderedByIndex()
    {
        // Every other verb here leaves Slots index-ordered; a swap that
        // appended instead would leave the page in an order nothing else in
        // this codebase produces.
        var layout = OnePageLayout("t6", Slot(0, "ToggleCargoScoop"), Slot(4, "LandingGearToggle"));

        var result = SlotEditEndpoint.Move(layout, 0, 4, 2);

        Assert.Equal(new[] { 0, 2 }, result.Layout!.Pages[0].Slots.Select(s => s.Index).ToArray());
    }

    [Fact]
    public void Move_UnknownTemplate_ReturnsUnknownTemplate()
    {
        var layout = OnePageLayout("t-nope", Slot(1, "LandingGearToggle"));

        var result = SlotEditEndpoint.Move(layout, 0, 1, 4);

        Assert.Equal(SlotEditEndpoint.EditOutcome.UnknownTemplate, result.Outcome);
    }
}
