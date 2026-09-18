using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Gated step over the <b>journal</b>, not <c>Status.json</c>: <c>{
/// "waitForEdge": [tokens], "failOn": [tokens], "failureDetailField": "Name",
/// "timeoutMs": t }</c>. Built for <c>request-docking</c>'s acknowledgement
/// (LC19): a request can resolve as either <c>DockingGranted</c> or
/// <c>DockingDenied</c>, and a step that only recognized the grant would
/// hang forever on the commonest failure - a denial for range emits
/// <c>DockingDenied</c> ALONE, with no preceding <c>DockingRequested</c> at
/// all.
///
/// <see cref="SucceedOn"/> and <see cref="FailOn"/> are both OR'd
/// internally (unlike <see cref="ConditionList"/>'s AND) - the step
/// succeeds the instant any <see cref="SucceedOn"/> edge has been seen, and
/// fails the instant any <see cref="FailOn"/> edge has, whichever comes
/// first. <see cref="FailOn"/> may be empty.
///
/// <b>Bounded by <see cref="Timeout"/> (O28, 2026-09-08).</b> Used to be
/// deliberately unbounded, matching <see cref="JournalStateStore.WaitForEdgeAsync"/>'s
/// own decision: <c>DockSRV</c> measured at 54s and 61s from the keypress,
/// unbounded in principle, because it is a physical process. That reasoning
/// is still correct for a completion like <c>DockSRV</c>, but it collided
/// with <c>POST /api/press</c> awaiting a macro run synchronously - an edge
/// that never arrives (tab tracker wrong, presses landed on the wrong panel)
/// held the whole endpoint, and every button on every device, hostage. Live
/// on 2026-09-08: <c>waitForEdge</c> waited 198 seconds for a grant that the
/// macro's own presses never caused - the commander requested docking by
/// hand while the macro sat holding the lock, and the macro claimed credit
/// for a request it never made. A bounded <see cref="Timeout"/> fixes both:
/// it caps the lockout, and an edge arriving long after the timeout is not
/// plausibly the macro's own doing, so the bound doubles as a crude
/// causality filter. <c>timeoutMs</c> is optional in the JSON; absent, it
/// defaults to <see cref="MacroTimingDefaults.DefaultWaitForEdgeTimeout"/>
/// (see that constant's remarks for the measurement behind the number).
/// </summary>
/// <param name="SucceedOn">Parsed edge conditions - the step succeeds when any has been seen since the run started.</param>
/// <param name="SucceedOnTokens">The original <c>Journal:</c> tokens for <see cref="SucceedOn"/>, kept for step-progress logging.</param>
/// <param name="FailOn">Parsed edge conditions - the step fails (aborting the macro) when any has been seen since the run started. May be empty.</param>
/// <param name="FailOnTokens">The original tokens for <see cref="FailOn"/>.</param>
/// <param name="FailureDetailField">
/// An optional journal string property (e.g. <c>"Reason"</c>) read off the
/// event that satisfied whichever <see cref="FailOn"/> edge matched, and
/// folded into the abort reason - <c>DockingDenied</c>'s <c>Reason</c>
/// (<c>Distance</c>/<c>NoSpace</c>/<c>Offences</c>/<c>NoReason</c>) is the
/// motivating case, so the commander sees what to change rather than a bare
/// "denied". <see langword="null"/> when no detail field is wanted.
/// </param>
/// <param name="Timeout">
/// How long to wait before giving up and aborting the macro. Always
/// positive - validated at load, same discipline as <see cref="WaitForStep.Timeout"/>.
/// </param>
public sealed record WaitForEdgeStep(
    IReadOnlyList<EdgeCondition> SucceedOn,
    IReadOnlyList<string> SucceedOnTokens,
    IReadOnlyList<EdgeCondition> FailOn,
    IReadOnlyList<string> FailOnTokens,
    string? FailureDetailField,
    TimeSpan Timeout) : MacroStep;
