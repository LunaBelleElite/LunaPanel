using System.Collections.Concurrent;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Executes a <see cref="MacroDefinition"/>'s steps in order. See the macro
/// grammar table in <c>ref/docs/design-decisions.md</c> and the task brief
/// for the settled behaviour of each step kind; this class is the one place
/// all seven are actually carried out.
///
/// <b>One lock and one switch, not two locks (2026-09-09).</b>
/// A single flag held for the whole run used to do two unrelated jobs at
/// once, and holding
/// it for the whole run is what let a <c>waitForEdge</c> block every other
/// macro for 198 seconds live - see <c>ref/docs/macros.md</c>'s "One lock and
/// one switch" section for the full reasoning. In short: <see cref="_injecting"/>
/// guards the keyboard, a physical resource, and is released the instant the
/// run's last press-emitting step completes even though the run continues,
/// refusing a DIFFERENT macro as <see cref="MacroRunOutcome.Busy"/>
/// (<see cref="MacroRunResult.InjectionBusy"/>);
/// <see cref="_runningMacroIds"/> tracks the run of one macro id for its
/// entire duration including any wait, and a second press of that SAME id
/// <b>cancels</b> the run in flight rather than refusing anything
/// (<see cref="MacroRunResult.Cancelled"/>) - the commander's ruling, "to
/// stop a running macro, just hit the same key again while it's going."
///
/// Actions are resolved through <c>LunaPanel.Core.Bindings.BindResolver.Resolve</c>
/// against the same <see cref="BindingsFile"/> a caller would use to compute
/// slot status, so a degraded macro and a degraded slot always agree with
/// each other about whether an action is currently usable - see
/// <c>ref/docs/macros.md</c>.
///
/// Every delay (hold duration, inter-press gap, modifier settle gap, a
/// <c>wait</c> step, and every gated step's poll) goes through the injected
/// <see cref="TimeProvider"/> via <c>Task.Delay(TimeSpan, TimeProvider,
/// CancellationToken)</c> or <see cref="GameStateStore.WaitForAsync"/> (which
/// does the same internally) - never a real sleep - so a test can drive an
/// entire macro run, including its timing, with a fake clock. Key injection
/// goes through the injected <see cref="IKeyInjector"/> only - this class
/// never touches Win32 or any other interop.
/// </summary>
public sealed class MacroRunner
{
    private readonly IKeyInjector _injector;
    private readonly GameStateStore _gameState;
    private readonly JournalStateStore _journal;
    private readonly PanelTabTracker _tabTracker;
    private readonly TimeProvider _clock;
    private readonly IDiagnosticLog _log;

    // Null in every pre-existing caller/test (an optional trailing
    // constructor parameter, so none of the many existing `new MacroRunner(...)`
    // call sites needed updating for this feature) - RunAsync falls back to
    // MacroTimingSettings.Default in that case, which is exactly today's
    // MacroTimingDefaults values, so behaviour is unchanged when this is
    // absent. When present, loaded fresh once per run (see RunAsync) rather
    // than cached on this instance - this class is a singleton and different
    // macro ids can run concurrently (see _runningMacroIds' own remarks), so
    // a per-run local, not a field, is what a settings change applies to
    // without a restart (ref/docs/macro-timing.md).
    private readonly MacroTimingSettingsStore? _timingSettingsStore;

    // Guards the KEYBOARD, not any particular macro. 0 = idle, 1 = a macro
    // is mid-injection. Held from before step 0 until the run's LAST
    // press-emitting step (press/pressUntil/gotoLeftPanelTab - see
    // IsInjectingStep) completes, then released even though the run
    // continues into any trailing wait/require/waitFor/waitForEdge steps -
    // never a timer, and never released-and-reacquired per step, which
    // would let a second macro interleave its presses between two of the
    // first macro's steps. A DIFFERENT macro arriving while this is 1 is
    // refused with MacroRunResult.InjectionBusy: two macros interleaving
    // keystrokes into one focused game window is a physical conflict this
    // runner knows about itself, not a guess about game state, so it is
    // exempt from the project's never-gate-on-uncertainty rule
    // (ref/docs/macros.md).
    private int _injecting;

    // Tracks ONE RUN PER MACRO ID for the ENTIRE run including any wait -
    // unlike _injecting, this entry is never removed early. The value is
    // that run's own CancellationTokenSource, which is the whole point:
    //
    //   **A second RunAsync call for the SAME macro id CANCELS the run in
    //   flight - it does not refuse it.** The commander's ruling, 2026-09-09:
    //   "to stop a running macro, just hit the same key again while it's
    //   going. makes it very simple for anyone, and intuitive."
    //
    // Superseded ver-0.29.1.0-dev's duplicate-run GUARD, which refused the
    // second press with MacroRunResult.DuplicateBusy ("Macro '{id}' is
    // already running and waiting on the game. Try again once it finishes.")
    // so that request-docking could not re-enter itself while its own grant
    // was pending (LC18). Re-entry is still impossible - the first run is
    // stopped before the second press returns a result, so the two never
    // overlap - but the commander is no longer told "no". Stopping a run
    // part-way leaves the game wherever the macro got to (a half-run
    // request-docking leaves the left panel open on Contacts); that is
    // accepted, exactly as this project's standing rule that a macro is
    // never blocked for the commander's own good implies - see
    // ref/docs/macros.md.
    //
    // TryAdd is literally the first decision RunAsync makes, before any
    // await, so a re-press is seen synchronously from the caller's point of
    // view - the same discipline the refusal it replaced already had.
    //
    // The CancellationTokenSource stored here is deliberately NEVER disposed:
    // the cancelling press can always race the run's own completion, and
    // Cancel() on a disposed source is documented to throw, so disposal
    // would trade a benign race for an exception on the commander's own
    // stop button. Nothing leaks in production - the linked source is built
    // from CancellationToken.None there (ServerHostBuilder passes no token),
    // which registers no callback on anything.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningMacroIds = new();

