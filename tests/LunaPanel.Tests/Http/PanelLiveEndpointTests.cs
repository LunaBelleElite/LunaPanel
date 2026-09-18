using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="PanelLiveEndpoint"/> directly - kept free of any
/// ASP.NET type, same discipline as <see cref="PanelEndpointTests"/>. The
/// actual server-sent-events streaming is only exercised end to end in
/// <c>ServerHostBuilderTests</c>; this class covers what to send, not how.
/// </summary>
public class PanelLiveEndpointTests
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

    private static StatusSnapshot SnapshotWithFlags(uint flags, bool gameRunning = true) => new(flags, null, null, gameRunning, SignedIn: true);

    private static Layout LayoutWith(string templateId, params LayoutSlot[] slots) =>
        new(1, new List<LayoutPage> { new("SHIP", templateId, slots, Array.Empty<LayoutSlot>()) });

    [Fact]
    public void BuildState_NoSnapshot_GameRunningIsFalse_AndSlotsAreNotLit()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, snapshot: null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.False(result.State!.GameRunning);
        Assert.Equal("Off", Assert.Single(result.State.Slots).Lit);
    }

    [Fact]
    public void BuildState_SnapshotPresent_GameRunningReflectsSnapshot()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var runningResult = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, SnapshotWithFlags(0, gameRunning: true));
        var closedResult = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, SnapshotWithFlags(0, gameRunning: false));

        Assert.True(runningResult.State!.GameRunning);
        Assert.False(closedResult.State!.GameRunning);
    }

    [Fact]
    public void BuildState_SnapshotSatisfiesLitCondition_SlotReportsLit()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, SnapshotWithFlags(1u << 2));

        Assert.Equal("Full", Assert.Single(result.State!.Slots).Lit);
    }

    // ---------------------------------------------------------------
    // What a held key and a running macro look like ON THE WIRE.
    //
    // These were the missing middle: LatchedLitTests proved the FUNCTION
    // raises a held slot to Full and a source scan proved the host HANDS the
    // live endpoint its latched actions, but nothing checked that this
    // endpoint's own payload - the thing a phone actually receives - carries
    // it. Two proofs either side of a gap are not a proof across it.
    // ---------------------------------------------------------------

    [Fact]
    public void BuildState_ASlotWhoseKeyIsCurrentlyHeld_GoesOutAsFull()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, null, Latch: true));

        var result = PanelLiveEndpoint.BuildState(
            layout, 0, Catalogue, SnapshotWithFlags(0), latchedActions: new HashSet<string>(StringComparer.Ordinal) { "ToggleCargoScoop" });

        // ToggleCargoScoop carries no lit condition at all in this file's
        // catalogue, so Full here can only have come from the latch - not
        // from the game state, which is what makes this discriminating.
        Assert.Equal("Full", Assert.Single(result.State!.Slots).Lit);
    }

    [Fact]
    public void BuildState_ALatchableSlotWhoseKeyIsNotHeld_GoesOutUnlit()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, "ToggleCargoScoop", null, null, null, Latch: true));

        var result = PanelLiveEndpoint.BuildState(
            layout, 0, Catalogue, SnapshotWithFlags(0), latchedActions: new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal("Off", Assert.Single(result.State!.Slots).Lit);
    }

    [Fact]
    public void BuildState_ASlotWhoseMacroIsRunning_GoesOutAsFull()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, null, "disembark", null, null));

        var running = PanelLiveEndpoint.BuildState(
            layout, 0, Catalogue, SnapshotWithFlags(0), runningMacroIds: new HashSet<string>(StringComparer.Ordinal) { "disembark" });
        var idle = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, SnapshotWithFlags(0));

        Assert.Equal("Full", Assert.Single(running.State!.Slots).Lit);
        Assert.Equal("Off", Assert.Single(idle.State!.Slots).Lit);
    }

    [Fact]
    public void BuildState_OnlyActiveSlots_NeverIncludesParkedSlots()
    {
        var page = new LayoutPage(
            "SHIP",
            "t6",
            new List<LayoutSlot> { new(0, "LandingGearToggle", null, null, null) },
            new List<LayoutSlot> { new(4, "ToggleCargoScoop", null, null, null) });
        var layout = new Layout(1, new List<LayoutPage> { page });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, snapshot: null);

        Assert.Equal(new[] { 0 }, result.State!.Slots.Select(s => s.Index));
    }

    [Fact]
    public void BuildState_PageIndexOutOfRange_ReturnsNoSuchPageOutcome_AndNoState()
    {
        var layout = LayoutWith("t6", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelLiveEndpoint.BuildState(layout, 1, Catalogue, snapshot: null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.NoSuchPage, result.Outcome);
        Assert.Null(result.State);
    }

    [Fact]
    public void BuildState_UnknownTemplateId_ReturnsUnknownTemplateOutcome_AndNoState()
    {
        var layout = LayoutWith("not-a-real-template", new LayoutSlot(0, "LandingGearToggle", null, null, null));

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, snapshot: null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.UnknownTemplate, result.Outcome);
        Assert.Null(result.State);
    }

    [Fact]
    public void StatesEqual_SameGameRunning_SameSlots_DifferentInstances_AreEqual()
    {
        var a = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full"), new(1, "Off") });
        var b = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full"), new(1, "Off") });

        Assert.True(PanelLiveEndpoint.StatesEqual(a, b));
    }

    [Fact]
    public void StatesEqual_DifferentGameRunning_AreNotEqual()
    {
        var a = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") });
        var b = new PanelLiveEndpoint.LiveState(false, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") });

        Assert.False(PanelLiveEndpoint.StatesEqual(a, b));
    }

    [Fact]
    public void StatesEqual_DifferentSlotLitValue_AreNotEqual()
    {
        var a = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") });
        var b = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Off") });

        Assert.False(PanelLiveEndpoint.StatesEqual(a, b));
    }

    [Fact]
    public void StatesEqual_DifferentSlotCount_AreNotEqual()
    {
        var a = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") });
        var b = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full"), new(1, "Off") });

        Assert.False(PanelLiveEndpoint.StatesEqual(a, b));
    }

    /// <summary>
    /// The record's own default equality is NOT what StatesEqual relies on -
    /// this is the exact gap StatesEqual exists to close (see its own
    /// remarks). Two independently-built lists with identical content are
    /// reference-unequal by default, which is why a naive `a == b` on
    /// LiveState itself would wrongly treat every push as a change.
    /// </summary>
    [Fact]
    public void LiveState_DefaultRecordEquality_IsFalseForEqualContent_DifferentListInstances()
    {
        var a = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") });
        var b = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") });

        Assert.NotEqual(a, b);
        Assert.True(PanelLiveEndpoint.StatesEqual(a, b));
    }

    // ------------------------------------------------------------------
    // ShouldPush - a context switch is an edge and must never be deduped
    // away by the lit-state comparison (ref/docs/vessel-context.md).
    // ------------------------------------------------------------------

    private static PanelLiveEndpoint.LiveState State(int? switchToPage = null) =>
        new(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") }, switchToPage);

    [Fact]
    public void ShouldPush_NothingChanged_AndNoSwitch_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(State(), State()));
    }

    /// <summary>
    /// The whole reason this function exists rather than a bare
    /// <see cref="PanelLiveEndpoint.StatesEqual"/> call. Getting into the SRV
    /// does not change the lit state of a page whose controls are all off, so
    /// the two states below are lit-identical - and dropping this push would
    /// drop precisely the event automatic switching is made of.
    /// </summary>
    [Fact]
    public void ShouldPush_LitStateIdentical_ButCarryingASwitch_IsTrue()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(State(switchToPage: 2), State()));
    }

    [Fact]
    public void ShouldPush_SwitchToPageZero_IsStillAPush_NotFalsyDropped()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(State(switchToPage: 0), State()));
    }

    [Fact]
    public void ShouldPush_LitStateChanged_WithNoSwitch_IsStillTrue()
    {
        var lastSent = new PanelLiveEndpoint.LiveState(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Off") });

        Assert.True(PanelLiveEndpoint.ShouldPush(State(), lastSent));
    }

    /// <summary>
    /// The dedupe itself is untouched: a stale <c>SwitchToPage</c> left on
    /// the last-sent state must not make every later push look different, or
    /// LC6's ~11.8s idle heartbeat floods the channel again.
    /// </summary>
    [Fact]
    public void ShouldPush_LastSentCarriedASwitch_ButNothingHasChangedSince_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(State(), State(switchToPage: 2)));
    }

    [Fact]
    public void BuildState_OrdinaryBuild_CarriesNoSwitch()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Null(result.State!.SwitchToPage);
    }

    // ------------------------------------------------------------------
    // Timing (2026-09-10) - a macro-timing change made on the PC, pushed to
    // an already-connected device (ref/docs/macro-timing.md). The same EDGE
    // shape SwitchToPage above has, for the same reason: it describes the
    // one push it caused, never a standing level, so it must survive the
    // lit-state dedupe without weakening it.
    // ------------------------------------------------------------------

    private static PanelLiveEndpoint.LiveState TimingState(PanelLiveEndpoint.TimingDto? timing) =>
        new(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") }, null, timing);

    /// <summary>
    /// Literal milliseconds, never <c>settings.HoldDuration.TotalMilliseconds</c>
    /// restated on the right-hand side - that would be an identity rather
    /// than an assertion.
    /// </summary>
    [Fact]
    public void TimingOf_CarriesBothFieldsAsWholeMilliseconds()
    {
        var dto = PanelLiveEndpoint.TimingOf(
            new MacroTimingSettings(TimeSpan.FromMilliseconds(275), TimeSpan.FromMilliseconds(180)));

        Assert.Equal(275, dto.HoldMs);
        Assert.Equal(180, dto.InterPressGapMs);
    }

    /// <summary>
    /// Not a level. A build caused by anything other than a timing save
    /// carries no timing at all, so a client can treat its presence as "this
    /// just changed" rather than having to compare it with what it already
    /// had.
    /// </summary>
    [Fact]
    public void BuildState_OrdinaryBuild_CarriesNoTiming()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Null(result.State!.Timing);
    }

    /// <summary>
    /// The whole point. A timing save changes nothing about how any button
    /// looks, so the lit-state comparison finds the two states identical -
    /// running this push through it alone would drop every timing change
    /// there is.
    /// </summary>
    [Fact]
    public void ShouldPush_LitStateIdentical_ButCarryingATiming_IsTrue()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(
            TimingState(new PanelLiveEndpoint.TimingDto(275, 180)),
            TimingState(null)));
    }

    /// <summary>
    /// The dedupe is not weakened by adding a field to it: a timing left on
    /// the last-sent state must not make every later push look different, or
    /// LC6's ~11.8s idle heartbeat floods the channel again.
    /// </summary>
    [Fact]
    public void ShouldPush_LastSentCarriedATiming_ButNothingHasChangedSince_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(
            TimingState(null),
            TimingState(new PanelLiveEndpoint.TimingDto(275, 180))));
    }

    /// <summary>
    /// <see cref="PanelLiveEndpoint.StatesEqual"/> answers "does any button
    /// look different", and timing is not a button - so it stays out of that
    /// comparison, exactly as <c>SwitchToPage</c> does. This is what makes
    /// the dedupe test above mean what it says rather than passing because
    /// the comparison happens to include the field.
    /// </summary>
    [Fact]
    public void StatesEqual_DiffersOnlyByTiming_IsStillEqual()
    {
        Assert.True(PanelLiveEndpoint.StatesEqual(
            TimingState(new PanelLiveEndpoint.TimingDto(275, 180)),
            TimingState(null)));
    }

    // ------------------------------------------------------------------
    // BindingsChanged (2026-09-10) - a rebind made in Elite, reaching an
    // already-open device (BindsFileWatcher). The same EDGE shape
    // SwitchToPage/Timing above have, for the same reason: it describes the
    // one push it caused, never a standing level, so it must survive the
    // lit-state dedupe without weakening it.
    // ------------------------------------------------------------------

    private static PanelLiveEndpoint.LiveState BindingsState(bool? bindingsChanged) =>
        new(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") }, null, null, bindingsChanged);

    /// <summary>
    /// Not a level. A build caused by anything other than a bindings-file
    /// change carries no signal at all, so a client can treat its presence
    /// as "this just changed" rather than having to compare it with what it
    /// already had.
    /// </summary>
    [Fact]
    public void BuildState_OrdinaryBuild_CarriesNoBindingsChanged()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Null(result.State!.BindingsChanged);
    }

    /// <summary>
    /// The whole point. A rebind very often changes nothing about how any
    /// button currently looks (lit state is driven by the game snapshot, not
    /// by which key an action is bound to), so the lit-state comparison
    /// finds the two states identical - running this push through it alone
    /// would drop the one push this whole feature exists to deliver.
    /// </summary>
    [Fact]
    public void ShouldPush_LitStateIdentical_ButCarryingABindingsChangedSignal_IsTrue()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(BindingsState(true), BindingsState(null)));
    }

    /// <summary>
    /// The dedupe is not weakened by adding a field to it: a signal left on
    /// the last-sent state must not make every later push look different, or
    /// LC6's ~11.8s idle heartbeat floods the channel again.
    /// </summary>
    [Fact]
    public void ShouldPush_LastSentCarriedABindingsChangedSignal_ButNothingHasChangedSince_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(BindingsState(null), BindingsState(true)));
    }

    /// <summary>
    /// <see cref="PanelLiveEndpoint.StatesEqual"/> answers "does any button
    /// look different", and a bindings-changed signal is not a button - so it
    /// stays out of that comparison, exactly as <c>SwitchToPage</c>/<c>Timing</c>
    /// do. This is what makes the dedupe test above mean what it says rather
    /// than passing because the comparison happens to include the field.
    /// </summary>
    [Fact]
    public void StatesEqual_DiffersOnlyByBindingsChanged_IsStillEqual()
    {
        Assert.True(PanelLiveEndpoint.StatesEqual(BindingsState(true), BindingsState(null)));
    }

    // ------------------------------------------------------------------
    // LayoutChanged (2026-09-13) - a device's layout edited live from the PC
    // (?asDevice=<id>), reaching that device's own already-open live
    // channel (ServerHostBuilder.OnLayoutSaved). The same EDGE shape
    // BindingsChanged above has, for the same reason: it describes the one
    // push it caused, never a standing level, so it must survive the
    // lit-state dedupe without weakening it.
    // ------------------------------------------------------------------

    private static PanelLiveEndpoint.LiveState LayoutState(bool? layoutChanged) =>
        new(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") }, null, null, null, layoutChanged);

    /// <summary>
    /// Not a level. A build caused by anything other than a layout save
    /// reaching this device carries no signal at all, so a client can treat
    /// its presence as "this just changed" rather than having to compare it
    /// with what it already had.
    /// </summary>
    [Fact]
    public void BuildState_OrdinaryBuild_CarriesNoLayoutChanged()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Null(result.State!.LayoutChanged);
    }

    /// <summary>
    /// The whole point. A layout edit very often changes nothing about how
    /// any button currently looks (a relabel, a reorder, a slot reassigned
    /// to another action with the same lit level), so the lit-state
    /// comparison finds the two states identical - running this push through
    /// it alone would drop the one push this fix exists to deliver.
    /// </summary>
    [Fact]
    public void ShouldPush_LitStateIdentical_ButCarryingALayoutChangedSignal_IsTrue()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(LayoutState(true), LayoutState(null)));
    }

    /// <summary>
    /// The dedupe is not weakened by adding a field to it: a signal left on
    /// the last-sent state must not make every later push look different, or
    /// LC6's ~11.8s idle heartbeat floods the channel again.
    /// </summary>
    [Fact]
    public void ShouldPush_LastSentCarriedALayoutChangedSignal_ButNothingHasChangedSince_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(LayoutState(null), LayoutState(true)));
    }

    /// <summary>
    /// <see cref="PanelLiveEndpoint.StatesEqual"/> answers "does any button
    /// look different", and a layout-changed signal is not a button - so it
    /// stays out of that comparison, exactly as <c>SwitchToPage</c>/<c>Timing</c>/
    /// <c>BindingsChanged</c> do. This is what makes the dedupe test above
    /// mean what it says rather than passing because the comparison happens
    /// to include the field.
    /// </summary>
    [Fact]
    public void StatesEqual_DiffersOnlyByLayoutChanged_IsStillEqual()
    {
        Assert.True(PanelLiveEndpoint.StatesEqual(LayoutState(true), LayoutState(null)));
    }

    // ------------------------------------------------------------------
    // MacroFinished (2026-09-17, O28) - a macro run's outcome, which
    // POST /api/press used to carry synchronously, now arriving on the
    // device's live channel once the run ends. The same EDGE shape the four
    // fields above have, for the same reason: it describes the one push it
    // caused, never a standing level, so it must survive the lit-state
    // dedupe without weakening it.
    // ------------------------------------------------------------------

    private static PanelLiveEndpoint.MacroFinishedDto SomeFinished() =>
        new("m", true, "Sent", null, null, Array.Empty<PanelLiveEndpoint.MacroStepDto>());

    private static PanelLiveEndpoint.LiveState MacroFinishedState(PanelLiveEndpoint.MacroFinishedDto? finished) =>
        new(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") }, null, null, null, null, finished);

    [Fact]
    public void BuildState_OrdinaryBuild_CarriesNoMacroFinished()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Null(result.State!.MacroFinished);
    }

    /// <summary>
    /// The whole point, and the trap this codebase has already fallen into
    /// four times: by the time a run ends the runner has already taken its
    /// id out of the running set and the dark push has gone out, so the
    /// finished push's lit state is IDENTICAL to the last one sent. Run
    /// through the lit-state comparison alone, every single outcome would be
    /// dropped.
    /// </summary>
    [Fact]
    public void ShouldPush_LitStateIdentical_ButCarryingAMacroFinished_IsTrue()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(MacroFinishedState(SomeFinished()), MacroFinishedState(null)));
    }

    [Fact]
    public void ShouldPush_LastSentCarriedAMacroFinished_ButNothingHasChangedSince_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(MacroFinishedState(null), MacroFinishedState(SomeFinished())));
    }

    [Fact]
    public void StatesEqual_DiffersOnlyByMacroFinished_IsStillEqual()
    {
        Assert.True(PanelLiveEndpoint.StatesEqual(MacroFinishedState(SomeFinished()), MacroFinishedState(null)));
    }

    /// <summary>
    /// The projection is the one the press response's macro branch used to
    /// apply, field for field - asserted against literals, not against
    /// another call into production.
    /// </summary>
    [Fact]
    public void MacroFinishedOf_CarriesEveryFieldThePressResponseUsedTo_UnderTheSameNames()
    {
        var result = new LunaPanel.Server.Input.MacroPresser.MacroPressResult(
            false,
            "MacroAborted",
            "action 'X' is not bound.",
            1,
            new[]
            {
                new MacroStepProgress(0, "press", MacroStepOutcome.Succeeded, TimeSpan.FromMilliseconds(150)),
                new MacroStepProgress(1, "press", MacroStepOutcome.Failed, TimeSpan.FromMilliseconds(0.5)),
            });

        var dto = PanelLiveEndpoint.MacroFinishedOf("request-docking", result);

        Assert.Equal("request-docking", dto.MacroId);
        Assert.False(dto.Fired);
        Assert.Equal("MacroAborted", dto.Outcome);
        Assert.Equal("action 'X' is not bound.", dto.Reason);
        Assert.Equal(1, dto.FailedStepIndex);
        Assert.Equal(2, dto.Steps.Count);
        Assert.Equal(new PanelLiveEndpoint.MacroStepDto(0, "press", "Succeeded", 150), dto.Steps[0]);
        Assert.Equal(new PanelLiveEndpoint.MacroStepDto(1, "press", "Failed", 0.5), dto.Steps[1]);
    }

    // ------------------------------------------------------------------
    // ThemeChanged - an EDHM colour edit reaching an already-open device
    // (ThemeFileWatcher). The same EDGE shape BindingsChanged/LayoutChanged
    // above have, for the same reason: it describes the one push it caused,
    // never a standing level, so it must survive the lit-state dedupe
    // without weakening it.
    // ------------------------------------------------------------------

    private static PanelLiveEndpoint.LiveState ThemeState(bool? themeChanged) =>
        new(true, new List<PanelLiveEndpoint.SlotLitDto> { new(0, "Full") }, ThemeChanged: themeChanged);

    /// <summary>
    /// Not a level. A build caused by anything other than a theme-file
    /// change carries no signal at all, so a client can treat its presence
    /// as "this just changed" rather than having to compare it with what it
    /// already had.
    /// </summary>
    [Fact]
    public void BuildState_OrdinaryBuild_CarriesNoThemeChanged()
    {
        var layout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>())
        });

        var result = PanelLiveEndpoint.BuildState(layout, 0, Catalogue, null);

        Assert.Equal(PanelLiveEndpoint.BuildOutcome.Ok, result.Outcome);
        Assert.Null(result.State!.ThemeChanged);
    }

    /// <summary>
    /// The whole point. An EDHM colour edit very often changes nothing about
    /// how any button's LIT state looks (lit state is driven by the game
    /// snapshot and layout, not by colour), so the lit-state comparison finds
    /// the two states identical - running this push through it alone would
    /// drop the one push this whole mechanism exists to deliver.
    /// </summary>
    [Fact]
    public void ShouldPush_LitStateIdentical_ButCarryingAThemeChangedSignal_IsTrue()
    {
        Assert.True(PanelLiveEndpoint.ShouldPush(ThemeState(true), ThemeState(null)));
    }

    /// <summary>
    /// The dedupe is not weakened by adding a field to it: a signal left on
    /// the last-sent state must not make every later push look different, or
    /// LC6's ~11.8s idle heartbeat floods the channel again.
    /// </summary>
    [Fact]
    public void ShouldPush_LastSentCarriedAThemeChangedSignal_ButNothingHasChangedSince_IsFalse()
    {
        Assert.False(PanelLiveEndpoint.ShouldPush(ThemeState(null), ThemeState(true)));
    }

    /// <summary>
    /// <see cref="PanelLiveEndpoint.StatesEqual"/> answers "does any button
    /// look different", and a theme-changed signal is not a button - so it
    /// stays out of that comparison, exactly as <c>BindingsChanged</c>/
    /// <c>LayoutChanged</c> do. This is what makes the dedupe test above mean
    /// what it says rather than passing because the comparison happens to
    /// include the field.
    /// </summary>
    [Fact]
    public void StatesEqual_DiffersOnlyByThemeChanged_IsStillEqual()
    {
        Assert.True(PanelLiveEndpoint.StatesEqual(ThemeState(true), ThemeState(null)));
    }
}
