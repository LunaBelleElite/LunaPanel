using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins <see cref="PanelTabPressEffect"/> - the one place that decides what
/// pressing a given Frontier action does to a <see cref="PanelTabTracker"/>,
/// shared by <c>MacroRunner</c>'s plain <c>press</c> step and the server's
/// single-tap <c>PlainPresser</c> so the two can never drift
/// (<c>ref/docs/panel-tab-tracking.md</c>'s "Fix 6"). No I/O anywhere in this
/// file, same discipline as <see cref="PanelTabTrackerTests"/>.
/// </summary>
public class PanelTabPressEffectTests
{
    // -----------------------------------------------------------------
    // FocusLeftPanel - arms a toggle credit before the press is known to
    // have succeeded, revokes it if the press turns out to have been
    // refused.
    // -----------------------------------------------------------------

    [Fact]
    public void FocusLeftPanel_Arm_BanksACreditImmediately_BeforeResolveIsEverCalled()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        PanelTabPressEffect.Arm(tracker, PanelTabTracker.LeftPanelToggleAction);

        // The credit exists the instant Arm returns, not only after Resolve
        // - this is the whole point of arming before the keystroke goes out.
        tracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void FocusLeftPanel_Resolve_Sent_LeavesTheCreditBanked()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.LeftPanelToggleAction);
        pending.Resolve(tracker, sent: true);

        tracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void FocusLeftPanel_Resolve_NotSent_RevokesTheCredit_SoTheNextEdgeIsExternal()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.LeftPanelToggleAction);
        pending.Resolve(tracker, sent: false);

        tracker.RecordLeftPanelEdge(); // no credit left - the refused press never reached Elite
        // Reversed 2026-09-08: an uncredited edge no longer nulls the tab -
        // it stays at the last believed value, at Low confidence.
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // FocusRightPanel (Fix 7, 2026-09-07) - the exact mirror of
    // FocusLeftPanel above, unambiguous by action name (no focus-belief
    // routing needed for a toggle, only for the shared Advance/Retreat).
    // -----------------------------------------------------------------

    [Fact]
    public void FocusRightPanel_Arm_BanksACreditImmediately_BeforeResolveIsEverCalled()
    {
        var tracker = new PanelTabTracker();

        PanelTabPressEffect.Arm(tracker, PanelTabTracker.RightPanelToggleAction);

        tracker.RecordRightPanelEdge();
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab);
    }

    [Fact]
    public void FocusRightPanel_Resolve_NotSent_RevokesTheCredit_SoTheNextEdgeIsExternal()
    {
        var tracker = new PanelTabTracker();

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.RightPanelToggleAction);
        pending.Resolve(tracker, sent: false);

        tracker.RecordRightPanelEdge(); // no credit left - the refused press never reached Elite
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.RightTabConfidence);
    }

    [Fact]
    public void FocusRightPanel_DoesNotArmTheLeftPanelsCredit()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        PanelTabPressEffect.Arm(tracker, PanelTabTracker.RightPanelToggleAction);

        tracker.RecordLeftPanelEdge(); // no left credit was armed - still external
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // CycleNextPanel - only applied on a confirmed Sent, never speculatively
    // before, because nothing ever confirms it (LC3) - there is no async
    // edge to race, so there is nothing to gain and everything to lose by
    // applying it before the press is known to have landed.
    //
    // Fix 7, 2026-09-07: CycleNextPanel/CyclePreviousPanel apply to
    // whichever panel PanelTabTracker's own focus belief names - every test
    // below that resolves one of these with sent:true first records an
    // own-left-panel-toggle to put that belief on Left (exactly as a real
    // FocusLeftPanel press earlier in the same macro/press sequence would),
    // so these tests keep pinning the Arm/Resolve mechanics specifically,
    // not the routing itself - PanelTabTrackerTests pins routing directly
    // (RecordOwnFocusedTabAdvance/Retreat, RecordObservedFocus).
    // -----------------------------------------------------------------

    [Fact]
    public void CycleNextPanel_Resolve_Sent_AdvancesTheTrackedTab()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation
        tracker.RecordOwnLeftPanelToggle(); // belief -> Left, as a real FocusLeftPanel press would set

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.AdvanceTabAction);
        pending.Resolve(tracker, sent: true);

        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);
    }

    [Fact]
    public void CycleNextPanel_Resolve_NotSent_LeavesTheTrackedTabUntouched()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.AdvanceTabAction);
        pending.Resolve(tracker, sent: false);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    [Fact]
    public void CycleNextPanel_Arm_DoesNotAdvanceByItself_OnlyResolveDoes()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        PanelTabPressEffect.Arm(tracker, PanelTabTracker.AdvanceTabAction);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // unchanged until Resolve
    }

    // -----------------------------------------------------------------
    // CyclePreviousPanel - the mirror of CycleNextPanel, same discipline.
    // -----------------------------------------------------------------

    [Fact]
    public void CyclePreviousPanel_Resolve_Sent_RetreatsTheTrackedTab()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation
        tracker.RecordOwnLeftPanelToggle(); // belief -> Left

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.RetreatTabAction);
        pending.Resolve(tracker, sent: true);

        Assert.Equal(PanelTab.Galaxy, tracker.CurrentTab);
    }

    [Fact]
    public void CyclePreviousPanel_Resolve_NotSent_LeavesTheTrackedTabUntouched()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.RetreatTabAction);
        pending.Resolve(tracker, sent: false);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    // -----------------------------------------------------------------
    // Any other action - no effect at all, regardless of outcome.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnrelatedAction_ArmAndResolve_NeverTouchesTheTracker(bool sent)
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        var pending = PanelTabPressEffect.Arm(tracker, "LandingGearToggle");
        pending.Resolve(tracker, sent: sent);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        tracker.RecordLeftPanelEdge(); // no credit was ever armed - still external
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Repeat > 1: the shared decision itself takes no repeat count (the
    // caller's own loop calls Arm/Resolve once per individual press) - this
    // proves calling it N times in a row produces exactly N applications,
    // never fewer, which is what "do not assume one" actually requires of
    // this type.
    // -----------------------------------------------------------------

    [Fact]
    public void CalledThreeTimesInARow_AdvancesByThree_NotOne()
    {
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation
        tracker.RecordOwnLeftPanelToggle(); // belief -> Left

        for (var i = 0; i < 3; i++)
        {
            var pending = PanelTabPressEffect.Arm(tracker, PanelTabTracker.AdvanceTabAction);
            pending.Resolve(tracker, sent: true);
        }

        // Navigation -> Transactions -> Contacts -> Galaxy
        Assert.Equal(PanelTab.Galaxy, tracker.CurrentTab);
    }

    // -----------------------------------------------------------------
    // Parity: the same action, driven through two independent trackers,
    // must leave both in the same state - this is the actual claim
    // "MacroRunner and the plain-press path can never drift" makes.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(PanelTabTracker.AdvanceTabAction)]
    [InlineData(PanelTabTracker.RetreatTabAction)]
    public void SameAction_AppliedTwice_LeavesTwoIndependentTrackersInTheSameState(string action)
    {
        var trackerA = new PanelTabTracker();
        trackerA.RecordFsdJump();
        trackerA.RecordOwnLeftPanelToggle(); // belief -> Left
        var trackerB = new PanelTabTracker();
        trackerB.RecordFsdJump();
        trackerB.RecordOwnLeftPanelToggle();

        var pendingA = PanelTabPressEffect.Arm(trackerA, action);
        pendingA.Resolve(trackerA, sent: true);

        var pendingB = PanelTabPressEffect.Arm(trackerB, action);
        pendingB.Resolve(trackerB, sent: true);

        Assert.Equal(trackerA.CurrentTab, trackerB.CurrentTab);
    }
}
