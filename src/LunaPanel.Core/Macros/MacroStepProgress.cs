namespace LunaPanel.Core.Macros;

/// <summary>
/// One <see cref="System.IProgress{T}"/> report from <see cref="MacroRunner.RunAsync"/>,
/// one per executed step. This is what makes a blind macro debuggable from
/// the sofa over SSE - a caller can render exactly which step is running,
/// which one failed, and how long each one took, without waiting for the
/// whole macro to finish or fail.
/// </summary>
/// <param name="StepIndex">
/// <b>The Nth step actually executed, in run order</b> - 0 for the first step
/// that ran, counting up once per executed step at any depth.
///
/// [2026-09-17] This used to mean "position in <see cref="MacroDefinition.Steps"/>",
/// and for a macro with no <c>branch</c> step the two readings are identical -
/// which is exactly why the change is worth stating rather than leaving to be
/// inferred. A <see cref="BranchStep"/> runs only one of its two arms, so a
/// step inside an arm has no position in <see cref="MacroDefinition.Steps"/>
/// at all, and the flat reading stopped being expressible. Verified safe to
/// repurpose before doing it: every consumer (the client's run-result sheet,
/// <c>MacroPresser</c>, <c>ServerHostBuilder</c>'s diagnostics) only displays
/// or logs this value - none re-index back into <see cref="MacroDefinition.Steps"/>
/// with it. <b>Do not start doing so</b>; for a macro containing a branch it
/// would read the wrong step.
/// </param>
/// <param name="StepKind">The step's JSON discriminator key (<c>"press"</c>, <c>"wait"</c>, <c>"require"</c>, <c>"waitFor"</c>, <c>"pressUntil"</c>, or <c>"branch"</c>).</param>
/// <param name="Outcome">Whether the step succeeded or failed.</param>
/// <param name="Elapsed">How long the step took, per the runner's injected <see cref="TimeProvider"/>.</param>
public sealed record MacroStepProgress(int StepIndex, string StepKind, MacroStepOutcome Outcome, TimeSpan Elapsed);
