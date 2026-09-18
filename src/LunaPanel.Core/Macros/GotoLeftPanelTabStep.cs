using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Gated step: <c>{ "gotoLeftPanelTab": "Contacts" }</c>. Asks the runner's
/// <see cref="PanelTabTracker"/> how many <see cref="PanelTabTracker.AdvanceTabAction"/>
/// presses reach <see cref="Target"/> from wherever the panel is currently
/// tracked to be, and presses exactly that many - never a hardcoded count.
///
/// Built for <c>request-docking</c> (<c>ref/docs/panel-tab-tracking.md</c>):
/// the panel starts on NAVIGATION only after a reset, and after a first run
/// this macro itself leaves it on CONTACTS - so "how far to move" is a
/// question with a different answer every time, not a constant.
///
/// <b>Reversed 2026-09-08:</b> this step used to refuse the whole macro
/// (sending no keys) when <see cref="PanelTabTracker.CurrentTab"/> was not
/// known, because the left panel's tabs wrap and there is no way to "wind"
/// to a known position the way the docked screen's clamped list allows. The
/// user reversed that at the governing decision recorded on
/// <see cref="PanelTabTracker"/>'s own remarks ("We should never NOT be
/// allowed to use a macro"): <see cref="PanelTabTracker.CurrentTab"/> is now
/// always present, so this step always computes and presses a route. When
/// <see cref="PanelTabTracker.LeftTabConfidence"/> is
/// <see cref="TabConfidence.Low"/>, <c>MacroRunner</c> logs a <c>WARN</c>
/// naming the believed tab before pressing anything, so a wrong outcome
/// stays explainable even though it is no longer prevented.
/// </summary>
/// <param name="Target">Which tab the panel should be on once this step succeeds.</param>
public sealed record GotoLeftPanelTabStep(PanelTab Target) : MacroStep;
