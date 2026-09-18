namespace LunaPanel.Core.GameState;

/// <summary>
/// The one place that decides what pressing a given Frontier action does to
/// a <see cref="PanelTabTracker"/> - shared by
/// <c>LunaPanel.Core.Macros.MacroRunner</c>'s plain <c>press</c> step and the
/// server's single-tap <c>LunaPanel.Server.Input.PlainPresser</c>, so a
/// button pressed once and the same action pressed as a macro step can never
/// drift about what it means for the tracker
/// (<c>ref/docs/panel-tab-tracking.md</c>'s "Fix 6": "Every keystroke
/// LunaPanel injects must feed the tracker, whatever surface asked for it").
///
/// This type takes no <c>repeat</c> count of its own - a caller pressing an
/// action several times (a macro's <c>press</c> step's own <c>Repeat</c>) is
/// expected to call <see cref="Arm"/>/<see cref="PendingPanelTabEffect.Resolve"/>
/// once per individual press, inside its own loop, exactly as it already
/// injects one keystroke per iteration - never once for the whole count.
///
/// <b>Two different orderings, because the three actions need opposite
/// defaults:</b>
/// <list type="bullet">
/// <item><description>
/// <see cref="PanelTabTracker.LeftPanelToggleAction"/> has an asynchronous
/// confirmation to protect - the <c>GuiFocus</c> edge <c>Status.json</c>
/// polling reports later - so its credit must be banked by <see cref="Arm"/>
/// BEFORE the keystroke is injected, win or lose. If the press then turns
/// out to have been refused, <see cref="PendingPanelTabEffect.Resolve"/>
/// revokes that same credit immediately - see
/// <see cref="PanelTabTracker.RevokeOwnLeftPanelToggle"/>'s own remarks for
/// why leaving it to expire on its own is not safe enough.
/// </description></item>
/// <item><description>
/// <see cref="PanelTabTracker.AdvanceTabAction"/>/<see cref="PanelTabTracker.RetreatTabAction"/>
/// have no confirmation at all - nothing Elite writes ever reveals the
/// active tab (LC3) - so there is nothing to win by applying them before the
/// press is known to have landed, and everything to lose: a tracker
/// advanced for a press that was actually refused would be confidently
/// wrong with no future signal able to correct it. <see cref="Arm"/> is
/// therefore a no-op for these two, and
/// <see cref="PendingPanelTabEffect.Resolve"/> only applies the
/// advance/retreat when the caller reports the press as sent.
/// </description></item>
/// </list>
///
/// A caller with no way to observe whether a press actually reached Elite at
/// all (<c>MacroRunner</c>'s <c>IKeyInjector.KeyDown</c>/<c>KeyUp</c> are
/// <see langword="void"/> - see <c>ChordPresser</c>'s own remarks) must
/// resolve with <c>sent: true</c> unconditionally, which reproduces exactly
/// the same blind optimism <c>MacroRunner</c> already had for
/// <c>FocusLeftPanel</c> before this type existed, extended to
/// <c>CycleNextPanel</c>/<c>CyclePreviousPanel</c> too.
///
/// <b>Fix 7, 2026-09-07: <see cref="PanelTabTracker.RightPanelToggleAction"/>
/// joined <see cref="PanelTabTracker.LeftPanelToggleAction"/> as a Toggle
/// action</b> (unambiguous by action name, same as the left one), and
/// <c>Advance</c>/<c>Retreat</c> now apply to whichever panel
/// <see cref="PanelTabTracker"/>'s own focus belief names, via
/// <see cref="PanelTabTracker.RecordOwnFocusedTabAdvance"/>/
/// <see cref="PanelTabTracker.RecordOwnFocusedTabRetreat"/>, rather than
/// always meaning the left panel - see that type's remarks, "Which panel is
/// focused". This type's own signature is unchanged by that: it never needed
/// to know which panel itself, only that the caller told <paramref
/// name="tracker"/> which one is open via <see cref="PanelTabTracker.RecordOwnLeftPanelToggle"/>/
/// <see cref="PanelTabTracker.RecordOwnRightPanelToggle"/> beforehand.
/// </summary>
public static class PanelTabPressEffect
{
    private enum Kind
    {
        None,
        Toggle,
        Advance,
        Retreat,
    }

