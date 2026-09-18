using LunaPanel.Core.Bindings;
using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Input;

/// <summary>
/// The macro counterpart to <see cref="ChordPresser"/>'s foreground/
/// integrity guard discipline (<c>ref/docs/injection.md</c>), applied to a
/// whole <see cref="MacroRunner"/> run instead of one chord.
///
/// Checks the guard exactly once, via <see cref="Win32KeyInjector.CheckGuard"/>,
/// <b>before</b> any key of the macro is ever sent. This mirrors why
/// <see cref="ChordPresser"/> doesn't run a single press through
/// <see cref="MacroRunner"/> at all: <c>MacroRunner</c>'s
/// <c>IKeyInjector.KeyDown</c>/<c>KeyUp</c> calls are <see langword="void"/>,
/// so if the guard were only ever evaluated per-keystroke deep inside
/// <see cref="Win32KeyInjector"/>, a macro whose every key was silently
/// refused (Elite not foreground) would still report
/// <see cref="MacroRunOutcome.Success"/> - exactly the silent-success
/// failure mode this whole guard exists to prevent, now applied to a run of
/// several keystrokes instead of one. Refusing the whole run up front, with
/// the same <see cref="InjectionOutcome"/> vocabulary a single press uses,
/// is what lets a macro refuse exactly as a single action does.
///
/// A run that passes the pre-flight check is handed to
/// <see cref="MacroRunner.RunAsync"/> unmodified - once running, an
/// in-flight focus change is accepted exposure, not handled here (the same
/// "harmless noise" tolerance <c>MacroRunner</c>'s own chord choreography
/// already accepts for a stray key-up - see <c>ref/docs/injection.md</c>'s
/// "Key-up bypasses the foreground guard").
/// </summary>
public static class MacroPresser
{
    /// <param name="Fired"><see langword="true"/> only when <see cref="Outcome"/> is <c>"Sent"</c>.</param>
    /// <param name="Outcome">
    /// One of the pre-flight <see cref="InjectionOutcome"/> names
    /// (<c>"GameNotForeground"</c>/<c>"UipiSuspected"</c>) when the guard
    /// refused before the run ever started; otherwise the
    /// <see cref="MacroRunOutcome"/> name (<c>"Success"</c>/<c>"Aborted"</c>/
    /// <c>"Busy"</c>/<c>"Cancelled"</c>) the run itself reached. Renders as <c>"Sent"</c> on
    /// success, matching a single press's own vocabulary rather than
    /// exposing <c>MacroRunOutcome.Success</c>'s different spelling.
    /// </param>
    /// <param name="Reason">The operator-readable reason for a refusal or abort; <see langword="null"/> on success.</param>
    /// <param name="FailedStepIndex">The step the macro aborted at, when <see cref="Outcome"/> is <c>"MacroAborted"</c>; otherwise <see langword="null"/>.</param>
    /// <param name="Steps">
    /// One <see cref="MacroStepProgress"/> per step the run actually executed,
    /// in order - empty when the guard refused before the run ever started
    /// (<see cref="GuardRefused"/>), since no step ran. Populated for every
    /// other outcome, including <c>"MacroAborted"</c> and <c>"Cancelled"</c>,
    /// so the caller can show what happened up to the point the run stopped -
    /// this is the per-step read-back <c>ref/docs/macro-builder.md</c>'s "What
    /// the builder shows after a run" describes as data <see cref="MacroRunner"/>
    /// already computes and the press response used to discard.
    /// </param>
    public sealed record MacroPressResult(bool Fired, string Outcome, string? Reason, int? FailedStepIndex, IReadOnlyList<MacroStepProgress> Steps)
    {
        public static MacroPressResult GuardRefused(InjectionAttemptResult guardResult) =>
            new(false, guardResult.Outcome.ToString(), guardResult.Reason, null, Array.Empty<MacroStepProgress>());

        public static MacroPressResult FromRunResult(MacroRunResult result, IReadOnlyList<MacroStepProgress> steps) => result.Outcome switch
        {
            MacroRunOutcome.Success => new MacroPressResult(true, "Sent", null, null, steps),
            MacroRunOutcome.Aborted => new MacroPressResult(false, "MacroAborted", result.FailureReason, result.FailedStepIndex, steps),
            MacroRunOutcome.Busy => new MacroPressResult(false, "Busy", result.FailureReason, null, steps),
            // Not Fired: a run the commander stopped part-way did not do
            // what the button says it does, and the panel shows Reason as a
            // toast precisely when Fired is false - which is where "you
            // pressed it again" needs to appear.
            MacroRunOutcome.Cancelled => new MacroPressResult(false, "Cancelled", result.FailureReason, null, steps),
            _ => throw new InvalidOperationException($"Unhandled MacroRunOutcome '{result.Outcome}'."),
        };
    }

