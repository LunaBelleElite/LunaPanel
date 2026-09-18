namespace LunaPanel.Core.GameState;

/// <summary>
/// Tracks which tab each of Elite's two side panels is currently on - the
/// left panel (<see cref="PanelTab"/>) since 2026-09-07, and the right panel
/// (<see cref="RightPanelTab"/>) since Fix 7 below, both per
/// <c>ref/docs/panel-tab-tracking.md</c>, which read in full explains why
/// every mechanism here exists and what it replaced ("park-and-step",
/// killed by the tabs turning out to wrap).
///
/// Nothing Elite writes names the active tab of either panel (LC3) - not
/// <c>Status.json</c>, not the journal. So this class carries no I/O of its
/// own and answers a narrower question than "what tab is it on": <b>a
/// believed index is always present (see "Reversed 2026-09-08" below), and
/// <see cref="TabConfidence"/> says how much that belief should be
/// trusted</b>. Before 2026-09-08 this class instead reported
/// <see langword="null"/> once it lost track and every caller refused to act
/// on that; the user reversed that design explicitly (see below) because a
/// macro must never be blocked by uncertainty about the panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reversed 2026-09-08: belief always exists, and every caller acts on
/// it.</b> The user, verbatim: <i>"We should never NOT be allowed to use a
/// macro."</i> And: <i>"We track as best we can, but if the commander messes
/// it up, that's their fault. We still fire off macros even during an
/// unknown state. We do our best to FIND a state when it becomes unknown so
/// we can decide what buttons to press after that... But if we fire off the
/// macro because we thought we were in a wrong spot, that's the commander's
/// fault."</i>
///
/// Before this change, an uncredited <c>GuiFocus</c> edge (see "Detect
/// staleness" in <c>ref/docs/panel-tab-tracking.md</c>) cleared the tracked
/// index to <see langword="null"/>, and every caller (<c>RequireStep</c>'s
/// <c>LeftPanelTabKnown</c> precondition, <c>GotoLeftPanelTabStep</c>'s own
/// defense-in-depth check) refused to act rather than guess - both gates are
/// now removed entirely (<c>ref/docs/macros.md</c>, <c>MacroRunner</c>).
/// The commander's own framing: "if somehow we were in contacts and they
/// open up left panel" LunaPanel should still believe Contacts until
/// something says otherwise - an uncredited edge is evidence the belief
/// might be wrong, not evidence it no longer exists. So an uncredited edge
/// now sets <see cref="TabConfidence.Low"/> and leaves the tracked index
/// untouched, rather than nulling it - see <see cref="SidePanel.RecordEdge"/>.
/// <see cref="CurrentTab"/>/<see cref="RightCurrentTab"/> are therefore
/// non-nullable, and <see cref="PressesToReach"/>/<see cref="PressesToReachRight"/>
/// always return a real press count, never <see langword="null"/> - a macro
/// always has a route to compute, however much it should be trusted.
///
/// The old wrapping argument against "winding" back to a known position from
/// unknown - the docked screen's clamped list can park itself this way, the
/// side panels' wrapping tabs cannot - is now moot rather than wrong: there
/// is no more "unknown start" to wind from, because the belief never goes
/// away. What used to be the refusal case is now simply the low-confidence
/// case, and the caller (<c>MacroRunner</c>'s <c>gotoLeftPanelTab</c> step)
/// proceeds anyway, logging a <c>WARN</c> naming the believed tab and why
/// confidence is low - the risk is the user's, accepted explicitly, but a
/// bad outcome must still be explainable afterward rather than mysterious.
/// See that step's own remarks for the log line, and
/// <c>tests/notes/live-checks.md</c> LC18 for why a wrong belief here has a
/// worse failure mode than a no-op: a second <c>UI_Select</c> on the
/// contacts row cancels a docking clearance just won.
/// </para>
/// <para><b>The mechanism, once per side.</b> Both panels share the exact
/// same shape - track our own presses, wrap on advance/retreat, arm a
/// toggle credit before opening/closing so the confirming edge is not
/// mistaken for the commander, and drop confidence to
/// <see cref="TabConfidence.Low"/> the moment an uncredited edge arrives.
/// The left panel's methods (<see cref="RecordOwnTabAdvance"/>,
/// <see cref="RecordOwnLeftPanelToggle"/>, <see cref="RecordLeftPanelEdge"/>,
/// ...) and the right panel's mirrors (<see cref="RecordOwnRightTabAdvance"/>,
/// <see cref="RecordOwnRightPanelToggle"/>, <see cref="RecordRightPanelEdge"/>,
/// ...) both delegate to the same private <see cref="SidePanel"/>
/// implementation, parameterised only by tab count - see that nested type's
/// remarks. This is deliberate: two hand-written copies of the
/// credit/TTL/wrap/confidence logic would drift, and drift here means a
/// confidently wrong belief with no warning attached, which is the exact
/// failure the logging requirement exists to prevent.
/// </para>
/// <para>
/// <b>The two panels are NOT symmetric in what resets them.</b> The left
/// panel has three measured free re-syncs (<see cref="RecordFsdJump"/>,
/// <see cref="RecordOnFootRoundTrip"/>, <see cref="RecordLoadGame"/>) -
/// LC4, LC15, and Fix 4 below. <b>Nobody has measured whether any of these
/// journal events also reset the right panel</b>, so none of them touch
/// <see cref="RightCurrentTab"/> at all. Extending them without a
/// measurement would be exactly the kind of confident guess this class
/// exists to warn about rather than hide - see "Fix 7", "What is NOT reset,
/// and why", below.
/// </para>
/// <para>
/// <b>Which panel is focused, and why that can't just be read live.</b>
/// <see cref="AdvanceTabAction"/>/<see cref="RetreatTabAction"/>
/// (<c>CycleNextPanel</c>/<c>CyclePreviousPanel</c>) are a single pair of
/// Frontier actions shared by both panels - which one a press actually
/// moves depends entirely on which panel currently has focus in the real
/// game. The obvious source for that is <c>Status.json</c>'s live
/// <c>GuiFocus</c> - but that value is confirmed asynchronously (the same
/// polling lag <see cref="RecordOwnLeftPanelToggle"/>'s whole credit system
/// exists to work around), so reading it live at the exact moment a
/// same-run <c>CycleNextPanel</c> follows a <c>FocusLeftPanel</c> press
/// would frequently see the panel as not-yet-open and silently drop the
/// advance. Instead, this class tracks its OWN belief of which panel is
/// focused (<see cref="RecordOwnLeftPanelToggle"/>/
/// <see cref="RecordOwnRightPanelToggle"/> flip it the instant they are
/// called, before any keystroke goes out - the same "trust our own action
/// immediately" pattern mechanism 1 already uses for the tab itself), and
/// <see cref="RecordObservedFocus"/> lets a caller with the real,
/// eventually-confirmed <c>GuiFocus</c> correct that belief whenever it
/// changes - including a focus change the commander causes by hand, which
/// the optimistic side alone could never see. <see cref="RecordOwnFocusedTabAdvance"/>/
/// <see cref="RecordOwnFocusedTabRetreat"/> (used by
/// <c>PanelTabPressEffect</c>) read this belief, not <c>Status.json</c>,
/// and apply to neither panel while it is <see langword="null"/> - "if
/// neither is open, it does nothing," per the brief that asked for this.
/// This focus belief is a separate concept from <see cref="TabConfidence"/>
/// - one says which panel a shared press affects, the other says how much
/// to trust a panel's own tracked index.
/// </para>
/// <para>
/// <b>Fix 7, 2026-09-07: a presumed starting position, and the right
/// panel.</b> Before this fix, <see cref="CurrentTab"/> started
/// <see langword="null"/> and stayed that way until an <c>FSDJump</c>,
/// on-foot round trip, or live <c>LoadGame</c> happened to occur - so
/// <c>request-docking</c> refused on the very first attempt after every
/// server restart, which the commander hit repeatedly while the server was
/// being restarted for an unrelated reason. The commander confirmed
/// directly: the left panel is reliably on NAVIGATION at session start, and
/// a LunaPanel server start almost always coincides with, or shortly
/// precedes, a game session actually beginning - so <see cref="CurrentTab"/>
/// now starts at <see cref="PanelTab.Navigation"/> and
/// <see cref="RightCurrentTab"/> starts at <see cref="RightPanelTab.Home"/>,
/// both a presumption, not a measurement, both at
/// <see cref="TabConfidence.High"/> initially. <b>Superseded in effect,
/// though not in spirit, by the 2026-09-08 reversal above</b>: an uncredited
/// edge now only ever lowers confidence rather than erasing the presumption
/// entirely, so the presumption survives (at reduced trust) even past the
/// point where it used to be discarded outright. The existing
/// <see cref="RecordFsdJump"/>/<see cref="RecordOnFootRoundTrip"/>/
/// <see cref="RecordLoadGame"/> reset points are UNCHANGED by either fix -
/// they still correct the model mid-session AND restore
/// <see cref="TabConfidence.High"/>, since each is a genuine, measured
/// re-sync, not a guess.
/// <para>
/// <b>What is NOT reset, and why.</b> The right panel gained the SAME
/// mechanism as the left (track, arm, detect staleness, confidence) and the
/// SAME starting presumption, but NONE of the left panel's three reset
/// points - <see cref="RecordFsdJump"/>, <see cref="RecordOnFootRoundTrip"/>,
/// and <see cref="RecordLoadGame"/> only ever touch the left panel's tab.
/// LC15 measured that an on-foot round trip resets the LEFT panel to
/// NAVIGATION; nobody has measured whether it (or an <c>FSDJump</c>, or a
/// live <c>LoadGame</c>) also resets the RIGHT panel to HOME, and assuming
/// it does without a measurement would be exactly the kind of confident
/// guess this whole class exists to be honest about rather than hide. If a
/// future LC entry measures this, extend the three reset methods below to
/// also call <c>_right.ResetTo</c> at that point - deliberately not done
/// here.
/// </para>
/// </para>
/// </remarks>
public sealed class PanelTabTracker
{
    /// <summary>
    /// The Frontier action name a macro presses to open or close the left
    /// panel. <see cref="LunaPanel.Core.Macros.MacroRunner"/> compares a
    /// <c>press</c> step's action against this constant to know when to call
    /// <see cref="RecordOwnLeftPanelToggle"/> - kept here, next to the
    /// tracker it arms, rather than as a bare string at the call site.
    /// </summary>
    public const string LeftPanelToggleAction = "FocusLeftPanel";