    /// <summary>
    /// Arms whatever bookkeeping <paramref name="action"/> needs before its
    /// keystroke is injected. Always call this before sending the keystroke,
    /// never after - see this type's remarks for why the toggle actions
    /// (<see cref="PanelTabTracker.LeftPanelToggleAction"/>/
    /// <see cref="PanelTabTracker.RightPanelToggleAction"/>) need it that way
    /// round.
    /// </summary>
    public static PendingPanelTabEffect Arm(PanelTabTracker tracker, string action)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(action);

        return action switch
        {
            PanelTabTracker.LeftPanelToggleAction => PendingPanelTabEffect.ForToggle(PanelSide.Left, tracker.RecordOwnLeftPanelToggle()),
            PanelTabTracker.RightPanelToggleAction => PendingPanelTabEffect.ForToggle(PanelSide.Right, tracker.RecordOwnRightPanelToggle()),
            PanelTabTracker.AdvanceTabAction => PendingPanelTabEffect.ForAdvance(),
            PanelTabTracker.RetreatTabAction => PendingPanelTabEffect.ForRetreat(),
            _ => PendingPanelTabEffect.None,
        };
    }

    /// <summary>
    /// What <see cref="Arm"/> decided for one specific press, resolved once
    /// the caller knows whether that press actually reached Elite. See
    /// <see cref="PanelTabPressEffect"/>'s remarks for the full reasoning;
    /// this struct only carries the small amount of state one press needs
    /// (which credit, if any, to revoke) between the two calls.
    /// </summary>
    public readonly struct PendingPanelTabEffect
    {
        private readonly Kind _kind;
        private readonly PanelSide _side;
        private readonly long _toggleCreditId;

        private PendingPanelTabEffect(Kind kind, PanelSide side, long toggleCreditId)
        {
            _kind = kind;
            _side = side;
            _toggleCreditId = toggleCreditId;
        }

        /// <summary>The action named was not one <see cref="PanelTabPressEffect"/> tracks; <see cref="Resolve"/> does nothing.</summary>
        public static PendingPanelTabEffect None { get; } = new(Kind.None, default, 0);

        internal static PendingPanelTabEffect ForToggle(PanelSide side, long creditId) => new(Kind.Toggle, side, creditId);
        internal static PendingPanelTabEffect ForAdvance() => new(Kind.Advance, default, 0);
        internal static PendingPanelTabEffect ForRetreat() => new(Kind.Retreat, default, 0);

        /// <param name="tracker">The same tracker <see cref="Arm"/> was called against.</param>
        /// <param name="sent">
        /// <see langword="true"/> when the keystroke actually reached Elite;
        /// <see langword="false"/> when it was refused, or the caller has no
        /// way to know and chooses to assume the worst. A caller with no way
        /// to observe the outcome at all must pass <see langword="true"/> -
        /// see this type's remarks.
        /// </param>
        public void Resolve(PanelTabTracker tracker, bool sent)
        {
            ArgumentNullException.ThrowIfNull(tracker);

            switch (_kind)
            {
                case Kind.Toggle:
                    if (!sent)
                    {
                        if (_side == PanelSide.Left)
                        {
                            tracker.RevokeOwnLeftPanelToggle(_toggleCreditId);
                        }
                        else
                        {
                            tracker.RevokeOwnRightPanelToggle(_toggleCreditId);
                        }
                    }

                    break;

                case Kind.Advance:
                    if (sent)
                    {
                        tracker.RecordOwnFocusedTabAdvance();
                    }

                    break;

                case Kind.Retreat:
                    if (sent)
                    {
                        tracker.RecordOwnFocusedTabRetreat();
                    }

                    break;
            }
        }
    }
}
