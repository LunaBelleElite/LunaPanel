using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins <see cref="PanelTabTracker"/> against every mechanism
/// <c>ref/docs/panel-tab-tracking.md</c> names: tracking our own presses,
/// the two free re-syncs, and dropping <see cref="TabConfidence"/> to
/// <see cref="TabConfidence.Low"/> - never nulling the tracked tab - the
/// moment an edge we did not cause happens. No I/O anywhere in this file -
/// the whole point of this class is that it needs none.
///
/// Reversed 2026-09-08 at the user's explicit governing decision ("We
/// should never NOT be allowed to use a macro.") - see
/// <c>PanelTabTracker</c>'s own remarks, "Reversed 2026-09-08". Before this,
/// an uncredited edge nulled <c>CurrentTab</c>/<c>RightCurrentTab</c> and
/// every test in this file that now asserts a surviving value plus
/// <see cref="TabConfidence.Low"/> instead asserted <c>Assert.Null(...)</c>.
/// </summary>
public class PanelTabTrackerTests
{
    [Fact]
    public void CurrentTab_StartsAtNavigation_ThePresumedSessionStartPosition()
    {
        // Fix 7 (2026-09-07, before-value: this test was named
        // CurrentTab_StartsUnknown and asserted Assert.Null(...)). The
        // commander confirmed the left panel is reliably on NAVIGATION at
        // session start, and a server start almost always coincides with or
        // precedes a game session - see PanelTabTracker's remarks, "Fix 7".
        Assert.Equal(PanelTab.Navigation, new PanelTabTracker().CurrentTab);
    }

    [Fact]
    public void CurrentTab_StartsAtHighConfidence()
    {
        Assert.Equal(TabConfidence.High, new PanelTabTracker().LeftTabConfidence);
    }

    [Fact]
    public void RightCurrentTab_StartsAtHome_TheSamePresumedSessionStartPosition()
    {
        // Added by Fix 7 alongside the left panel's own presumption above -
        // same justification, same safeguard.
        Assert.Equal(RightPanelTab.Home, new PanelTabTracker().RightCurrentTab);
    }

    [Fact]
    public void RightCurrentTab_StartsAtHighConfidence()
    {
        Assert.Equal(TabConfidence.High, new PanelTabTracker().RightTabConfidence);
    }

    // -----------------------------------------------------------------
    // The two free re-syncs (LC4, LC15) - restore High confidence too, not
    // just the tab value, per the brief ("Add a test proving FSD jump /
    // on-foot round trip restore high confidence").
    // -----------------------------------------------------------------

    [Fact]
    public void RecordFsdJump_SetsCurrentTabToNavigation()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordFsdJump();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordFsdJump_OverridesAPreviouslyKnownTab()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        tracker.RecordOwnTabAdvance(); // Navigation -> Transactions