    /// <summary>
    /// The Frontier action name a macro presses to open or close the right
    /// panel - the exact mirror of <see cref="LeftPanelToggleAction"/>,
    /// added by Fix 7. No shipped macro uses it yet.
    /// </summary>
    public const string RightPanelToggleAction = "FocusRightPanel";

    /// <summary>
    /// The Frontier action name a macro presses to advance whichever side
    /// panel currently has focus by one tab. Shared by both panels - see
    /// this type's remarks, "Which panel is focused, and why that can't just
    /// be read live".
    /// </summary>
    public const string AdvanceTabAction = "CycleNextPanel";

    /// <summary>
    /// The Frontier action name that retreats whichever side panel currently
    /// has focus by one tab - the exact mirror of <see cref="AdvanceTabAction"/>.
    /// Added Fix 6 (2026-09-07) alongside <see cref="RecordOwnTabRetreat"/>.
    /// </summary>
    public const string RetreatTabAction = "CyclePreviousPanel";

    /// <summary>
    /// How long an own-toggle credit (either panel) is honored before an
    /// edge treats it as though its confirmation is never coming and drops
    /// it - see <see cref="SidePanel"/>'s remarks, "Bounding drift". Set to
    /// ten times <c>StatusFileWatcher.SafetyPollInterval</c> (1s, the
    /// worst-case delay between a real <c>GuiFocus</c> change and LunaPanel
    /// observing it): comfortably longer than any genuine edge should ever
    /// take to arrive, so this bound only ever fires for a credit whose
    /// press truly never produced one, never for a real one that was merely
    /// slow.
    /// </summary>
    internal static readonly TimeSpan OwnToggleArmTtl = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly SidePanel _left = new((int)PanelTab.Navigation, tabCount: 4);
    private readonly SidePanel _right = new((int)RightPanelTab.Home, tabCount: 8);

