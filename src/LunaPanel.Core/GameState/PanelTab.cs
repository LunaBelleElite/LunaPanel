namespace LunaPanel.Core.GameState;

/// <summary>
/// The left panel's four tabs, in cycle order. Measured 2026-09-07
/// (<c>tests/notes/live-checks.md</c> LC19); see
/// <c>ref/docs/panel-tab-tracking.md</c> for the full design this enum
/// belongs to.
///
/// The cycle is <c>Galaxy -&gt; Navigation -&gt; Transactions -&gt; Contacts</c>
/// and it <b>wraps</b> - one more <see cref="PanelTabTracker"/> press past
/// <see cref="Contacts"/> lands back on <see cref="Galaxy"/>. A fifth tab,
/// <c>TARGET</c>, is visible in the same row but is not reachable by the
/// cycle keys at all (O20, closed) - it is deliberately not a member of this
/// enum, because a value nothing can ever cycle to or from would be a dead
/// case in every switch that touches this type.
///
/// <see cref="Navigation"/> is the tab both known reset points
/// (<c>FSDJump</c>, an on-foot round trip) restore, and it is also where the
/// panel starts on a fresh login - not the first name in the cycle order,
/// which is <see cref="Galaxy"/>.
/// </summary>
public enum PanelTab
{
    Galaxy,
    Navigation,
    Transactions,
    Contacts,
}
