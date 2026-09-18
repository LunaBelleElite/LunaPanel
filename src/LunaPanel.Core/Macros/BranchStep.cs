using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Macros;

/// <summary>
/// The grammar's one conditional step:
/// <c>{ "branch": [conditions], "then": [steps], "else": [steps] }</c>.
///
/// <b>The only step kind that redirects execution.</b> Every other step runs
/// exactly once, in the order the file lists it; this one evaluates
/// <see cref="Conditions"/> against <c>GameStateStore.Current</c> at the
/// moment it is reached and then runs <em>one</em> of its two arms. The other
/// arm never runs at all.
///
/// <b>Unknown counts as the negative case.</b> A <see langword="null"/>
/// snapshot - the game not running yet - takes <see cref="Else"/>, with a
/// <c>WARN</c> naming why. That is not a new rule invented here: it is
/// exactly what <c>Condition</c>/<c>ConditionList</c> already do internally
/// and what <c>MacroRunner.ExecuteRequire</c> already reports. Note this is
/// NOT the never-gate-a-macro rule (<c>.claude-memory/never-gate-a-macro.md</c>)
/// being broken - nothing is refused, and keys still fire; the macro simply
/// takes the arm written for "not that state".
///
/// <b>Nesting is capped at one level</b> - <c>MacroDefinition.Parse</c>
/// rejects a <c>branch</c> inside a <c>then</c>/<c>else</c> arm. Same cap,
/// and same reasoning, as the folders feature shipped on 2026-09-16: one
/// motivating use case today, so the grammar, the parser's error labelling
/// and the mutation surface all stay provably bounded. Revisit only when a
/// real second use case needs depth.
/// </summary>
/// <param name="Conditions">The parsed condition list, ANDed - the same <see cref="ConditionList"/> <c>require</c>/<c>waitFor</c>/<c>pressUntil</c> parse.</param>
/// <param name="ConditionTokens">
/// The original tokens fed to <see cref="ConditionList.Parse"/>, kept for
/// logging and for serialization - <c>ConditionList</c>/<c>Condition</c> do
/// not preserve their own source text. Never empty: a branch with no
/// condition is always-true and therefore not a branch at all, so
/// <c>MacroDefinition.Parse</c> rejects it.
/// </param>
/// <param name="Then">Steps run when the condition holds. Never null; may be empty, but not at the same time as <see cref="Else"/>.</param>
/// <param name="Else">Steps run when it does not, or when the game's state is not known. Never null; may be empty, but not at the same time as <see cref="Then"/>.</param>
public sealed record BranchStep(
    ConditionList Conditions,
    IReadOnlyList<string> ConditionTokens,
    IReadOnlyList<MacroStep> Then,
    IReadOnlyList<MacroStep> Else) : MacroStep;