    // The tracker's own belief of which panel is currently focused - see
    // this type's remarks, "Which panel is focused". Starts null: at
    // construction time nothing has told this class either panel is open
    // yet (the Fix 7 presumption is about each panel's TAB, not about either
    // panel being open right now).
    private PanelSide? _focusedPanel;

    /// <param name="clock">
    /// Source of time for expiring stale own-toggle credits (see
    /// <see cref="OwnToggleArmTtl"/>). Defaults to
    /// <see cref="TimeProvider.System"/> so every existing caller and test
    /// that never cared about this can keep using the parameterless form.
    /// </param>
    public PanelTabTracker(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// The left panel's tracked tab - always present (see this type's
    /// remarks, "Reversed 2026-09-08"). Starts at
    /// <see cref="PanelTab.Navigation"/> - a presumption, not a measurement;
    /// see this type's remarks, "Fix 7". Check <see cref="LeftTabConfidence"/>
    /// alongside this to know how much to trust it.
    /// </summary>
    public PanelTab CurrentTab
    {
        get { lock (_gate) { return (PanelTab)_left.Current; } }
    }

    /// <summary>
    /// The right panel's tracked tab - always present, same as
    /// <see cref="CurrentTab"/>. Starts at <see cref="RightPanelTab.Home"/> -
    /// the same presumption as <see cref="CurrentTab"/>, for the same
    /// reason; see this type's remarks, "Fix 7". Added by Fix 7 - no macro
    /// reads this yet. Check <see cref="RightTabConfidence"/> alongside this.
    /// </summary>
    public RightPanelTab RightCurrentTab
    {
        get { lock (_gate) { return (RightPanelTab)_right.Current; } }
    }

    /// <summary>
    /// How much to trust <see cref="CurrentTab"/> right now. See
    /// <see cref="TabConfidence"/> and this type's remarks, "Reversed
    /// 2026-09-08" - a caller acts on <see cref="CurrentTab"/> regardless of
    /// this value; it exists so a low-confidence action can be logged rather
    /// than silently indistinguishable from a confident one.
    /// </summary>
    public TabConfidence LeftTabConfidence
    {
        get { lock (_gate) { return _left.Confidence; } }
    }

    /// <summary>The exact mirror of <see cref="LeftTabConfidence"/> for <see cref="RightCurrentTab"/>.</summary>
    public TabConfidence RightTabConfidence
    {
        get { lock (_gate) { return _right.Confidence; } }
    }

    /// <summary>Resets the LEFT panel to <see cref="PanelTab.Navigation"/> at <see cref="TabConfidence.High"/> - the free re-sync <c>FSDJump</c> gives (LC4). Does not touch <see cref="RightCurrentTab"/> - see this type's remarks, "What is NOT reset, and why".</summary>
    public void RecordFsdJump()
    {
        lock (_gate) { _left.ResetTo((int)PanelTab.Navigation); }
    }

    /// <summary>Resets the LEFT panel to <see cref="PanelTab.Navigation"/> at <see cref="TabConfidence.High"/> - the free re-sync an on-foot round trip gives (LC15). Does not touch <see cref="RightCurrentTab"/> - see this type's remarks, "What is NOT reset, and why".</summary>
    public void RecordOnFootRoundTrip()
    {
        lock (_gate) { _left.ResetTo((int)PanelTab.Navigation); }
    }

    /// <summary>
    /// Resets the LEFT panel to <see cref="PanelTab.Navigation"/> at
    /// <see cref="TabConfidence.High"/> - the session-start seed (Fix 4,
    /// 2026-09-07). <b>Call this only for a LIVE <c>LoadGame</c>, never one
    /// found by the startup back-scan</b> - see
    /// <c>JournalStateStore.Changed</c>'s remarks for where that distinction
    /// is made. Does not touch <see cref="RightCurrentTab"/> - see this
    /// type's remarks, "What is NOT reset, and why". Largely superseded by
    /// Fix 7's constructor presumption for the specific "refuses on first
    /// attempt" symptom, but kept: it still re-affirms NAVIGATION (and
    /// restores High confidence) for a real game restart happening well into
    /// an already-running server's lifetime, when the tab may genuinely have
    /// drifted since Fix 7's presumption was made.
    /// </summary>
    public void RecordLoadGame()
    {
        lock (_gate) { _left.ResetTo((int)PanelTab.Navigation); }
    }

    /// <summary>
    /// Advances the LEFT panel's tracked tab by one position, wrapping
    /// <see cref="PanelTab.Contacts"/> back to <see cref="PanelTab.Galaxy"/>.
    /// Does not change <see cref="LeftTabConfidence"/> - advancing our own
    /// belief is not a confirmation of it. Called directly by
    /// <c>GotoLeftPanelTabStep</c>, which is unambiguously about the left
    /// panel by name - unaffected by <see cref="_focusedPanel"/>.
    /// </summary>
    public void RecordOwnTabAdvance()
    {
        lock (_gate) { _left.Advance(); }
    }

    /// <summary>
    /// Retreats the LEFT panel's tracked tab by one position, wrapping
    /// <see cref="PanelTab.Galaxy"/> back to <see cref="PanelTab.Contacts"/> -
    /// the exact mirror of <see cref="RecordOwnTabAdvance"/>.
    /// </summary>
    public void RecordOwnTabRetreat()
    {
        lock (_gate) { _left.Retreat(); }
    }

    /// <summary>
    /// Advances the RIGHT panel's tracked tab by one position, wrapping
    /// <see cref="RightPanelTab.Playlist"/> back to
    /// <see cref="RightPanelTab.Home"/>. Added by Fix 7, for symmetry and
    /// direct testability of the wrap arithmetic - no macro calls this
    /// directly yet (routing of the shared <see cref="AdvanceTabAction"/>
    /// press to whichever panel is focused goes through
    /// <see cref="RecordOwnFocusedTabAdvance"/> instead).
    /// </summary>
    public void RecordOwnRightTabAdvance()
    {
        lock (_gate) { _right.Advance(); }
    }

    /// <summary>Retreats the RIGHT panel's tracked tab by one position - the exact mirror of <see cref="RecordOwnRightTabAdvance"/>. Added by Fix 7.</summary>
    public void RecordOwnRightTabRetreat()
    {
        lock (_gate) { _right.Retreat(); }
    }

    /// <summary>
    /// Banks one "own toggle" credit for <see cref="RecordLeftPanelEdge"/> to
    /// consume, and optimistically flips this class's belief of which panel
    /// is focused to <see cref="PanelSide.Left"/> (or back to
    /// <see langword="null"/> if it already believed Left, modelling the
    /// press as a close) - see this type's remarks, "Which panel is
    /// focused". Call this immediately before injecting EACH
    /// <c>FocusLeftPanel</c> press itself (once per press, not once per
    /// macro - <c>request-docking</c> calls this twice, at its open and its
    /// close).
    /// </summary>
    /// <returns>
    /// An opaque id identifying this specific credit, for
    /// <see cref="RevokeOwnLeftPanelToggle"/> to take back if the press this
    /// credit was armed for turns out never to have reached Elite. Callers
    /// with no way to observe a refusal are free to simply discard it.
    /// </returns>
    public long RecordOwnLeftPanelToggle()
    {
        lock (_gate)
        {
            var id = _left.RecordOwnToggle(_clock.GetUtcNow());
            _focusedPanel = _focusedPanel == PanelSide.Left ? null : PanelSide.Left;
            return id;
        }
    }

    /// <summary>
    /// The exact mirror of <see cref="RecordOwnLeftPanelToggle"/> for the
    /// right panel, added by Fix 7: banks a credit against
    /// <see cref="RecordRightPanelEdge"/> and flips the focus belief toward
    /// <see cref="PanelSide.Right"/>.
    /// </summary>
    public long RecordOwnRightPanelToggle()
    {
        lock (_gate)
        {
            var id = _right.RecordOwnToggle(_clock.GetUtcNow());
            _focusedPanel = _focusedPanel == PanelSide.Right ? null : PanelSide.Right;
            return id;
        }
    }

    /// <summary>
    /// Takes back a credit <see cref="RecordOwnLeftPanelToggle"/> banked,
    /// because the press it was armed for turned out to have been refused
    /// and therefore never reached Elite - no confirming <c>GuiFocus</c>
    /// edge is ever coming for it. Also undoes that call's optimistic focus
    /// flip, for the same reason: the press never happened, so the belief it
    /// produced was never true. A safe no-op (touching neither the credit
    /// nor the focus belief) if <paramref name="creditId"/> is not a
    /// currently-pending credit.
    /// </summary>
    public void RevokeOwnLeftPanelToggle(long creditId)
    {
        lock (_gate)
        {
            if (_left.RevokeOwnToggle(creditId))
            {
                _focusedPanel = _focusedPanel == PanelSide.Left ? null : PanelSide.Left;
            }
        }
    }

    /// <summary>The exact mirror of <see cref="RevokeOwnLeftPanelToggle"/> for the right panel, added by Fix 7.</summary>
    public void RevokeOwnRightPanelToggle(long creditId)
    {
        lock (_gate)
        {
            if (_right.RevokeOwnToggle(creditId))
            {
                _focusedPanel = _focusedPanel == PanelSide.Right ? null : PanelSide.Right;
            }
        }
    }

    /// <summary>
    /// Records that the left panel's <c>GuiFocus</c> state just flipped
    /// open&#8596;closed. Discards any banked credits older than
    /// <see cref="OwnToggleArmTtl"/> first, then consumes one remaining
    /// credit without effect if any is left; otherwise drops
    /// <see cref="LeftTabConfidence"/> to <see cref="TabConfidence.Low"/> -
    /// leaving <see cref="CurrentTab"/> exactly where it was - because an
    /// edge LunaPanel did not cause (and cannot charge to a still-live
    /// credit) means the commander may be in the panel and the previously
    /// tracked position - including the Fix 7 presumption - is now a belief
    /// rather than a confirmed fact. See this type's remarks, "Reversed
    /// 2026-09-08": before that change this cleared <see cref="CurrentTab"/>
    /// to <see langword="null"/> instead. Does not touch this class's focus
    /// belief; see <see cref="RecordObservedFocus"/> for that.
    /// </summary>
    public void RecordLeftPanelEdge()
    {
        lock (_gate) { _left.RecordEdge(_clock.GetUtcNow(), OwnToggleArmTtl); }
    }

    /// <summary>The exact mirror of <see cref="RecordLeftPanelEdge"/> for the right panel, added by Fix 7.</summary>
    public void RecordRightPanelEdge()
    {
        lock (_gate) { _right.RecordEdge(_clock.GetUtcNow(), OwnToggleArmTtl); }
    }

    /// <summary>
    /// Sets this class's ground-truth belief of which panel is currently
    /// focused, from the real (eventually-confirmed) <c>GuiFocus</c> - call
    /// this every time <c>GameStateStore.Changed</c> fires, with
    /// <paramref name="focus"/> computed from the new snapshot's
    /// <c>GuiFocus</c> against <see cref="StatusVocabulary.GuiFocusValues"/>
    /// (<c>ExternalPanel</c> =&gt; <see cref="PanelSide.Left"/>,
    /// <c>InternalPanel</c> =&gt; <see cref="PanelSide.Right"/>, anything
    /// else =&gt; <see langword="null"/>). Unlike the optimistic flip in
    /// <see cref="RecordOwnLeftPanelToggle"/>/<see cref="RecordOwnRightPanelToggle"/>,
    /// this OVERWRITES the belief outright rather than toggling it - it is
    /// the authoritative value, not an inference, which is what keeps it
    /// correct even when the commander switches focus by hand (see this
    /// type's remarks). Safe to call on every snapshot change, not only an
    /// edge: idempotent, and cheap under the same lock every other method
    /// here already takes.
    /// </summary>
    public void RecordObservedFocus(PanelSide? focus)
    {
        lock (_gate) { _focusedPanel = focus; }
    }

    /// <summary>
    /// Records that <c>Status.json</c>'s <c>Destination</c> just changed
    /// value, with <paramref name="observedFocus"/> being which panel that
    /// same snapshot's <c>GuiFocus</c> reported as focused. When (and only
    /// when) that is <see cref="PanelSide.Left"/>, the LEFT panel's believed
    /// tab is set to <see cref="PanelTab.Navigation"/> at
    /// <see cref="TabConfidence.Low"/> - <b>never</b>
    /// <see cref="TabConfidence.High"/>. Anything else is a no-op. Does not
    /// touch the right panel, which has no measured relationship to
    /// <c>Destination</c> at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What was measured (LC20, 2026-09-08).</b> With the left panel open,
    /// selecting an entry on the NAVIGATION tab writes <c>Destination</c>;
    /// the TRANSACTIONS tab writes nothing at all, and the journal stays
    /// silent throughout - this signal exists only in <c>Status.json</c>. So
    /// a <c>Destination</c> change seen while the left panel is open is
    /// evidence the commander is on NAVIGATION. It is the <b>first re-sync
    /// available to a commander sitting docked</b>: the two older ones
    /// (<see cref="RecordFsdJump"/>, <see cref="RecordOnFootRoundTrip"/>)
    /// both require travel, which is why a docked commander holding a stale
    /// belief previously had no way back. Concretely: on 2026-09-09 a
    /// <c>request-docking</c> run acted on a stale "Contacts" belief while
    /// the commander had moved to Navigation, pressed into the wrong tab,
    /// and never made the request.
    /// </para>
    /// <para>
    /// <b>Why the ceiling is deliberately <see cref="TabConfidence.Low"/>,
    /// and what it would take to lift it.</b> LC20 explicitly does <i>not</i>
    /// establish whether <c>Destination</c> can change with the panel open
    /// while the commander is NOT on Navigation - a route auto-advancing on
    /// arrival is the obvious candidate and nobody has measured it. That case
    /// would produce a confidently WRONG belief, which is worse than an
    /// unsure one and would defeat the point of the confidence model. So this
    /// snaps the position and flags it, which is strictly better than the
    /// status quo either way: if the commander really was on Navigation the
    /// belief is repaired where it was stale, and if a route auto-advanced
    /// instead the belief may be wrong but <c>gotoLeftPanelTab</c>'s WARN
    /// still fires and nothing claims certainty it has not earned. Promoting
    /// this to <see cref="TabConfidence.High"/> requires exactly the
    /// measurement LC20 names as missing - the ceiling is a decision, not an
    /// oversight. (Coordinator's ruling, 2026-09-09.)
    /// </para>
    /// <para>
    /// <b>Why the focus is a parameter rather than this class's own belief.</b>
    /// <see cref="RecordOwnLeftPanelToggle"/> flips that belief optimistically,
    /// before the keystroke is even injected, so a press that never reached
    /// Elite could authorise a snap. The caller instead passes the focus
    /// computed from the very snapshot the <c>Destination</c> change was read
    /// out of - which is also what excludes the galaxy map structurally
    /// rather than by luck, since the galaxy map has its own <c>GuiFocus</c>
    /// value (LC20).
    /// </para>
    /// <para>
    /// <b>Confidence is only lowered when the belief actually moves.</b> A
    /// change seen while <see cref="CurrentTab"/> is already
    /// <see cref="PanelTab.Navigation"/> is evidence that AGREES with the
    /// belief, so it leaves <see cref="LeftTabConfidence"/> untouched - a
    /// commander who jumps (a measured, <see cref="TabConfidence.High"/>
    /// re-sync) and then picks a destination must not be punished with a Low
    /// belief for doing the very thing the tracker just confirmed. It is
    /// never raised either way.
    /// </para>
    /// </remarks>
    public void RecordDestinationChange(PanelSide? observedFocus)
    {
        if (observedFocus != PanelSide.Left)
        {
            return;
        }

        lock (_gate) { _left.SnapToAtLowConfidence((int)PanelTab.Navigation); }
    }

    /// <summary>
    /// Applies <see cref="AdvanceTabAction"/> to whichever panel this
    /// class's own focus belief currently names - a no-op if neither panel
    /// is believed focused. Used only by <c>PanelTabPressEffect</c>, which
    /// resolves this for a plain <c>press</c> step naming
    /// <see cref="AdvanceTabAction"/>; <c>GotoLeftPanelTabStep</c> calls
    /// <see cref="RecordOwnTabAdvance"/> directly instead, since it already
    /// knows unambiguously which panel it means.
    /// </summary>
    internal void RecordOwnFocusedTabAdvance()
    {
        lock (_gate)
        {
            if (_focusedPanel == PanelSide.Left) { _left.Advance(); }
            else if (_focusedPanel == PanelSide.Right) { _right.Advance(); }
        }
    }

    /// <summary>The exact mirror of <see cref="RecordOwnFocusedTabAdvance"/> for <see cref="RetreatTabAction"/>.</summary>
    internal void RecordOwnFocusedTabRetreat()
    {
        lock (_gate)
        {
            if (_focusedPanel == PanelSide.Left) { _left.Retreat(); }
            else if (_focusedPanel == PanelSide.Right) { _right.Retreat(); }
        }
    }

    /// <summary>
    /// How many <see cref="AdvanceTabAction"/> presses would reach
    /// <paramref name="target"/> on the LEFT panel from <see cref="CurrentTab"/>.
    /// Always in <c>[0, 3]</c>, and always computable - see this type's
    /// remarks, "Reversed 2026-09-08": a believed index is always present,
    /// so this never refuses. Check <see cref="LeftTabConfidence"/>
    /// separately to know how much to trust the route this returns.
    /// </summary>
    public int PressesToReach(PanelTab target)
    {
        lock (_gate) { return _left.PressesToReach((int)target); }
    }

    /// <summary>
    /// The exact mirror of <see cref="PressesToReach"/> for the RIGHT panel,
    /// added by Fix 7. Always in <c>[0, 7]</c>. No macro calls this yet -
    /// groundwork, per the brief that added it.
    /// </summary>
    public int PressesToReachRight(RightPanelTab target)
    {
        lock (_gate) { return _right.PressesToReach((int)target); }
    }

    /// <summary>
    /// The credit/TTL/wrap/confidence implementation shared by both side
    /// panels, so the left and right panel logic cannot drift apart from
    /// each other - see the class remarks, "The mechanism, once per side".
    /// Every method here assumes the caller already holds
    /// <see cref="PanelTabTracker"/>'s own <c>_gate</c> lock; this type has
    /// no locking of its own, exactly like the fields it replaces used to
    /// sit directly on <see cref="PanelTabTracker"/> under that same lock.
    /// </summary>
    private sealed class SidePanel
    {
        private readonly int _tabCount;
        private int _current;
        private TabConfidence _confidence;

        // A list, not a Queue<T>: RevokeOwnToggle needs to remove one
        // specific credit by id, wherever it sits, not just the front.
        // Consumption and TTL pruning both still work oldest-first by always
        // operating on index 0.
        private readonly List<PendingToggle> _pendingToggles = new();
        private long _nextToggleId;

        private readonly record struct PendingToggle(long Id, DateTimeOffset ArmedAt);

        public SidePanel(int initialTab, int tabCount)
        {
            _current = initialTab;
            _tabCount = tabCount;
            _confidence = TabConfidence.High;
        }

        public int Current => _current;

        public TabConfidence Confidence => _confidence;

        public void ResetTo(int tab)
        {
            _current = tab;
            _confidence = TabConfidence.High;
        }

        // Unlike ResetTo, this is NOT a measured re-sync: it moves the
        // believed position on evidence that is merely suggestive, so it can
        // only ever lower confidence, never restore it. And it leaves
        // confidence entirely alone when the belief was already there,
        // because agreeing evidence is not a reason to trust a position less
        // than before. See PanelTabTracker.RecordDestinationChange, the only
        // caller, for the measurement and the ruling behind both halves.
        public void SnapToAtLowConfidence(int tab)
        {
            if (_current == tab)
            {
                return;
            }

            _current = tab;
            _confidence = TabConfidence.Low;
        }

        public void Advance()
        {
            _current = (_current + 1) % _tabCount;
        }

        public void Retreat()
        {
            _current = (_current + _tabCount - 1) % _tabCount;
        }

        public long RecordOwnToggle(DateTimeOffset now)
        {
            var id = ++_nextToggleId;
            _pendingToggles.Add(new PendingToggle(id, now));
            return id;
        }

        public bool RevokeOwnToggle(long creditId)
        {
            var index = _pendingToggles.FindIndex(p => p.Id == creditId);
            if (index >= 0)
            {
                _pendingToggles.RemoveAt(index);
                return true;
            }

            return false;
        }

        // Bounding drift: a credit whose edge never arrives at all (game not
        // focused when the key went down, the keystroke dropped) would, left
        // unbounded, sit as a permanent +1 - silently consumed by some later
        // edge the commander actually caused by hand, making the tracker
        // confidently report a wrong tab with no warning attached. So every
        // credit expires after `ttl` if never consumed - generous enough
        // that a genuine own edge is never mistaken for a dropped one.
        //
        // Reversed 2026-09-08 (PanelTabTracker's own remarks): an uncredited
        // edge used to null `_current` outright. It now only lowers
        // `_confidence` - the tracked index is left exactly where it was,
        // because the commander's own most likely position IS wherever
        // LunaPanel last believed the panel to be, and a macro must be free
        // to act on that belief regardless.
        public void RecordEdge(DateTimeOffset now, TimeSpan ttl)
        {
            while (_pendingToggles.Count > 0 && now - _pendingToggles[0].ArmedAt > ttl)
            {
                _pendingToggles.RemoveAt(0);
            }

            if (_pendingToggles.Count > 0)
            {
                _pendingToggles.RemoveAt(0);
                return;
            }

            _confidence = TabConfidence.Low;
        }

        public int PressesToReach(int target) => (target - _current + _tabCount) % _tabCount;
    }
}