        tracker.RecordFsdJump();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordFsdJump_OverridesEvenALowConfidencePosition()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // an edge we didn't arm -> Low confidence
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);

        tracker.RecordFsdJump();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordFsdJump_RestoresHighConfidence_AfterAnUncreditedEdgeLoweredIt()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // Low confidence
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);

        tracker.RecordFsdJump();

        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordOnFootRoundTrip_SetsCurrentTabToNavigation()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordOnFootRoundTrip();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordOnFootRoundTrip_RestoresHighConfidence_AfterAnUncreditedEdgeLoweredIt()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // Low confidence
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);

        tracker.RecordOnFootRoundTrip();

        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // The session-start seed (Fix 4, 2026-09-07) - same shape as the two
    // re-syncs above, pinned separately because it is a distinct arm the
    // caller reaches for a distinct reason (LoadGame, not FSDJump/Embark),
    // and the caller-side "live only" constraint lives in the class doc
    // remarks, not in this method's own logic.
    // -----------------------------------------------------------------

    [Fact]
    public void RecordLoadGame_SetsCurrentTabToNavigation()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordLoadGame();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordLoadGame_OverridesAPreviouslyKnownTab()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        tracker.RecordOwnTabAdvance(); // Navigation -> Transactions

        tracker.RecordLoadGame();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordLoadGame_OverridesEvenALowConfidencePosition()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // an edge we didn't arm -> Low confidence
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);

        tracker.RecordLoadGame();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Advancing our own presses - wraps. Reversed 2026-09-08: advancing no
    // longer requires a known/High-confidence starting point, because a
    // believed index always exists now - see "Belief survives an uncredited
    // edge" below for the claim that actually matters here.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(PanelTab.Galaxy, PanelTab.Navigation)]
    [InlineData(PanelTab.Navigation, PanelTab.Transactions)]
    [InlineData(PanelTab.Transactions, PanelTab.Contacts)]
    [InlineData(PanelTab.Contacts, PanelTab.Galaxy)]
    public void RecordOwnTabAdvance_MovesOneStepForward_WrappingAtTheEnd(PanelTab from, PanelTab to)
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // anchors at Navigation
        // Walk to `from` first via repeated advances from the known anchor.
        while (tracker.CurrentTab != from)
        {
            tracker.RecordOwnTabAdvance();
        }

        tracker.RecordOwnTabAdvance();

        Assert.Equal(to, tracker.CurrentTab);
    }

    [Fact]
    public void RecordOwnTabAdvance_AtLowConfidence_StillAdvances_ConfidenceUnchanged()
    {
        // Reversed 2026-09-08 (before-value: this test was named
        // RecordOwnTabAdvance_WhileUnknown_StaysUnknown and asserted
        // Assert.Null(tracker.CurrentTab) after the same setup - advancing
        // used to be a no-op once the tab was unknown; there is no more
        // "unknown" to be stuck in).
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // Low confidence, still Navigation

        tracker.RecordOwnTabAdvance();

        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);
        // Advancing our own belief is not a confirmation of it - confidence
        // stays exactly where the edge left it.
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Retreating our own presses (CyclePreviousPanel) - the exact mirror of
    // RecordOwnTabAdvance, added to close the plain-press hole
    // (ref/docs/panel-tab-tracking.md's "Fix 6"): a commander can retreat a
    // tab exactly as they can advance one, and until now nothing tracked it
    // at all, from either surface.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(PanelTab.Navigation, PanelTab.Galaxy)]
    [InlineData(PanelTab.Transactions, PanelTab.Navigation)]
    [InlineData(PanelTab.Contacts, PanelTab.Transactions)]
    [InlineData(PanelTab.Galaxy, PanelTab.Contacts)]
    public void RecordOwnTabRetreat_MovesOneStepBackward_WrappingAtTheStart(PanelTab from, PanelTab to)
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // anchors at Navigation
        while (tracker.CurrentTab != from)
        {
            tracker.RecordOwnTabAdvance();
        }

        tracker.RecordOwnTabRetreat();

        Assert.Equal(to, tracker.CurrentTab);
    }

    [Fact]
    public void RecordOwnTabRetreat_AtLowConfidence_StillRetreats()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // Low confidence, still Navigation

        tracker.RecordOwnTabRetreat();

        Assert.Equal(PanelTab.Galaxy, tracker.CurrentTab);
    }

    [Fact]
    public void RecordOwnTabRetreat_UndoesRecordOwnTabAdvance()
    {
        // Not load-bearing on its own (retreat is defined independently by
        // its own wrapping arithmetic, not "whatever undoes advance") but a
        // cheap sanity cross-check that the two arithmetics actually agree.
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        var start = tracker.CurrentTab;

        tracker.RecordOwnTabAdvance();
        tracker.RecordOwnTabRetreat();

        Assert.Equal(start, tracker.CurrentTab);
    }

    // -----------------------------------------------------------------
    // Detecting staleness: an edge we caused vs. one we didn't. Reversed
    // 2026-09-08: an uncredited edge drops confidence to Low and LEAVES the
    // tracked tab untouched - it used to null it outright.
    // -----------------------------------------------------------------

    [Fact]
    public void RecordLeftPanelEdge_WithoutAnArmedOwnToggle_DropsConfidenceToLow_LeavingTheTabUntouched()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        tracker.RecordLeftPanelEdge();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordLeftPanelEdge_WithoutAnArmedOwnToggle_BeliefSurvives_EvenAwayFromNavigation()
    {
        // The exact claim the brief asks for by name: "if somehow we were in
        // contacts and they open up left panel" LunaPanel still believes
        // Contacts until something says otherwise - the belief that
        // survives is whatever it actually was, not always the Fix 7
        // presumption.
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        tracker.RecordOwnTabAdvance(); // Navigation -> Transactions
        tracker.RecordOwnTabAdvance(); // Transactions -> Contacts
        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);

        tracker.RecordLeftPanelEdge(); // uncredited - the commander may have touched it

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordOwnLeftPanelToggle_ThenEdge_LeavesTheTrackedTabUntouched_AndKeepsHighConfidence()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        tracker.RecordOwnLeftPanelToggle();
        tracker.RecordLeftPanelEdge();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordOwnLeftPanelToggle_ArmsExactlyOneEdge_NotEveryFutureOne()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        tracker.RecordOwnLeftPanelToggle();
        tracker.RecordLeftPanelEdge(); // consumes the arm - this is "our" open
        tracker.RecordLeftPanelEdge(); // NOT armed - the commander closed it by hand

        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void ASecondOwnToggle_MustBeArmedAgain_ArmingIsNotSticky()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        tracker.RecordOwnLeftPanelToggle();
        tracker.RecordLeftPanelEdge(); // our open, consumed

        tracker.RecordLeftPanelEdge(); // an unarmed close - not ours

        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Two own toggles per run (the live 2026-09-07 bug) - request-docking
    // presses FocusLeftPanel twice (open at step 1, close at step 5) before
    // either GuiFocus edge arrives, because the edges come back
    // asynchronously from Status.json polling. A one-shot bool arm loses
    // the first arm the instant the second RecordOwnLeftPanelToggle() call
    // happens (setting an already-true flag is a no-op), so only one of the
    // two edges is ever treated as "ours" and the tracked tab goes unknown
    // right after a successful run. The fix models this as a COUNT: one
    // credit per toggle, one credit consumed per edge, matched FIFO - order
    // doesn't matter for correctness here, only that the count matches.
    // -----------------------------------------------------------------

    [Fact]
    public void TwoOwnTogglesArmedBeforeEitherEdgeArrives_BothEdgesAreConsumed()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // anchors at Navigation

        // Both presses (open, then close) happen back-to-back, exactly as
        // MacroRunner does across request-docking's step 1 and step 5, well
        // before either GuiFocus edge has had a chance to arrive.
        tracker.RecordOwnLeftPanelToggle();
        tracker.RecordOwnLeftPanelToggle();

        // Both edges then land late, in order.
        tracker.RecordLeftPanelEdge();
        tracker.RecordLeftPanelEdge();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void TwoOwnTogglesArmed_AThirdUnarmedEdge_StillDropsConfidenceToLow()
    {
        // The count must not over-forgive: exactly two credits were armed,
        // so a THIRD edge with nothing left to consume is still the
        // commander.
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        tracker.RecordOwnLeftPanelToggle();
        tracker.RecordOwnLeftPanelToggle();

        tracker.RecordLeftPanelEdge(); // consumes credit 1
        tracker.RecordLeftPanelEdge(); // consumes credit 2
        tracker.RecordLeftPanelEdge(); // no credit left - the commander

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Revoking a credit whose press turned out to be refused (Fix 6,
    // 2026-09-07) - RecordOwnLeftPanelToggle must be called BEFORE the
    // keystroke goes out (to win the race against Status.json's confirming
    // GuiFocus edge), which means it has to bank a credit before anyone
    // knows whether the keystroke will actually reach Elite. If it turns out
    // to have been refused (game not foreground, UIPI), the credit must be
    // taken back immediately - not left to expire on its own after
    // OwnToggleArmTtl - because a genuine hand-driven edge landing inside
    // that window would otherwise be wrongly absorbed by a credit whose
    // press never happened, hiding real staleness.
    // -----------------------------------------------------------------

    [Fact]
    public void RevokeOwnLeftPanelToggle_RemovesTheCredit_SoTheNextEdgeIsExternal()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var creditId = tracker.RecordOwnLeftPanelToggle();
        tracker.RevokeOwnLeftPanelToggle(creditId);

        tracker.RecordLeftPanelEdge(); // no credit left - the commander
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RevokeOwnLeftPanelToggle_OnlyRemovesTheNamedCredit_ASeparateOneStillArms()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var refusedId = tracker.RecordOwnLeftPanelToggle();
        tracker.RecordOwnLeftPanelToggle(); // a second, genuinely sent press
        tracker.RevokeOwnLeftPanelToggle(refusedId);

        tracker.RecordLeftPanelEdge(); // consumes the surviving credit
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RevokeOwnLeftPanelToggle_UnknownId_IsANoOp()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        var creditId = tracker.RecordOwnLeftPanelToggle();

        tracker.RevokeOwnLeftPanelToggle(creditId + 999); // not a real id

        tracker.RecordLeftPanelEdge(); // the real credit is still there
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RevokeOwnLeftPanelToggle_AlreadyConsumedByAnEdge_IsANoOp()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        var creditId = tracker.RecordOwnLeftPanelToggle();
        tracker.RecordLeftPanelEdge(); // consumes it already

        tracker.RevokeOwnLeftPanelToggle(creditId); // too late, and must not throw

        // A second, unarmed edge is still external - revoking the
        // already-spent credit must not have somehow un-consumed it.
        tracker.RecordLeftPanelEdge();
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Bounding drift: an armed toggle whose edge never arrives (game not
    // focused, keystroke dropped) must not stay armed forever - an
    // unbounded credit would eventually swallow a genuine hand-opened panel
    // and confidently report a wrong tab, which is worse than the staleness
    // bug this whole class exists to prevent. Chosen bound: a credit expires
    // after PanelTabTracker.OwnToggleArmTtl, a generous multiple of
    // StatusFileWatcher.SafetyPollInterval (1s) - comfortably longer than
    // any real GuiFocus edge should ever take to arrive, so a genuine own
    // edge is never mistaken for a dropped one, while a truly dropped
    // credit still clears out on its own instead of lingering indefinitely.
    // -----------------------------------------------------------------

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    [Fact]
    public void AnArmedToggle_WhoseEdgeNeverArrives_ExpiresAfterTheTtl_SoALaterEdgeIsExternal()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = new PanelTabTracker(clock);
        tracker.RecordFsdJump();

        tracker.RecordOwnLeftPanelToggle(); // its edge never arrives (dropped keystroke)
        clock.Advance(PanelTabTracker.OwnToggleArmTtl + TimeSpan.FromMilliseconds(1));

        // A later edge with no fresh toggle is the commander, not the stale credit.
        tracker.RecordLeftPanelEdge();

        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void AnArmedToggle_EdgeArrivesJustBeforeTheTtlExpires_IsStillConsumed()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var tracker = new PanelTabTracker(clock);
        tracker.RecordFsdJump();

        tracker.RecordOwnLeftPanelToggle();
        clock.Advance(PanelTabTracker.OwnToggleArmTtl - TimeSpan.FromMilliseconds(1));

        tracker.RecordLeftPanelEdge();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // PressesToReach - the whole reason this class exists for a macro.
    // Reversed 2026-09-08: always computable now, never null - a macro
    // always has a route, however much it should be trusted.
    // -----------------------------------------------------------------

    [Fact]
    public void PressesToReach_AtLowConfidence_StillComputesARoute()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // Low confidence, still Navigation

        Assert.Equal(2, tracker.PressesToReach(PanelTab.Contacts));
    }

    [Theory]
    [InlineData(PanelTab.Navigation, PanelTab.Contacts, 2)]
    [InlineData(PanelTab.Contacts, PanelTab.Navigation, 2)]
    [InlineData(PanelTab.Contacts, PanelTab.Contacts, 0)]
    [InlineData(PanelTab.Navigation, PanelTab.Galaxy, 3)]
    [InlineData(PanelTab.Galaxy, PanelTab.Navigation, 1)]
    public void PressesToReach_ComputesTheWrappingDistance(PanelTab from, PanelTab target, int expected)
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();
        while (tracker.CurrentTab != from)
        {
            tracker.RecordOwnTabAdvance();
        }

        Assert.Equal(expected, tracker.PressesToReach(target));
    }

    [Fact]
    public void PressesToReach_IsAlwaysInRangeZeroToThree()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        foreach (PanelTab target in Enum.GetValues<PanelTab>())
        {
            var presses = tracker.PressesToReach(target);
            Assert.InRange(presses, 0, 3);
        }
    }

    // -----------------------------------------------------------------
    // Fix 7, 2026-09-07: the right panel - the exact same mechanism as the
    // left (shared SidePanel implementation), mirrored for RightPanelTab's
    // eight tabs.
    // -----------------------------------------------------------------

    // No longer needs to be bounded against a "stuck unknown" no-op loop -
    // Reversed 2026-09-08, RecordOwnRightTabAdvance always advances - but
    // kept as a `for` loop anyway: a plain `while` here would still spin
    // forever if `target` were never reached for some other reason, and a
    // bounded helper that fails loudly is strictly safer than one that can
    // hang, cheaply, with no downside now that the loop is guaranteed to
    // terminate well within one cycle.
    private static void WalkRightTo(PanelTabTracker tracker, RightPanelTab target)
    {
        for (var i = 0; i < 8; i++)
        {
            if (tracker.RightCurrentTab == target)
            {
                return;
            }

            tracker.RecordOwnRightTabAdvance();
        }

        Assert.Fail($"RightCurrentTab never reached {target} within one full cycle.");
    }

    [Theory]
    [InlineData(RightPanelTab.Home, RightPanelTab.Modules)]
    [InlineData(RightPanelTab.Playlist, RightPanelTab.Home)] // wraps
    public void RecordOwnRightTabAdvance_MovesOneStepForward_WrappingAtTheEnd(RightPanelTab from, RightPanelTab to)
    {
        var tracker = new PanelTabTracker();
        WalkRightTo(tracker, from);

        tracker.RecordOwnRightTabAdvance();

        Assert.Equal(to, tracker.RightCurrentTab);
    }

    [Theory]
    [InlineData(RightPanelTab.Modules, RightPanelTab.Home)]
    [InlineData(RightPanelTab.Home, RightPanelTab.Playlist)] // wraps
    public void RecordOwnRightTabRetreat_MovesOneStepBackward_WrappingAtTheStart(RightPanelTab from, RightPanelTab to)
    {
        var tracker = new PanelTabTracker();
        WalkRightTo(tracker, from);

        tracker.RecordOwnRightTabRetreat();

        Assert.Equal(to, tracker.RightCurrentTab);
    }

    [Fact]
    public void RecordOwnRightTabAdvance_AtLowConfidence_StillAdvances()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordRightPanelEdge(); // Low confidence, still Home

        tracker.RecordOwnRightTabAdvance();

        Assert.Equal(RightPanelTab.Modules, tracker.RightCurrentTab);
    }

    [Fact]
    public void RecordRightPanelEdge_WithoutAnArmedOwnToggle_DropsConfidenceToLow_LeavingTheTabUntouched()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordRightPanelEdge();

        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.RightTabConfidence);
    }

    [Fact]
    public void RecordOwnRightPanelToggle_ThenEdge_LeavesTheTrackedRightTabUntouched_AndKeepsHighConfidence()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordOwnRightPanelToggle();
        tracker.RecordRightPanelEdge();

        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab);
        Assert.Equal(TabConfidence.High, tracker.RightTabConfidence);
    }

    [Fact]
    public void RecordRightPanelEdge_DoesNotAffectTheLeftTab_AndViceVersa()
    {
        // The two SidePanel instances are independent state - an edge on
        // one side must never touch the other's tracked tab or confidence.
        var tracker = new PanelTabTracker();
        tracker.RecordOwnTabAdvance(); // Navigation -> Transactions

        tracker.RecordRightPanelEdge(); // uncredited - right confidence drops

        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.RightTabConfidence);
    }

    [Fact]
    public void RevokeOwnRightPanelToggle_RemovesTheCredit_SoTheNextEdgeIsExternal()
    {
        var tracker = new PanelTabTracker();

        var creditId = tracker.RecordOwnRightPanelToggle();
        tracker.RevokeOwnRightPanelToggle(creditId);

        tracker.RecordRightPanelEdge();
        Assert.Equal(TabConfidence.Low, tracker.RightTabConfidence);
    }

    [Fact]
    public void PressesToReachRight_AtLowConfidence_StillComputesARoute()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordRightPanelEdge(); // Low confidence, still Home

        Assert.Equal(5, tracker.PressesToReachRight(RightPanelTab.Storage));
    }

    [Theory]
    [InlineData(RightPanelTab.Home, RightPanelTab.Playlist, 7)]
    [InlineData(RightPanelTab.Home, RightPanelTab.Home, 0)]
    [InlineData(RightPanelTab.Playlist, RightPanelTab.Home, 1)]
    [InlineData(RightPanelTab.Home, RightPanelTab.Modules, 1)]
    public void PressesToReachRight_ComputesTheWrappingDistance(RightPanelTab from, RightPanelTab target, int expected)
    {
        var tracker = new PanelTabTracker();
        WalkRightTo(tracker, from);

        Assert.Equal(expected, tracker.PressesToReachRight(target));
    }

    [Fact]
    public void PressesToReachRight_IsAlwaysInRangeZeroToSeven()
    {
        var tracker = new PanelTabTracker();

        foreach (RightPanelTab target in Enum.GetValues<RightPanelTab>())
        {
            var presses = tracker.PressesToReachRight(target);
            Assert.InRange(presses, 0, 7);
        }
    }

    // -----------------------------------------------------------------
    // Fix 7: the tracker's own belief of which panel is focused, and the
    // internal routing (RecordOwnFocusedTabAdvance/Retreat) PanelTabPressEffect
    // uses for the shared CycleNextPanel/CyclePreviousPanel actions - "a
    // press attributed to the wrong panel corrupts both models at once", per
    // the brief that asked for this to be pinned.
    // -----------------------------------------------------------------

    [Fact]
    public void RecordOwnFocusedTabAdvance_WithNeitherPanelBelievedOpen_IsANoOp_ForBothPanels()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordOwnFocusedTabAdvance();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // unchanged
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab); // unchanged
    }

    [Fact]
    public void RecordOwnLeftPanelToggle_ThenFocusedAdvance_AdvancesOnlyTheLeftTab()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordOwnLeftPanelToggle(); // belief -> Left
        tracker.RecordOwnFocusedTabAdvance();

        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab); // untouched
    }

    [Fact]
    public void RecordOwnRightPanelToggle_ThenFocusedAdvance_AdvancesOnlyTheRightTab()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordOwnRightPanelToggle(); // belief -> Right
        tracker.RecordOwnFocusedTabAdvance();

        Assert.Equal(RightPanelTab.Modules, tracker.RightCurrentTab);
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // untouched
    }

    [Fact]
    public void RecordOwnRightPanelToggle_ThenFocusedRetreat_RetreatsOnlyTheRightTab()
    {
        var tracker = new PanelTabTracker();

        tracker.RecordOwnRightPanelToggle(); // belief -> Right
        tracker.RecordOwnFocusedTabRetreat();

        Assert.Equal(RightPanelTab.Playlist, tracker.RightCurrentTab); // wraps backward
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // untouched
    }

    [Fact]
    public void ASecondLeftToggle_ClosingTheBelief_MakesFocusedAdvanceANoOpAgain()
    {
        // FocusLeftPanel toggles: open, then close - the second call must
        // flip the belief back to "nothing focused," not leave it stuck on
        // Left.
        var tracker = new PanelTabTracker();
        tracker.RecordOwnLeftPanelToggle(); // open -> belief Left
        tracker.RecordOwnLeftPanelToggle(); // close -> belief null

        tracker.RecordOwnFocusedTabAdvance();

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // unchanged - no panel believed open
    }

    [Fact]
    public void RevokeOwnLeftPanelToggle_UndoesTheOptimisticFocusFlip_SoFocusedAdvanceIsANoOp()
    {
        var tracker = new PanelTabTracker();
        var creditId = tracker.RecordOwnLeftPanelToggle(); // belief -> Left (optimistic)

        tracker.RevokeOwnLeftPanelToggle(creditId); // the press was refused - never happened

        tracker.RecordOwnFocusedTabAdvance();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // unchanged - belief reverted to null
    }

    [Fact]
    public void RevokeOwnRightPanelToggle_UndoesTheOptimisticFocusFlip()
    {
        var tracker = new PanelTabTracker();
        var creditId = tracker.RecordOwnRightPanelToggle(); // belief -> Right (optimistic)

        tracker.RevokeOwnRightPanelToggle(creditId);

        tracker.RecordOwnFocusedTabAdvance();
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab); // unchanged - belief reverted to null
    }

    [Fact]
    public void RecordObservedFocus_OverwritesTheBelief_RegardlessOfPriorToggles()
    {
        // The ground-truth setter is authoritative, not a flip - it must
        // work even when no own-toggle was ever recorded (the commander
        // opened a panel by hand).
        var tracker = new PanelTabTracker();

        tracker.RecordObservedFocus(PanelSide.Right);
        tracker.RecordOwnFocusedTabAdvance();
        Assert.Equal(RightPanelTab.Modules, tracker.RightCurrentTab);

        tracker.RecordObservedFocus(PanelSide.Left);
        tracker.RecordOwnFocusedTabAdvance();
        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);

        tracker.RecordObservedFocus(null);
        tracker.RecordOwnFocusedTabAdvance(); // no-op now
        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab); // unchanged
        Assert.Equal(RightPanelTab.Modules, tracker.RightCurrentTab); // unchanged
    }

    [Fact]
    public void RecordObservedFocus_CorrectsAWrongOptimisticBelief_ThePressAttributedToTheWrongPanelCase()
    {
        // The exact hazard the brief named: an optimistic Left belief left
        // over from an earlier toggle must not silently steal a
        // CycleNextPanel press that Elite actually applied to the RIGHT
        // panel because the commander's own hand switched focus there
        // without going through LunaPanel's own toggle at all.
        var tracker = new PanelTabTracker();
        tracker.RecordOwnLeftPanelToggle(); // stale belief: Left

        tracker.RecordObservedFocus(PanelSide.Right); // ground truth catches up

        tracker.RecordOwnFocusedTabAdvance();
        Assert.Equal(RightPanelTab.Modules, tracker.RightCurrentTab);
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // NOT corrupted by the stale belief
    }

    // -----------------------------------------------------------------
    // RecordDestinationChange (2026-09-08, LC20) - the third re-sync, and
    // the first one available to a commander who is sitting docked. Every
    // test here also pins the Low-confidence ceiling, because that ceiling
    // is the whole safety property: LC20 did NOT establish that Destination
    // can only change from a Navigation selection (a route auto-advancing
    // on arrival is unmeasured), so this observation may be wrong and must
    // never claim otherwise. See ref/docs/panel-tab-tracking.md,
    // "Measured 2026-09-08".
    // -----------------------------------------------------------------

    /// <summary>
    /// Moves a fresh tracker's LEFT belief to <see cref="PanelTab.Contacts"/>
    /// at <see cref="TabConfidence.High"/> - where <c>request-docking</c>
    /// leaves it, and the stale belief that cost the commander a docking
    /// request on 2026-09-09.
    /// </summary>
    private static PanelTabTracker TrackerBelievingContactsAtHighConfidence()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordOwnTabAdvance(); // Navigation -> Transactions
        tracker.RecordOwnTabAdvance(); // Transactions -> Contacts
        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
        return tracker;
    }

    [Fact]
    public void RecordDestinationChange_WithTheLeftPanelObservedFocused_SnapsTheTabToNavigation()
    {
        var tracker = TrackerBelievingContactsAtHighConfidence();

        tracker.RecordDestinationChange(PanelSide.Left);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void RecordDestinationChange_SnapsAtLowConfidence_NeverHigh_EvenFromAHighConfidenceBelief()
    {
        // The ruling this whole observation was built under (coordinator,
        // 2026-09-09): set the tab, but never claim certainty. Promoting
        // this to High needs the measurement LC20 names as missing - whether
        // Destination can change with the panel open while the commander is
        // NOT on Navigation.
        var tracker = TrackerBelievingContactsAtHighConfidence();

        tracker.RecordDestinationChange(PanelSide.Left);

        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordDestinationChange_WithNoPanelObservedFocused_ChangesNothing()
    {
        // The panel is closed: LC20's finding is gated on
        // GuiFocus == ExternalPanel and says nothing at all about a
        // Destination change with no panel open (a route advancing in
        // flight, a selection from the galaxy map - which has its own
        // GuiFocus value and is excluded structurally by this same gate).
        var tracker = TrackerBelievingContactsAtHighConfidence();

        tracker.RecordDestinationChange(null);

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordDestinationChange_WithTheRightPanelObservedFocused_ChangesNothing()
    {
        var tracker = TrackerBelievingContactsAtHighConfidence();

        tracker.RecordDestinationChange(PanelSide.Right);

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordDestinationChange_NeverTouchesTheRightPanelsOwnTab()
    {
        // The right panel has no measured relationship to Destination at
        // all - same discipline as RecordFsdJump/RecordOnFootRoundTrip,
        // which deliberately leave it alone (see this file's own
        // "What is NOT reset" reasoning in PanelTabTracker's remarks).
        var tracker = TrackerBelievingContactsAtHighConfidence();
        tracker.RecordOwnRightTabAdvance(); // Home -> Modules

        tracker.RecordDestinationChange(PanelSide.Left);

        Assert.Equal(RightPanelTab.Modules, tracker.RightCurrentTab);
        Assert.Equal(TabConfidence.High, tracker.RightTabConfidence);
    }

    [Fact]
    public void RecordDestinationChange_WhenAlreadyBelievedNavigationAtHighConfidence_LeavesThatConfidenceAlone()
    {
        // Evidence that AGREES with the belief must not degrade it. A
        // commander who jumps (RecordFsdJump - a measured re-sync, High)
        // and then picks a destination on the Navigation tab would
        // otherwise be punished with a Low-confidence belief and a
        // gotoLeftPanelTab WARN for doing exactly the thing the tracker
        // just confirmed.
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        tracker.RecordDestinationChange(PanelSide.Left);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordDestinationChange_WhenAlreadyBelievedNavigationAtLowConfidence_StaysLow_NeverPromoted()
    {
        // The ceiling from the other side: this observation can lower
        // confidence, and can never raise it.
        var tracker = new PanelTabTracker();
        tracker.RecordLeftPanelEdge(); // uncredited - Navigation, now Low
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);

        tracker.RecordDestinationChange(PanelSide.Left);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public void RecordDestinationChange_DoesNotConsultTheClassesOwnFocusBelief_OnlyTheObservedFocusPassedIn()
    {
        // Deliberate: RecordOwnLeftPanelToggle flips the internal belief to
        // Left OPTIMISTICALLY, before the keystroke is even injected, so
        // reading that belief here would let a press that never landed
        // authorise a snap. The caller passes the observed GuiFocus from the
        // very snapshot the Destination change was read out of, which is
        // also what excludes the galaxy map structurally (LC20).
        var tracker = TrackerBelievingContactsAtHighConfidence();
        tracker.RecordOwnLeftPanelToggle(); // internal belief -> Left, optimistically

        tracker.RecordDestinationChange(null); // but the snapshot says no panel is focused

        Assert.Equal(PanelTab.Contacts, tracker.CurrentTab);
        Assert.Equal(TabConfidence.High, tracker.LeftTabConfidence);
    }
}
