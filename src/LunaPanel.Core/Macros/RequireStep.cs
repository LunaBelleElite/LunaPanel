using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Gated step: <c>{ "require": [conditions] }</c>. Evaluated once, right
/// now, against whatever snapshot <c>GameStateStore.Current</c> currently
/// holds (a <see langword="null"/> snapshot - the game not running yet -
/// evaluates as not-satisfied, same as any other false condition).
///
/// <b>Reversed 2026-09-12:</b> this step used to abort the whole macro,
/// sending <b>no keys at all</b>, when the condition wasn't satisfied - see
/// <c>MacroRunner.ExecuteRequire</c>'s remarks and
/// <c>never-gate-a-macro.md</c>. It now always succeeds: when the condition
/// is not satisfied, <c>MacroRunner</c> logs a <c>WARN</c> naming the
/// unsatisfied tokens and what was actually observed, then proceeds anyway.
/// This step exists to make that belief explainable, not to refuse the
/// macro on it.
/// </summary>
/// <param name="Conditions">The parsed condition list, ANDed.</param>
/// <param name="ConditionTokens">
/// The original condition tokens actually fed to <see cref="ConditionList.Parse"/>
/// - kept alongside <see cref="Conditions"/> only for step-progress logging.
/// <c>ConditionList</c>/<c>Condition</c> don't preserve their own source
/// text.
/// </param>
/// <remarks>
/// This step used to also understand a special <c>LeftPanelTabKnown</c>
/// token, refusing the whole macro before any key was pressed when
/// <see cref="PanelTabTracker.CurrentTab"/> was not known. **Removed
/// 2026-09-08** at the user's explicit governing decision reversing the
/// refuse-when-unknown design - see <see cref="PanelTabTracker"/>'s remarks,
/// "Reversed 2026-09-08", and <c>ref/docs/panel-tab-tracking.md</c>. The
/// token is no longer meta-syntax this step understands: a <c>require</c>
/// array that still names it now falls straight through to
/// <see cref="ConditionList.Parse"/> like any other token and is rejected
/// there as an unknown status condition name, rather than being silently
/// ignored - see <c>MacroDefinition.ParseRequire</c>.
/// </remarks>
public sealed record RequireStep(ConditionList Conditions, IReadOnlyList<string> ConditionTokens) : MacroStep;