    /// <summary>
    /// Raised whenever <see cref="RunningMacroIds"/> changes - once when a
    /// run actually starts, once when it ends, however it ends. The panel's
    /// live channel subscribes so a macro's button lights for as long as it
    /// runs (<c>ref/docs/lit-state.md</c>), the same way a latch already
    /// does.
    ///
    /// Raised on whichever thread is running the macro, synchronously, and
    /// after the set has already changed - so a handler that reads
    /// <see cref="RunningMacroIds"/> sees the new state, not the old one.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Which macro ids are executing right now - a snapshot, safe to hold.
    /// Empty almost always: a macro run is measured in hundreds of
    /// milliseconds.
    ///
    /// A run that was refused before it began is never in here. A press that
    /// arrives while a DIFFERENT macro holds the keyboard
    /// (<see cref="MacroRunOutcome.Busy"/>) is refused before this set is
    /// published, deliberately: a button that flickered on and straight back
    /// off for a run that never happened would be a lie told in the one
    /// place this project uses to say "something is happening".
    /// </summary>
    public IReadOnlySet<string> RunningMacroIds => _runningMacroIds.Keys.ToHashSet(StringComparer.Ordinal);

    public MacroRunner(
        IKeyInjector injector,
        GameStateStore gameState,
        JournalStateStore journal,
        PanelTabTracker tabTracker,
        TimeProvider clock,
        IDiagnosticLog log,
        MacroTimingSettingsStore? timingSettingsStore = null)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _tabTracker = tabTracker ?? throw new ArgumentNullException(nameof(tabTracker));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _timingSettingsStore = timingSettingsStore;
    }

    /// <summary>
    /// Runs every step of <paramref name="macro"/> in order against
    /// <paramref name="bindingsFile"/>, reporting one <see cref="MacroStepProgress"/>
    /// per executed step through <paramref name="progress"/> (if supplied).
    /// Returns <see cref="MacroRunOutcome.Busy"/> immediately, running
    /// nothing, when a DIFFERENT macro holds the keyboard
    /// (<see cref="_injecting"/>), and <see cref="MacroRunOutcome.Cancelled"/>
    /// immediately - having stopped the run already in flight - when this
    /// same macro id is pressed again (<see cref="_runningMacroIds"/>).
    ///
    /// Exactly <see cref="TryStart"/> followed by awaiting whatever it handed
    /// back - the blocking shape every pre-existing caller and test already
    /// drives, kept as the one-line composition it is rather than a second
    /// copy of the start logic (2026-09-17, O28).
    /// </summary>
    public Task<MacroRunResult> RunAsync(
        MacroDefinition macro,
        BindingsFile bindingsFile,
        IProgress<MacroStepProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        TryStart(macro, bindingsFile, out var run, progress, cancellationToken);
        return run;
    }

    /// <summary>
    /// The synchronous half of a run, split out (2026-09-17, O28) so a caller
    /// can learn whether a run genuinely began WITHOUT waiting for it to end:
    /// <c>POST /api/press</c> answers the commander the moment this returns
    /// and observes <paramref name="run"/> separately. Returns
    /// <see langword="true"/> when the run is now in flight, with
    /// <paramref name="run"/> the task that completes when it ends; returns
    /// <see langword="false"/> for the two synchronous outcomes -
    /// <see cref="MacroRunOutcome.Cancelled"/> (this same id was already
    /// running and has now been told to stop) and <see cref="MacroRunOutcome.Busy"/>
    /// (a different macro holds the keyboard) - with <paramref name="run"/>
    /// already completed carrying that result, so a caller can treat both
    /// shapes uniformly by awaiting it.
    ///
    /// <b>Both decisions happen here, before any await, in this order:</b>
    /// <see cref="_runningMacroIds"/>' <c>TryAdd</c> first, then the
    /// <see cref="_injecting"/> compare-exchange - pinned, and unchanged by
    /// the split (<c>ref/docs/macros.md</c>'s "One lock and one switch").
    /// The <see cref="Changed"/> raise for a genuine start also stays on this
    /// side of the boundary, so a caller sees the running set already
    /// updated by the time it holds the task.
    /// </summary>
    public bool TryStart(
        MacroDefinition macro,
        BindingsFile bindingsFile,
        out Task<MacroRunResult> run,
        IProgress<MacroStepProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(bindingsFile);

        // This run's own token: cancelled either by the caller's token (the
        // linked parent, whose semantics are unchanged - it still throws out
        // of the run task) or by a second press of this same macro id, which
        // returns Cancelled instead. See _runningMacroIds' remarks for why
        // this source is never disposed once published.
        var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runToken = runCts.Token;

        // The single decision point for "is this id already running?" -
        // TryAdd, not a read-then-write, so two simultaneous presses cannot
        // both believe they are the first.
        if (!_runningMacroIds.TryAdd(macro.Id, runCts))
        {
            // Never published, so disposing this one is safe - unlike the
            // in-flight run's own source, which the run may be removing
            // right now.
            runCts.Dispose();

            if (_runningMacroIds.TryGetValue(macro.Id, out var inFlight))
            {
                inFlight.Cancel();
            }

            // Reported as Cancelled even in the narrow window where the run
            // finished between the TryAdd above and this lookup: the
            // commander pressed stop on something they saw running, and
            // "stopped" is the honest answer to that press either way.
            run = Task.FromResult(MacroRunResult.Cancelled(macro.Id));
            return false;
        }

        if (Interlocked.CompareExchange(ref _injecting, 1, 0) != 0)
        {
            // Removed WITHOUT raising Changed, and nothing announced it
            // starting either - see RunningMacroIds' own remarks for why a
            // Busy press must not produce a flash of a lit button.
            _runningMacroIds.TryRemove(macro.Id, out _);
            run = Task.FromResult(MacroRunResult.InjectionBusy);
            return false;
        }

        // The run is genuinely under way now: the id is published and the
        // keyboard is ours. Everything past this point ends in ExecuteRunAsync's
        // finally, which is the ONE place that takes it back down again.
        Changed?.Invoke();

        run = ExecuteRunAsync(macro, bindingsFile, progress, runToken, cancellationToken);
        return true;
    }

    /// <summary>
    /// The asynchronous half: everything after both guards have been taken
    /// and <see cref="Changed"/> has been raised. Owns the try/finally that
    /// releases both guards on every exit path - the split moved the start
    /// decisions out, not this.
    /// </summary>
    private async Task<MacroRunResult> ExecuteRunAsync(
        MacroDefinition macro,
        BindingsFile bindingsFile,
        IProgress<MacroStepProgress>? progress,
        CancellationToken runToken,
        CancellationToken cancellationToken)
    {
        // Released exactly once - either right after the run's last
        // injecting step below, or by the safety net in `finally` if the
        // macro aborts/throws before ever reaching it, or immediately below
        // if the macro has no injecting step at all. Never twice: Volatile.Write
        // is idempotent here anyway, but the flag keeps intent explicit.
        var injectionReleased = false;

        // How many steps have actually been EXECUTED so far, at any depth -
        // a shared cursor rather than a for-loop variable, because a branch
        // means there is no single flat list left to index into. Declared out
        // here, not in the loop, so the cancellation handler below can name
        // the step the run actually stopped at. See MacroStepProgress's own
        // remarks for the settled meaning of this number.
        var cursor = new StepCursor();

        void ReleaseInjectionLock()
        {
            if (!injectionReleased)
            {
                injectionReleased = true;
                Volatile.Write(ref _injecting, 0);
            }
        }

        try
        {
            // Asked once, before step 0, over the whole step TREE - a
            // structural question about what the macro could ever press, not
            // a per-run one, so a branch's two arms are both counted here
            // even though only one of them will run. False means the keyboard
            // was never actually needed and the lock is dropped before the
            // loop starts. Everything finer-grained than this is decided
            // per-position inside ExecuteSequenceAsync.
            if (!MightInject(macro.Steps, 0))
            {
                ReleaseInjectionLock();
            }

            // Taken once, before step 0, never per-step: an edge that lands
            // between two steps (docking's own 0s DockingRequested/Granted
            // gap, LC18) must already be "since" this mark, or a waitForEdge
            // step marking only at its own turn could start after the event
            // it needed already happened - see WaitForEdgeStep's remarks.
            var runWatermark = _journal.Mark();

            // Loaded once per run, into a LOCAL - never a field - and never
            // recomputed per step: the commander's setting (ref/docs/macro-timing.md)
            // beats the shipped default when stored, exactly as
            // MacroTimingSettings.Default already models by construction.
            // See _timingSettingsStore's own remarks for why this must be a
            // local rather than cached on the instance.
            var timing = _timingSettingsStore?.Load() ?? MacroTimingSettings.Default;

            var context = new SequenceContext(
                macro,
                bindingsFile,
                runWatermark,
                timing,
                progress,
                ReleaseInjectionLock,
                runToken,
                cancellationToken);

            var stopped = await ExecuteSequenceAsync(macro.Steps, tailMightInject: false, cursor, context).ConfigureAwait(false);
            return stopped ?? MacroRunResult.Success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Reached when the stop arrived during something that DOES
            // observe the token - a wait/waitFor/waitForEdge step, or the
            // inter-press gap between two repeats of one press step. All of
            // those are points where nothing is held down. The filter keeps
            // a caller-cancelled run throwing exactly as it always has.
            return StoppedAt(macro, cursor.Executed);
        }
        finally
        {
            // Both released on every path, cancellation included - a stop
            // that left either one set would wedge the runner permanently,
            // which is the bug class this area has spent two versions
            // fixing.
            ReleaseInjectionLock();

            // The single place a run ends - success, abort, the commander
            // stopping it, or an exception on the way out. The button that
            // lit when this run started goes dark here and nowhere else:
            // clearing it in each of those branches instead is how one of
            // them would eventually be missed and leave a button lit over a
            // macro that finished minutes ago.
            if (_runningMacroIds.TryRemove(macro.Id, out _))
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// The result a run reports when the commander stopped it by pressing
    /// the same button again, plus the one log line that records where it
    /// got to. <c>Warn</c>, not <c>Info</c>: nothing failed, but a macro
    /// that stopped part-way has left the game somewhere the macro's author
    /// never intended (a half-run <c>request-docking</c> leaves the left
    /// panel open on Contacts), and that is worth finding in a log later.
    /// </summary>
    private MacroRunResult StoppedAt(MacroDefinition macro, int index)
    {
        _log.Warn(
            "Macro",
            $"Macro '{macro.Id}' stopped at step {index} - the same button was pressed again.",
            "The run stopped where it was; whatever it had already pressed stands. No further steps ran.");

        return MacroRunResult.Cancelled(macro.Id);
    }

    /// <summary>
    /// How many steps have executed so far, at any depth. A shared, mutable
    /// cursor rather than a loop variable because <see cref="BranchStep"/>
    /// means a run no longer walks one flat list - see
    /// <see cref="MacroStepProgress"/> for what the number means to a
    /// consumer.
    /// </summary>
    private sealed class StepCursor
    {
        public int Executed;
    }

    /// <summary>
    /// Everything <see cref="ExecuteSequenceAsync"/> needs that is fixed for
    /// the whole run, bundled so the recursion carries two arguments of its
    /// own (the steps, and whether anything after them might still inject)
    /// rather than eleven.
    /// </summary>
    private sealed record SequenceContext(
        MacroDefinition Macro,
        BindingsFile Bindings,
        JournalWatermark RunWatermark,
        MacroTimingSettings Timing,
        IProgress<MacroStepProgress>? Progress,
        Action ReleaseInjectionLock,
        CancellationToken RunToken,
        CancellationToken CallerToken);

    /// <summary>
    /// Runs one sequence of steps - the macro's own list, or one arm of a
    /// <see cref="BranchStep"/>. Returns <see langword="null"/> when the
    /// sequence ran to its end, or the <see cref="MacroRunResult"/> the whole
    /// run must report (a stop, or an abort) when it did not.
    ///
    /// <b>The injection lock, phrased per position.</b> The old flat loop
    /// computed the last injecting index once and released the lock when it
    /// reached it. That is not expressible over a branch, because only one
    /// arm ever runs. The equivalent per-position statement is: after an
    /// injecting step, release if nothing at or after the next position -
    /// <em>including</em> everything the enclosing sequences have left, which
    /// is what <paramref name="tailMightInject"/> carries in - can ever
    /// inject. For an ordinary flat macro this releases at exactly the step
    /// the old code did.
    ///
    /// The one place conservatism survives is <see cref="MightInject"/>'s
    /// treatment of a branch not yet reached: both arms count, because which
    /// one will run is not knowable yet. It disappears the moment the branch
    /// is actually resolved - the taken arm is recursed into with the real
    /// tail, and the untaken arm stops mattering. A global upper bound
    /// computed once over both arms was considered and rejected: this file's
    /// own history (the 2026-09-09 "one lock and one switch" rework) exists
    /// because over-holding this lock blocked every other macro for 198
    /// seconds live, and extending a hold to cover an arm that never runs
    /// would reintroduce exactly that for nothing.
    /// </summary>
    private async Task<MacroRunResult?> ExecuteSequenceAsync(
        IReadOnlyList<MacroStep> steps,
        bool tailMightInject,
        StepCursor cursor,
        SequenceContext context)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            // Step boundary: the safest place there is to stop, because
            // no key is down here. A cancel that lands mid-chord is
            // deliberately not acted on until the chord has finished
            // releasing (see PressChordOnceAsync) - a stranded modifier
            // in the commander's game is worse than any macro misfire.
            if (context.RunToken.IsCancellationRequested)
            {
                // The caller's own token keeps its old semantics: it
                // throws out of RunAsync, as MacroRunner has always done
                // and as WaitForEdgeStep_IsCancellable_WhileWaiting pins.
                // Only a second press of this macro's own button reports
                // a result.
                context.CallerToken.ThrowIfCancellationRequested();
                return StoppedAt(context.Macro, cursor.Executed);
            }

            var step = steps[i];

            // Everything still to come after THIS step: the rest of this
            // sequence, plus whatever the enclosing sequences have left.
            var restMightInject = tailMightInject || MightInject(steps, i + 1);

            var index = cursor.Executed;
            var kind = StepKind(step);
            var conditionDescription = ConditionDescription(step);
            var start = _clock.GetUtcNow();

            if (step is BranchStep branch)
            {
                var (takeThen, armReason) = EvaluateBranch(branch);
                var armSteps = takeThen ? branch.Then : branch.Else;
                var armName = takeThen ? "then" : "else";
                var branchElapsed = _clock.GetUtcNow() - start;
                cursor.Executed++;

                // A condition test cannot time out, so a branch always
                // succeeds - the question is only which way it went. One
                // progress row all the same, for run-result-sheet symmetry
                // with require/waitFor.
                context.Progress?.Report(new MacroStepProgress(index, kind, MacroStepOutcome.Succeeded, branchElapsed));
                _log.Write(new DiagnosticEvent(
                    _clock.GetUtcNow(),
                    DiagnosticLevel.Info,
                    "Macro",
                    $"Macro '{context.Macro.Id}' step {index} ({kind}): {MacroStepOutcome.Succeeded}",
                    $"condition={conditionDescription}, elapsed={branchElapsed.TotalMilliseconds}ms, arm={armName} ({armReason})"));

                // Now that the arm is known, the conservatism above is over:
                // if neither the arm nor anything after it can press
                // anything, the keyboard is not needed again and is handed
                // back here rather than at the end of the run.
                if (!restMightInject && !MightInject(armSteps, 0))
                {
                    context.ReleaseInjectionLock();
                }

                // tailMightInject for the arm is restMightInject, which
                // already folds in everything this list has left after the
                // branch AND everything above it - so an injecting step
                // written after the branch at the outer level correctly keeps
                // the lock held through the arm.
                var armResult = await ExecuteSequenceAsync(armSteps, restMightInject, cursor, context).ConfigureAwait(false);
                if (armResult is not null)
                {
                    return armResult;
                }

                continue;
            }

            var (outcome, reason) = await ExecuteStepAsync(step, context.Bindings, context.RunWatermark, context.Timing, context.RunToken).ConfigureAwait(false);
            var elapsed = _clock.GetUtcNow() - start;
            cursor.Executed++;

            if (IsInjectingStep(step) && !restMightInject)
            {
                context.ReleaseInjectionLock();
            }

            context.Progress?.Report(new MacroStepProgress(index, kind, outcome, elapsed));

            var detail = $"condition={conditionDescription}, elapsed={elapsed.TotalMilliseconds}ms" +
                (outcome == MacroStepOutcome.Failed ? $", reason={reason}" : string.Empty);
            _log.Write(new DiagnosticEvent(
                _clock.GetUtcNow(),
                outcome == MacroStepOutcome.Failed ? DiagnosticLevel.Warn : DiagnosticLevel.Info,
                "Macro",
                $"Macro '{context.Macro.Id}' step {index} ({kind}): {outcome}",
                detail));

            if (outcome == MacroStepOutcome.Failed)
            {
                var abortReason = reason ?? "unknown reason";
                _log.Warn("Macro", $"Macro '{context.Macro.Id}' aborted at step {index}.", abortReason);
                return MacroRunResult.Aborted(index, abortReason);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether anything at or after <paramref name="fromIndex"/> could ever
    /// emit a press. A <see cref="BranchStep"/> counts when <em>either</em>
    /// arm could - a structural fact, computable without running anything,
    /// and the only conservatism in the whole scheme (see
    /// <see cref="ExecuteSequenceAsync"/>). Recursion is bounded by the
    /// grammar's one-level nesting cap, but written to recurse rather than to
    /// assume a depth, so raising that cap later does not silently produce a
    /// wrong answer here.
    /// </summary>
    private static bool MightInject(IReadOnlyList<MacroStep> steps, int fromIndex)
    {
        for (var i = fromIndex; i < steps.Count; i++)
        {
            if (steps[i] is BranchStep branch)
            {
                if (MightInject(branch.Then, 0) || MightInject(branch.Else, 0))
                {
                    return true;
                }

                continue;
            }

            if (IsInjectingStep(steps[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Which arm a <see cref="BranchStep"/> takes, decided NOW against
    /// whatever snapshot <see cref="GameStateStore.Current"/> currently holds
    /// - the same mechanism <see cref="ExecuteRequire"/> uses, deliberately,
    /// so a <c>branch</c> and a <c>require</c> on the same tokens can never
    /// disagree about what the game looks like.
    ///
    /// A <see langword="null"/> snapshot (the game not running yet) takes the
    /// <c>else</c> arm and logs a <c>WARN</c> naming why - "unknown counts as
    /// the negative case", the rule <c>Condition</c>/<c>ConditionList</c>
    /// already apply internally rather than a new one. Nothing is refused:
    /// the macro still runs, just down the arm written for "not that state".
    /// </summary>
    private (bool TakeThen, string Reason) EvaluateBranch(BranchStep step)
    {
        var snapshot = _gameState.Current;
        if (snapshot is null)
        {
            _log.Warn("Macro",
                $"branch [{string.Join(",", step.ConditionTokens)}] could not be evaluated (no snapshot yet).",
                "Taking the 'else' arm: the game's state is not known, and unknown counts as the negative case here - the same rule require already reports.");
            return (false, "no snapshot yet");
        }

        return step.Conditions.Evaluate(snapshot)
            ? (true, "condition met")
            : (false, "condition not met");
    }

    /// <summary>
    /// Whether <paramref name="step"/>'s kind ever emits a key press. This is
    /// a static property of the step's KIND, not of what happens to occur at
    /// run time - a <see cref="GotoLeftPanelTabStep"/> that turns out to need
    /// zero presses (already on the target tab) still counts, because
    /// whether a step KIND can press anything is a structural fact and must
    /// not depend on data only known once the run is under way.
    ///
    /// <b>A <see cref="BranchStep"/> answers <see langword="false"/> here,
    /// and that is not the whole story</b> - the branch itself presses
    /// nothing, but its arms may. Only <see cref="MightInject"/> knows that,
    /// which is why every decision about the injection lock goes through that
    /// method and never through this one directly.
    /// </summary>
    private static bool IsInjectingStep(MacroStep step) => step switch
    {
        PressStep => true,
        PressKeyStep => true,
        PressUntilStep => true,
        GotoLeftPanelTabStep => true,
        WaitStep => false,
        RequireStep => false,
        WaitForStep => false,
        WaitForEdgeStep => false,
        BranchStep => false,
        _ => false
    };

    private async Task<(MacroStepOutcome Outcome, string? Reason)> ExecuteStepAsync(
        MacroStep step,
        BindingsFile bindingsFile,
        JournalWatermark runWatermark,
        MacroTimingSettings timing,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case PressStep press:
                return await ExecutePressAsync(press, bindingsFile, timing, cancellationToken).ConfigureAwait(false);

            case PressKeyStep pressKey:
                return await ExecutePressKeyAsync(pressKey, timing, cancellationToken).ConfigureAwait(false);

            case WaitStep wait:
                await DelayAsync(wait.Duration, cancellationToken).ConfigureAwait(false);
                return (MacroStepOutcome.Succeeded, null);

            case RequireStep require:
                return ExecuteRequire(require);

            case WaitForStep waitFor:
                var met = await _gameState.WaitForAsync(
                    s => s is not null && waitFor.Conditions.Evaluate(s),
                    waitFor.Timeout,
                    cancellationToken).ConfigureAwait(false);
                return met
                    ? (MacroStepOutcome.Succeeded, null)
                    : (MacroStepOutcome.Failed, $"waitFor [{string.Join(",", waitFor.ConditionTokens)}] timed out after {waitFor.Timeout.TotalMilliseconds}ms.");

            case PressUntilStep pressUntil:
                return await ExecutePressUntilAsync(pressUntil, bindingsFile, runWatermark, timing, cancellationToken).ConfigureAwait(false);

            case WaitForEdgeStep waitForEdge:
                return await ExecuteWaitForEdgeAsync(waitForEdge, runWatermark, cancellationToken).ConfigureAwait(false);

            case GotoLeftPanelTabStep gotoTab:
                return await ExecuteGotoLeftPanelTabAsync(gotoTab, bindingsFile, timing, cancellationToken).ConfigureAwait(false);

            // BranchStep deliberately absent: it does not execute a step, it
            // chooses a sequence, and ExecuteSequenceAsync handles it before
            // ever reaching here. Reaching this case would mean a branch was
            // dispatched as an ordinary step, which is a bug worth throwing
            // for rather than silently no-opping.
            default:
                throw new InvalidOperationException($"Unhandled macro step type '{step.GetType().Name}'.");
        }
    }

    /// <summary>
    /// Evaluated once, right now, against whatever snapshot
    /// <see cref="GameStateStore.Current"/> currently holds.
    ///
    /// <b>Reversed 2026-09-12:</b> this step used to fail the whole macro
    /// (sending no keys) when the condition wasn't satisfied - see
    /// <see cref="RequireStep"/>'s remarks. It now always succeeds, per the
    /// same governing decision already applied to
    /// <see cref="ExecuteGotoLeftPanelTabAsync"/> on 2026-09-08: a macro must
    /// never be blocked by uncertainty about the game's state. When the
    /// condition is not satisfied - including a <see langword="null"/>
    /// snapshot, the game not running yet - this logs a <c>WARN</c> naming
    /// the unsatisfied tokens and what was actually observed before
    /// proceeding, so a wrong outcome stays explainable rather than
    /// mysterious.
    /// </summary>
    private (MacroStepOutcome, string?) ExecuteRequire(RequireStep step)
    {
        var currentSnapshot = _gameState.Current;
        var satisfiedNow = currentSnapshot is not null && step.Conditions.Evaluate(currentSnapshot);
        if (!satisfiedNow)
        {
            var observed = currentSnapshot is null ? "no snapshot yet" : "condition not met";
            _log.Warn("Macro",
                $"require [{string.Join(",", step.ConditionTokens)}] was not satisfied ({observed}).",
                "Proceeding anyway: a macro must never be blocked by uncertainty about the game's state.");
        }

        return (MacroStepOutcome.Succeeded, null);
    }

    private async Task<(MacroStepOutcome, string?)> ExecutePressAsync(PressStep step, BindingsFile bindingsFile, MacroTimingSettings timing, CancellationToken cancellationToken)
    {
        var resolution = BindResolver.Resolve(bindingsFile, step.Action);
        if (!resolution.IsBound)
        {
            return (MacroStepOutcome.Failed, $"action '{step.Action}' is not bound ({resolution.Reason}).");
        }

        // Precedence, pinned by test (ref/docs/macro-timing.md's "Defaults
        // and compatibility"): a per-step holdMs beats the commander's
        // setting, which beats the shipped default - exactly what
        // MacroTimingSettings.Default already models for the second half of
        // that chain, so `timing.HoldDuration` alone is correct whether or
        // not anything is actually stored.
        var hold = step.Hold ?? timing.HoldDuration;
        for (var i = 0; i < step.Repeat; i++)
        {
            // No cancellation check here on purpose: a stop that lands
            // between two repeats is picked up by the inter-press gap's own
            // cancellable delay at the bottom of this loop, which is a point
            // where nothing is held down. That gap is always at least 1ms
            // (MacroTimingSettings.MinMs, enforced on the way in AND on the
            // way out of MacroTimingSettingsStore.Load), so it can never
            // short-circuit past the token the way a zero delay would.
            //
            // The shared decision (LunaPanel.Core.GameState.PanelTabPressEffect)
            // both this method and the server's single-tap PlainPresser call,
            // so a plain press and a macro step can never drift about what
            // FocusLeftPanel/CycleNextPanel/CyclePreviousPanel mean for the
            // tracker (ref/docs/panel-tab-tracking.md's "Fix 6"). Armed
            // BEFORE the key goes down, not after - required for
            // FocusLeftPanel's toggle credit (the tracker's own edge
            // handling only works if it knows a toggle is "ours" before the
            // resulting GuiFocus edge can possibly arrive) and harmless for
            // the other two, which Arm() never acts on speculatively anyway.
            var pending = PanelTabPressEffect.Arm(_tabTracker, step.Action);

            await PressChordOnceAsync(resolution.Chord!, hold).ConfigureAwait(false);

            // MacroRunner has no way to observe whether a press was actually
            // sent - IKeyInjector.KeyDown/KeyUp are void (see ChordPresser's
            // own remarks on exactly why it exists instead of routing a
            // single press through this class) - so it always resolves as
            // sent. This is the same blind optimism this method already had
            // for FocusLeftPanel before the extraction, now shared with
            // CycleNextPanel/CyclePreviousPanel too.
            pending.Resolve(_tabTracker, sent: true);

            if (i < step.Repeat - 1)
            {
                await DelayAsync(timing.InterPressGap, cancellationToken).ConfigureAwait(false);
            }
        }

        return (MacroStepOutcome.Succeeded, null);
    }

    /// <summary>
    /// Presses the physical key <see cref="PressKeyStep.Key"/> names,
    /// resolved directly through <see cref="Scancodes.TryGet"/> - no
    /// <see cref="BindResolver"/> involved, since this step names a key, not
    /// a Frontier action. <see cref="Scancodes.TryGet"/> failing is
    /// defensive only (the client only ever sends a name straight out of
    /// <see cref="Scancodes.All"/>, and <see cref="MacroDefinition.Parse"/>
    /// already rejects an unrecognized key at load time) - fails this one
    /// step clearly rather than throwing out of the run.
    ///
    /// No <see cref="PanelTabPressEffect"/> arming here, unlike
    /// <see cref="ExecutePressAsync"/>: that mechanism keys off a Frontier
    /// action NAME (<c>FocusLeftPanel</c>/<c>CycleNextPanel</c>/
    /// <c>CyclePreviousPanel</c>) to credit the tab tracker, and a raw key
    /// press carries no action name to match against - there is nothing here
    /// for the tracker to attribute. A commander who wants tab-tracked panel
    /// navigation already has <c>gotoLeftPanelTab</c>/<c>press</c> for that;
    /// this step is for a key with no Elite meaning at all.
    /// </summary>
    private async Task<(MacroStepOutcome, string?)> ExecutePressKeyAsync(PressKeyStep step, MacroTimingSettings timing, CancellationToken cancellationToken)
    {
        if (!Scancodes.TryGet(step.Key, out var info))
        {
            return (MacroStepOutcome.Failed, $"key '{step.Key}' is not a recognized physical key.");
        }

        var chord = new ResolvedChord(info, Array.Empty<ScancodeInfo>(), BindResolver.PrettifyKeyName(step.Key));
        var hold = step.Hold ?? timing.HoldDuration;
        for (var i = 0; i < step.Repeat; i++)
        {
            await PressChordOnceAsync(chord, hold).ConfigureAwait(false);

            if (i < step.Repeat - 1)
            {
                await DelayAsync(timing.InterPressGap, cancellationToken).ConfigureAwait(false);
            }
        }

        return (MacroStepOutcome.Succeeded, null);
    }

    private async Task<(MacroStepOutcome, string?)> ExecutePressUntilAsync(
        PressUntilStep step,
        BindingsFile bindingsFile,
        JournalWatermark runWatermark,
        MacroTimingSettings timing,
        CancellationToken cancellationToken)
    {
        var resolution = BindResolver.Resolve(bindingsFile, step.Action);
        if (!resolution.IsBound)
        {
            return (MacroStepOutcome.Failed, $"action '{step.Action}' is not bound ({resolution.Reason}).");
        }

        for (var attempt = 1; attempt <= step.MaxAttempts; attempt++)
        {
            await PressChordOnceAsync(resolution.Chord!, timing.HoldDuration).ConfigureAwait(false);

            // A stop between attempts is picked up by this wait, which
            // observes the token itself (GameStateStore.WaitForAsync throws,
            // and WaitForJournalConditionAsync's own ThrowIfCancellationRequested
            // mirrors it) - and does so with nothing held down, the chord
            // above having fully released.
            //
            // Exactly one of step.JournalCondition/step.Conditions is set -
            // validated at load by MacroDefinition.ParsePressUntil - so
            // exactly one branch below ever runs for a given step.
            var satisfied = step.JournalCondition is not null
                ? await WaitForJournalConditionAsync(step.JournalCondition, runWatermark, step.Timeout, cancellationToken).ConfigureAwait(false)
                : await _gameState.WaitForAsync(
                    s => s is not null && step.Conditions!.Evaluate(s),
                    step.Timeout,
                    cancellationToken).ConfigureAwait(false);

            if (satisfied)
            {
                return (MacroStepOutcome.Succeeded, null);
            }
        }

        var gateDescription = step.JournalCondition is not null
            ? $"{EdgeCondition.Prefix}{step.JournalCondition.EventName}"
            : string.Join(",", step.ConditionTokens!);
        return (MacroStepOutcome.Failed,
            $"gave up after {step.MaxAttempts} attempt(s) waiting for [{gateDescription}].");
    }

    /// <summary>
    /// Waits, bounded by <paramref name="timeout"/>, for
    /// <paramref name="condition"/> to become true since
    /// <paramref name="runWatermark"/> - one attempt's own per-attempt time
    /// slice within <see cref="ExecutePressUntilAsync"/>'s retry loop, not
    /// the whole step. Deadline-loop-against-<see cref="JournalStateStore.Changed"/>
    /// shape mirrors <see cref="ExecuteWaitForEdgeAsync"/>'s exactly (same
    /// subscribe-before-check ordering, same remaining-time recomputed each
    /// iteration from the injected <see cref="TimeProvider"/>), narrowed to
    /// one condition and a <see langword="bool"/> return since this caller
    /// has no fail-on and reports its own failure text.
    /// </summary>
    private async Task<bool> WaitForJournalConditionAsync(
        EdgeCondition condition,
        JournalWatermark runWatermark,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Captured BEFORE checking, not after - see
            // ExecuteWaitForEdgeAsync's own remarks on why: an edge landing
            // in the gap between the check and the subscribe would otherwise
            // be missed for the rest of this iteration's wait.
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(JournalEvent _) => signal.TrySetResult();
            _journal.Changed += OnChanged;
            try
            {
                if (condition.Evaluate(_journal, runWatermark))
                {
                    return true;
                }

                var remaining = deadline - _clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = Task.Delay(remaining, _clock, iterationCts.Token);
                await Task.WhenAny(signal.Task, delayTask).ConfigureAwait(false);
                iterationCts.Cancel();
            }
            finally
            {
                _journal.Changed -= OnChanged;
            }
        }
    }

    /// <summary>
    /// Waits until any of <see cref="WaitForEdgeStep.SucceedOn"/> has been
    /// seen since <paramref name="runWatermark"/>, or aborts as soon as any
    /// of <see cref="WaitForEdgeStep.FailOn"/> has, or <see cref="WaitForEdgeStep.Timeout"/>
    /// elapses - whichever comes first (O28: bounded 2026-09-08, see that
    /// property's remarks and <see cref="WaitForEdgeStep"/>'s for why).
    /// Deadline arithmetic mirrors <see cref="GameStateStore.WaitForAsync"/>'s
    /// own - a deadline computed once up front, and the remaining time
    /// recomputed from the injected <see cref="TimeProvider"/> each time
    /// around the loop, never a fixed per-iteration delay. Expressed over
    /// <see cref="JournalStateStore.Changed"/> rather than <c>WaitForEdgeAsync</c>
    /// itself, because that method takes exactly one <see cref="EdgeCondition"/>
    /// and this step needs to race several.
    /// </summary>
    private async Task<(MacroStepOutcome, string?)> ExecuteWaitForEdgeAsync(
        WaitForEdgeStep step,
        JournalWatermark runWatermark,
        CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + step.Timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Captured BEFORE checking, not after - the same ordering
            // JournalStateStore.WaitForEdgeAsync itself uses and explains:
            // an edge landing in the gap between the check and the subscribe
            // would otherwise be missed for the rest of this iteration's wait.
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(JournalEvent _) => signal.TrySetResult();
            _journal.Changed += OnChanged;
            try
            {
                for (var i = 0; i < step.SucceedOn.Count; i++)
                {
                    if (step.SucceedOn[i].Evaluate(_journal, runWatermark))
                    {
                        return (MacroStepOutcome.Succeeded, null);
                    }
                }

                for (var i = 0; i < step.FailOn.Count; i++)
                {
                    if (step.FailOn[i].Evaluate(_journal, runWatermark))
                    {
                        var detail = step.FailureDetailField is not null
                            ? _journal.LastEvent(step.FailOn[i].EventName)?.String(step.FailureDetailField)
                            : null;
                        var reason = detail is not null
                            ? $"{step.FailOnTokens[i]} ({step.FailureDetailField}={detail})"
                            : step.FailOnTokens[i];
                        return (MacroStepOutcome.Failed, $"waitForEdge: {reason}.");
                    }
                }

                var remaining = deadline - _clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return (MacroStepOutcome.Failed,
                        $"waitForEdge [{string.Join(",", step.SucceedOnTokens)}] timed out after {step.Timeout.TotalMilliseconds}ms - " +
                        "no grant or denial arrived within the timeout, so the request may not have been made. Check the panel.");
                }

                using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = Task.Delay(remaining, _clock, iterationCts.Token);
                await Task.WhenAny(signal.Task, delayTask).ConfigureAwait(false);
                iterationCts.Cancel();
            }
            finally
            {
                _journal.Changed -= OnChanged;
            }
        }
    }

    /// <summary>
    /// Presses <see cref="PanelTabTracker.AdvanceTabAction"/> exactly as
    /// many times as <see cref="PanelTabTracker.PressesToReach"/> says are
    /// needed to reach <see cref="GotoLeftPanelTabStep.Target"/> - zero when
    /// already there, which sends no keys at all.
    ///
    /// <b>Reversed 2026-09-08:</b> this step used to fail the whole macro
    /// (sending no keys) when the tracker didn't know the current tab - see
    /// <see cref="GotoLeftPanelTabStep"/>'s remarks. It now always computes a
    /// route from <see cref="PanelTabTracker.CurrentTab"/> and presses it,
    /// per the governing decision that a macro must never be blocked by
    /// uncertainty about the panel. When
    /// <see cref="PanelTabTracker.LeftTabConfidence"/> is
    /// <see cref="TabConfidence.Low"/>, this logs a <c>WARN</c> naming the
    /// believed tab before pressing anything - the risk is accepted, but a
    /// wrong outcome must stay explainable rather than mysterious.
    /// </summary>
    private async Task<(MacroStepOutcome, string?)> ExecuteGotoLeftPanelTabAsync(
        GotoLeftPanelTabStep step,
        BindingsFile bindingsFile,
        MacroTimingSettings timing,
        CancellationToken cancellationToken)
    {
        var believedTab = _tabTracker.CurrentTab;
        if (_tabTracker.LeftTabConfidence == TabConfidence.Low)
        {
            _log.Warn("Macro",
                $"gotoLeftPanelTab: acting on a LOW-confidence belief that the left panel is on {believedTab} (target {step.Target}).",
                "An uncredited GuiFocus edge was observed since this was last confirmed - the commander may have opened or closed the left panel by hand. Proceeding anyway: a macro must never be blocked by uncertainty about the panel.");
        }

        var presses = _tabTracker.PressesToReach(step.Target);

        var resolution = BindResolver.Resolve(bindingsFile, PanelTabTracker.AdvanceTabAction);
        if (presses > 0 && !resolution.IsBound)
        {
            return (MacroStepOutcome.Failed, $"action '{PanelTabTracker.AdvanceTabAction}' is not bound ({resolution.Reason}).");
        }

        for (var i = 0; i < presses; i++)
        {
            await PressChordOnceAsync(resolution.Chord!, timing.HoldDuration).ConfigureAwait(false);

            // Credited immediately after the press it describes and BEFORE
            // the cancellable gap below, so a stopped route leaves the
            // tracker believing exactly the presses that actually landed -
            // the panel's real position, not the one the whole route would
            // have reached. A stopped macro leaves the game somewhere
            // unintended; it must not also leave the belief wrong.
            _tabTracker.RecordOwnTabAdvance();

            if (i < presses - 1)
            {
                await DelayAsync(timing.InterPressGap, cancellationToken).ConfigureAwait(false);
            }
        }

        return (MacroStepOutcome.Succeeded, null);
    }

    /// <summary>
    /// One full chord press: modifiers down, a settling gap (only when the
    /// chord has modifiers), main key down, held for <paramref name="hold"/>,
    /// then everything released in reverse order (main key first, then
    /// modifiers in reverse of the order they went down). This choreography
    /// is exactly what the timing research behind <see cref="MacroTimingDefaults"/>
    /// calls for - see that type's own remarks before changing any of it.
    ///
    /// <b>Takes no <see cref="CancellationToken"/>, on purpose (2026-09-09).</b>
    /// Once a modifier is down, this method's only job is to get it back up:
    /// a run stopped part-way through a chord would leave a key held down in
    /// the commander's game, which is worse than any macro misfire this
    /// project has ever shipped. A chord costs at most a settle gap plus a
    /// hold (250ms at the defaults), so refusing to cut it short delays a
    /// stop imperceptibly - and every point at which this class DOES observe
    /// a stop is one where nothing is held: the step boundary at the top of
    /// <see cref="RunAsync"/>'s loop, the inter-press gap between two
    /// repeats, and any gated step's own wait.
    /// The <c>finally</c> below is the second half of the same guarantee:
    /// it releases exactly what actually went down, in reverse order, even
    /// if the injector itself throws mid-chord.
    /// </summary>
    private async Task PressChordOnceAsync(ResolvedChord chord, TimeSpan hold)
    {
        var modifiersDown = 0;
        var mainKeyDown = false;

        try
        {
            foreach (var modifier in chord.ModifierKeys)
            {
                _injector.KeyDown(modifier);
                modifiersDown++;
            }

            if (chord.ModifierKeys.Count > 0)
            {
                await DelayAsync(MacroTimingDefaults.ModifierSettleGap, CancellationToken.None).ConfigureAwait(false);
            }

            _injector.KeyDown(chord.MainKey);
            mainKeyDown = true;
            await DelayAsync(hold, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (mainKeyDown)
            {
                _injector.KeyUp(chord.MainKey);
            }

            for (var i = modifiersDown - 1; i >= 0; i--)
            {
                _injector.KeyUp(chord.ModifierKeys[i]);
            }
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, _clock, cancellationToken);

    private static string StepKind(MacroStep step) => step switch
    {
        PressStep => "press",
        PressKeyStep => "pressKey",
        WaitStep => "wait",
        RequireStep => "require",
        WaitForStep => "waitFor",
        PressUntilStep => "pressUntil",
        WaitForEdgeStep => "waitForEdge",
        GotoLeftPanelTabStep => "gotoLeftPanelTab",
        BranchStep => "branch",
        _ => "unknown"
    };

    private static string ConditionDescription(MacroStep step) => step switch
    {
        RequireStep r => string.Join(",", r.ConditionTokens),
        WaitForStep w => string.Join(",", w.ConditionTokens),
        PressUntilStep p => p.JournalCondition is not null ? $"{EdgeCondition.Prefix}{p.JournalCondition.EventName}" : string.Join(",", p.ConditionTokens!),
        WaitForEdgeStep e => string.Join(",", e.SucceedOnTokens) + (e.FailOnTokens.Count > 0 ? $" / fail:{string.Join(",", e.FailOnTokens)}" : string.Empty),
        GotoLeftPanelTabStep g => g.Target.ToString(),
        BranchStep b => string.Join(",", b.ConditionTokens),
        _ => "n/a"
    };
}
