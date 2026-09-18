using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Gated, retrying step: <c>{ "pressUntil": "Action", "cond": [conditions],
/// "timeoutMs": t, "maxAttempts": n }</c> OR <c>{ "pressUntil": "Action",
/// "condJournal": "EventName", "timeoutMs": t, "maxAttempts": n }</c>. This
/// is the retry primitive - see "Macro grammar" in
/// <c>ref/docs/design-decisions.md</c> for why it exists: a toggle bind is a
/// blind flip, and re-sending the key when the expected state doesn't arrive
/// is what makes the macro authoritative rather than merely usually-correct.
///
/// Each attempt presses the resolved chord once (held for
/// <see cref="MacroTimingDefaults.DefaultHoldDuration"/> - this step has no
/// per-step hold override in the grammar), then waits up to
/// <see cref="Timeout"/> for the gate to become true. If it doesn't, the
/// chord is pressed again, up to <see cref="MaxAttempts"/> times total; if
/// the gate still hasn't arrived after the last attempt, the macro aborts
/// naming this step.
///
/// <b>Exactly one of two mutually-exclusive gates, never both, never
/// neither</b> - validated at load by <see cref="MacroDefinition.Parse"/>:
/// <see cref="Conditions"/> (a STATUS condition list, evaluated against a
/// snapshot, the original shape) XOR <see cref="JournalCondition"/> (a
/// single JOURNAL event, added 2026-09-13, using the exact same
/// <see cref="EdgeCondition"/> type <see cref="WaitForEdgeStep"/> already
/// uses - "RefuelAll" fires with no corresponding STATUS bit, so a
/// status-only gate could never wait for it). <see cref="Conditions"/> and
/// <see cref="ConditionTokens"/> are <see langword="null"/> when
/// <see cref="JournalCondition"/> is set, and vice versa.
/// </summary>
/// <param name="Action">The Frontier action name to resolve and press.</param>
/// <param name="Conditions">The parsed STATUS condition list, ANDed. <see langword="null"/> when <see cref="JournalCondition"/> is set instead.</param>
/// <param name="ConditionTokens">The original condition tokens - see <see cref="RequireStep.ConditionTokens"/> for why these are kept separately. <see langword="null"/> when <see cref="JournalCondition"/> is set instead.</param>
/// <param name="Timeout">How long to wait for the gate after each press. Always positive - validated at load.</param>
/// <param name="MaxAttempts">Total presses allowed before giving up. Always positive - validated at load.</param>
/// <param name="JournalCondition">The parsed JOURNAL event gate. <see langword="null"/> when <see cref="Conditions"/> is set instead.</param>
public sealed record PressUntilStep(
    string Action,
    ConditionList? Conditions,
    IReadOnlyList<string>? ConditionTokens,
    TimeSpan Timeout,
    int MaxAttempts,
    EdgeCondition? JournalCondition = null) : MacroStep;