    /// <summary>
    /// A synchronous <see cref="IProgress{T}"/> - the framework's own
    /// <see cref="Progress{T}"/> posts its callback through a captured
    /// <see cref="SynchronizationContext"/>, which in an ASP.NET Core request
    /// with no such context queues onto the thread pool instead of running
    /// inline, and could report a step after the run task has already
    /// completed. Every report must have landed in the list by the time the
    /// task the caller awaits completes, so the read-back is complete.
    ///
    /// Since O28 (2026-09-17) that list is also read from a DIFFERENT thread
    /// than the one appending to it - the run appends on whatever thread
    /// resumed it, and the caller's continuation reads it wherever the task
    /// completion happened to schedule it. Reports happen-before the task
    /// completes and the continuation happens-after, so the ordering is
    /// already established by the task itself; the lock in
    /// <see cref="StepCollector"/> is the explicit statement of that
    /// contract rather than a reliance on it being true by accident.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public SynchronousProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }

    /// <summary>
    /// The per-run steps list, with every access - each append from the run
    /// and the one final read from the completion - under one lock. See
    /// <see cref="SynchronousProgress{T}"/> for why this crossed a thread
    /// boundary in O28. <see cref="Snapshot"/> copies rather than exposing
    /// the list, so nothing outside ever holds a reference to it.
    /// </summary>
    private sealed class StepCollector
    {
        private readonly object _gate = new();
        private readonly List<MacroStepProgress> _steps = new();

        public void Add(MacroStepProgress step)
        {
            lock (_gate)
            {
                _steps.Add(step);
            }
        }

        public IReadOnlyList<MacroStepProgress> Snapshot()
        {
            lock (_gate)
            {
                return _steps.ToArray();
            }
        }
    }

    /// <summary>
    /// What <see cref="Start"/> hands back. Exactly one of two shapes:
    /// <see cref="Immediate"/> non-null (the guard refused, or the runner
    /// answered <c>Busy</c>/<c>Cancelled</c> synchronously - nothing is
    /// running), or <see cref="Immediate"/> null with the run genuinely in
    /// flight. <see cref="Completion"/> is never null and completes with the
    /// run's real <see cref="MacroPressResult"/> in BOTH cases - already
    /// complete for the synchronous shape - so a caller that wants the old
    /// blocking behaviour just awaits it (<see cref="RunAsync"/>).
    /// </summary>
    public sealed record MacroPressStart(MacroPressResult? Immediate, Task<MacroPressResult> Completion)
    {
        public bool Started => Immediate is null;
    }

    /// <summary>
    /// The non-blocking entry point (2026-09-17, O28): checks the guard, asks
    /// <see cref="MacroRunner.TryStart"/> to start the run, and returns the
    /// moment either has answered. The three synchronous outcomes - the
    /// guard refusing, <c>Busy</c>, <c>Cancelled</c> - come back as
    /// <see cref="MacroPressStart.Immediate"/> exactly as <see cref="RunAsync"/>
    /// always reported them; a genuine start comes back with no
    /// <see cref="MacroPressStart.Immediate"/> and a
    /// <see cref="MacroPressStart.Completion"/> the caller observes however
    /// it likes. The guard check stays FIRST, before the runner is consulted
    /// at all - the pre-flight discipline this class exists for.
    /// </summary>
    public static MacroPressStart Start(
        Win32KeyInjector injector,
        MacroRunner runner,
        MacroDefinition macro,
        BindingsFile bindingsFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(bindingsFile);

        var guardResult = injector.CheckGuard();
        if (guardResult.Outcome != InjectionOutcome.Sent)
        {
            var refused = MacroPressResult.GuardRefused(guardResult);
            return new MacroPressStart(refused, Task.FromResult(refused));
        }

        var steps = new StepCollector();
        var progress = new SynchronousProgress<MacroStepProgress>(steps.Add);
        if (!runner.TryStart(macro, bindingsFile, out var run, progress, cancellationToken))
        {
            // Busy or Cancelled: the runner answered synchronously and `run`
            // is already complete, so reading it here cannot block.
            var immediate = MacroPressResult.FromRunResult(run.Result, steps.Snapshot());
            return new MacroPressStart(immediate, Task.FromResult(immediate));
        }

        return new MacroPressStart(null, CompleteAsync(run, steps));
    }

    private static async Task<MacroPressResult> CompleteAsync(Task<MacroRunResult> run, StepCollector steps)
    {
        var runResult = await run.ConfigureAwait(false);
        return MacroPressResult.FromRunResult(runResult, steps.Snapshot());
    }

    /// <summary>
    /// The blocking shape: <see cref="Start"/>, then wait for whatever it
    /// handed back. What every pre-existing test drives, kept as this
    /// one-line composition rather than a second copy of the start logic.
    /// </summary>
    public static Task<MacroPressResult> RunAsync(
        Win32KeyInjector injector,
        MacroRunner runner,
        MacroDefinition macro,
        BindingsFile bindingsFile,
        CancellationToken cancellationToken = default) =>
        Start(injector, runner, macro, bindingsFile, cancellationToken).Completion;
}
