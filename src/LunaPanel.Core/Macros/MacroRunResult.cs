namespace LunaPanel.Core.Macros;

/// <summary>Final result of one <see cref="MacroRunner.RunAsync"/> call.</summary>
/// <param name="Outcome">Success, an aborted run, or a busy refusal.</param>
/// <param name="FailedStepIndex">The step that failed, when <see cref="Outcome"/> is <see cref="MacroRunOutcome.Aborted"/>; otherwise <see langword="null"/>.</param>
/// <param name="FailureReason">Why that step failed, when <see cref="Outcome"/> is <see cref="MacroRunOutcome.Aborted"/> (why the run was refused, when <see cref="MacroRunOutcome.Busy"/>; what stopped it, when <see cref="MacroRunOutcome.Cancelled"/>); otherwise <see langword="null"/>.</param>
public sealed record MacroRunResult(MacroRunOutcome Outcome, int? FailedStepIndex, string? FailureReason)
{
    public static MacroRunResult Success { get; } = new(MacroRunOutcome.Success, null, null);

    /// <summary>
    /// Generic busy result kept for callers/tests that only care about the
    /// <see cref="MacroRunOutcome"/> shape, not which guard refused.
    /// <see cref="MacroRunner"/> itself never returns this one directly - it
    /// returns <see cref="InjectionBusy"/>, the only refusal left since the
    /// same-macro refusal became a cancel (see <see cref="Cancelled"/>).
    /// </summary>
    public static MacroRunResult Busy { get; } = new(MacroRunOutcome.Busy, null, "A macro is already running.");

    /// <summary>
    /// Refused by the injection lock: another macro is mid-keystroke and the
    /// keyboard is not free. A physical resource conflict, not a guess about
    /// game state - see <c>MacroRunner._injecting</c>'s own remarks and
    /// <c>ref/docs/macros.md</c> for why this is exempt from the
    /// never-gate-on-uncertainty rule.
    /// </summary>
    public static MacroRunResult InjectionBusy { get; } = new(
        MacroRunOutcome.Busy,
        null,
        "Another macro is using the keyboard right now. Try again in a second.");

    /// <summary>
    /// The run was stopped by the commander pressing the same macro's own
    /// button again (the ruling of 2026-09-09 - see
    /// <c>MacroRunner._runningMacroIds</c>'s own remarks). Returned by BOTH
    /// halves of that exchange: the run that was stopped, and the second
    /// press that stopped it - neither one succeeded, and from the
    /// commander's side both are the same single event ("I stopped it"), so
    /// they read the same in the toast <c>POST /api/press</c> puts on screen.
    ///
    /// <b>Superseded <c>DuplicateBusy</c>, removed 2026-09-09.</b> That
    /// factory returned <see cref="MacroRunOutcome.Busy"/> with the message
    /// "Macro '{macroId}' is already running and waiting on the game. Try
    /// again once it finishes." - a refusal, which is exactly what the
    /// ruling replaced. It is gone rather than deprecated: nothing in the
    /// runner can produce that refusal any more, so a factory that could
    /// still be called would only mislead.
    /// </summary>
    public static MacroRunResult Cancelled(string macroId) => new(
        MacroRunOutcome.Cancelled,
        null,
        $"Macro '{macroId}' stopped - you pressed it again.");

    public static MacroRunResult Aborted(int stepIndex, string reason) => new(MacroRunOutcome.Aborted, stepIndex, reason);
}
