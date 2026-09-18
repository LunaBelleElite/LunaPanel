namespace LunaPanel.Core.GameState;

/// <summary>
/// How much <see cref="PanelTabTracker"/> trusts its own tracked tab index
/// for one side panel, right now. Added 2026-09-08 when the earlier
/// refuse-when-unknown design was reversed at the user's explicit
/// instruction - see <see cref="PanelTabTracker"/>'s own remarks, "Reversed
/// 2026-09-08", and <c>ref/docs/panel-tab-tracking.md</c> for the full
/// reasoning and the governing quote.
///
/// <b>This is deliberately not a third "unknown" state.</b> A tracked index
/// is now always present (<see cref="PanelTabTracker.CurrentTab"/> and
/// <see cref="PanelTabTracker.RightCurrentTab"/> are non-nullable) - this
/// enum only says how much LunaPanel currently trusts that index, not
/// whether one exists.
/// </summary>
public enum TabConfidence
{
    /// <summary>
    /// LunaPanel caused every change since this position was last confirmed
    /// (by construction, or by a genuine reset - <c>FSDJump</c>, an on-foot
    /// round trip, or a live <c>LoadGame</c>).
    /// </summary>
    High,

    /// <summary>
    /// A <c>GuiFocus</c> open/close edge arrived that LunaPanel did not arm -
    /// the commander may have touched this panel by hand, so the tracked
    /// index is a belief, not a confirmed fact. LunaPanel still acts on it:
    /// see <see cref="PanelTabTracker"/>'s remarks, "Reversed 2026-09-08".
    /// </summary>
    Low,
}
