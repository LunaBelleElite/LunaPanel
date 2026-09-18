using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Drives <see cref="MacroPresser.RunAsync"/> against the real
/// <see cref="Win32KeyInjector"/> (through its test-seam constructor, same
/// discipline as <see cref="Win32KeyInjectorTests"/>/<see cref="ChordPresserTests"/>)
/// and a real <see cref="MacroRunner"/> with a fake clock - no test here can
/// reach a real Win32 API. This is the guard-before-run discipline
/// <c>ref/docs/injection.md</c> describes: a macro must refuse exactly as a
/// single press does when Elite is not foreground, and the refusal must
/// happen before <see cref="MacroRunner.RunAsync"/> is ever called, not
/// merely before any keystroke lands.
/// </summary>
public class MacroPresserTests
{
    private static ForegroundContext EliteOk() => new("EliteDangerous64", IntegrityLevel.Medium, IntegrityLevel.Medium);
    private static ForegroundContext NotElite() => new("Discord", IntegrityLevel.Medium, IntegrityLevel.Medium);

    // Same fake clock shape as ChordPresserTests/MacroRunnerTests - see
    // either file's remarks (and tests/notes/open-items.md O6) for why
    // CreateTimer must be virtualized and PumpAsync cedes a bounded real
    // wait after each Advance().
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _utcNow;
        private readonly List<PendingTimer> _pending = new();

        /// <summary>
        /// Completed while the code under test is known to be parked on a
        /// timer this clock holds; reset by any <see cref="Advance"/> that
        /// actually fires one. See <c>MacroRunnerTests.FakeTimeProvider</c>
        /// for the full account (O6) - in short, time added while the
        /// awaiting continuation is mid-resumption and holds no timer is
        /// silently lost, and this is how <see cref="PumpAsync"/> waits that
        /// window out instead of guessing at a duration.
        /// </summary>
        private TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeTimeProvider(DateTimeOffset start) => _utcNow = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock) { return _utcNow; }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PendingTimer(this, callback, state, period);
            lock (_lock)
            {
                timer.DueAt = dueTime <= TimeSpan.Zero ? _utcNow : _utcNow + dueTime;
                _pending.Add(timer);
                _armed.TrySetResult();
            }

            return timer;
        }

        /// <summary>
        /// Completes once the code under test is parked on a timer again -
        /// immediately, if nothing has fired since it last armed one.
        /// </summary>
        public Task ArmedAsync()
        {
            lock (_lock) { return _armed.Task; }
        }

        /// <summary>
        /// Moves the clock and fires everything now due, returning HOW MANY
        /// callbacks fired - zero meaning the code under test cannot
        /// possibly have moved.
        /// </summary>
        public int Advance(TimeSpan delta)
        {
            List<PendingTimer> due;
            lock (_lock)
            {
                _utcNow += delta;
                due = _pending.Where(t => !t.Disposed && t.DueAt <= _utcNow).ToList();
                foreach (var timer in due)
                {
                    if (timer.Period == Timeout.InfiniteTimeSpan || timer.Period == TimeSpan.Zero)
                    {
                        _pending.Remove(timer);
                    }
                    else
                    {
                        timer.DueAt += timer.Period;
                    }
                }

                // Reset BEFORE the callbacks run: a continuation that
                // re-arms synchronously from inside InvokeCallback must
                // complete the fresh signal, not the stale one.
                if (due.Count > 0)
                {
                    _armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            foreach (var timer in due)
            {
                timer.InvokeCallback();
            }

            return due.Count;
        }

        private void Remove(PendingTimer timer)
        {
            lock (_lock) { _pending.Remove(timer); }
        }

        private sealed class PendingTimer : ITimer
        {
            private readonly FakeTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public PendingTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Period = period;
            }

            public DateTimeOffset DueAt { get; set; }
            public TimeSpan Period { get; }
            public bool Disposed { get; private set; }

            public void InvokeCallback()
            {
                if (!Disposed) _callback(_state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
                Disposed = true;
                _owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// Waits until the code under test is parked on a timer this fake clock
    /// holds, and only then advances it. <b>Rewritten 2026-09-17 (O6)</b>
    /// from an advance followed by a blind, fixed <c>Task.Delay(50)</c> of
    /// real time - see <c>MacroRunnerTests.PumpAsync</c> for the full
    /// account and for the pin
    /// (<c>PumpAsync_DoesNotAdvancePastAContinuationThatHasNotReArmedYet</c>)
    /// that holds the first barrier below in place. An advance issued while
    /// the code under test is mid-resumption is not merely early, it is
    /// lost, and the run then hangs until its guard fires - which is what
    /// this file's own
    /// <c>MacroRunner_TwoModifierChord_ReleasesModifiers_InReverseOrder</c>
    /// did, intermittently, on a full-suite run.
    /// </summary>
    private static async Task PumpAsync(FakeTimeProvider clock, TimeSpan advanceBy)
    {
        // Barrier one, and the load-bearing one: never advance into the gap
        // where the code under test holds no timer.
        await clock.ArmedAsync().WaitAsync(PumpArmGuard);

        if (clock.Advance(advanceBy) == 0)
        {
            // A deliberate partial advance - nothing was due, so nothing
            // resumed, and there is no settling to wait for either.
            return;
        }

        // Barrier two, best-effort: let what this advance released become
        // observable, for assertions made between two pumps rather than
        // after the awaited task. Falls through only on the advance that
        // finishes the run, where no next timer is coming.
        try
        {
            await clock.ArmedAsync().WaitAsync(PumpSettleGuard);
        }
        catch (TimeoutException)
        {
        }
    }

    /// <summary>
    /// Hang guard on <see cref="PumpAsync"/>'s wait to re-arm, not a margin:
    /// a passing test spends microseconds here.
    /// </summary>
    private static readonly TimeSpan PumpArmGuard = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound on <see cref="PumpAsync"/>'s post-advance settle, paid in
    /// full only on the advance that completes a run.
    /// </summary>
    private static readonly TimeSpan PumpSettleGuard = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Upper bound for every <c>await</c> on a <see cref="MacroPresser.RunAsync"/>
    /// task in this file - same rationale and value as
    /// <c>MacroRunnerTests.RunHangGuard</c> (O27).
    /// </summary>
    private static readonly TimeSpan RunHangGuard = TimeSpan.FromSeconds(10);

    private sealed class RecordingSender
    {
        public List<NativeMethods.INPUT> Sent { get; } = new();

        public (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
        {
            Sent.AddRange(inputs);
            return (1u, 0);
        }
    }

    private static BindingsFile BuildBindings(params (string Action, string Key)[] bindings)
    {
        var elements = string.Concat(bindings.Select(b =>
            $"<{b.Action}><Primary Device=\"Keyboard\" Key=\"{b.Key}\" /></{b.Action}>"));
        var xml = $"<Root MajorVersion=\"4\" MinorVersion=\"2\">{elements}</Root>";
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    private static MacroDefinition MacroWithSteps(params MacroStep[] steps) => new("test-macro", "Test Macro", steps);

    [Fact]
    public async Task RunAsync_GuardRefuses_ReturnsGameNotForeground_AndNeverRunsTheMacro()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);
        var gameState = new GameStateStore(clock);
        var runner = new MacroRunner(injector, gameState, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));

        // If MacroPresser mistakenly ran the macro despite the guard's
        // refusal, this RequireStep would fail it as "Aborted" (Docked is
        // never set) rather than the "Sent" a bound PressStep alone would
        // reach - either way, this proves RunAsync never got past the guard
        // check by the OUTCOME it returns, not merely by inspecting state.
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var result = await MacroPresser.RunAsync(injector, runner, macro, bindings).WaitAsync(RunHangGuard);

        Assert.False(result.Fired);
        Assert.Equal("GameNotForeground", result.Outcome);
        Assert.Contains("active window", result.Reason);
        Assert.Null(result.FailedStepIndex);
        Assert.Empty(sender.Sent);
        // The guard refused before RunAsync was ever called - no step ran.
        Assert.Empty(result.Steps);
    }

    [Fact]
    public async Task RunAsync_GuardRefuses_UipiSuspected_ReturnsThatOutcome()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        ForegroundContext Context() => new("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);
        var gameState = new GameStateStore(clock);
        var runner = new MacroRunner(injector, gameState, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var result = await MacroPresser.RunAsync(injector, runner, macro, bindings).WaitAsync(RunHangGuard);

        Assert.False(result.Fired);
        Assert.Equal("UipiSuspected", result.Outcome);
        Assert.Contains("UIPI", result.Reason);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task RunAsync_GuardPasses_RunsTheMacroToSuccess_ReturnsSentAndFired()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var gameState = new GameStateStore(clock);
        var runner = new MacroRunner(injector, gameState, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = MacroPresser.RunAsync(injector, runner, macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.True(result.Fired);
        Assert.Equal("Sent", result.Outcome);
        Assert.Null(result.Reason);
        Assert.Equal(2, sender.Sent.Count); // one down, one up
        var step = Assert.Single(result.Steps);
        Assert.Equal(0, step.StepIndex);
        Assert.Equal(MacroStepOutcome.Succeeded, step.Outcome);
    }

    /// <summary>
    /// The per-step read-back <c>ref/docs/macro-builder.md</c>'s "What the
    /// builder shows after a run" describes, for the run that goes all the
    /// way through: every step MacroRunner executed appears, in order, with
    /// its own outcome. Two bound presses so both steps succeed and the run
    /// completes.
    /// </summary>
    [Fact]
    public async Task RunAsync_GuardPasses_MultiStepMacroSucceeds_ReturnsOneStepProgressPerStep()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var gameState = new GameStateStore(clock);
        var runner = new MacroRunner(injector, gameState, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"), ("OtherAction", "Key_S"));
        var macro = MacroWithSteps(
            new PressStep("TestAction", Repeat: 1, Hold: null),
            new PressStep("OtherAction", Repeat: 1, Hold: null));

        var runTask = MacroPresser.RunAsync(injector, runner, macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.True(result.Fired);
        Assert.Equal("Sent", result.Outcome);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(0, result.Steps[0].StepIndex);
        Assert.Equal(MacroStepOutcome.Succeeded, result.Steps[0].Outcome);
        Assert.Equal(1, result.Steps[1].StepIndex);
        Assert.Equal(MacroStepOutcome.Succeeded, result.Steps[1].Outcome);
    }

    /// <summary>
    /// Same read-back, for the run that aborts partway: the failed step's own
    /// progress is included (as <see cref="MacroStepOutcome.Failed"/>), and
    /// nothing beyond it - the run stopped there.
    /// </summary>
    [Fact]
    public async Task RunAsync_GuardPasses_MacroAbortsPartway_ReturnsStepsUpToAndIncludingTheFailure()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var gameState = new GameStateStore(clock);
        var runner = new MacroRunner(injector, gameState, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        // Second step names an action with no binding at all - fails
        // immediately, no wait needed to reach it deterministically.
        var macro = MacroWithSteps(
            new PressStep("TestAction", Repeat: 1, Hold: null),
            new PressStep("Unbound", Repeat: 1, Hold: null));

        var runTask = MacroPresser.RunAsync(injector, runner, macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.False(result.Fired);
        Assert.Equal("MacroAborted", result.Outcome);
        Assert.Equal(1, result.FailedStepIndex);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(MacroStepOutcome.Succeeded, result.Steps[0].Outcome);
        Assert.Equal(1, result.Steps[1].StepIndex);
        Assert.Equal(MacroStepOutcome.Failed, result.Steps[1].Outcome);
    }

    [Fact]
    public async Task RunAsync_GuardPasses_RequireStepUnsatisfied_FiresAnywayWithWarning()
    {
        // RequireStep no longer aborts a run (2026-09-12, never-gate-a-macro.md)
        // - it logs a WARN and lets the macro proceed to send its keys.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var gameState = new GameStateStore(clock); // Current is null - "Docked" can never be satisfied
        var runner = new MacroRunner(injector, gameState, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new RequireStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }),
            new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = MacroPresser.RunAsync(injector, runner, macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.True(result.Fired);
        Assert.Equal("Sent", result.Outcome);
        Assert.Null(result.Reason);
        Assert.Equal(2, sender.Sent.Count); // one down, one up

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("Docked", warning.Message);
    }

    // ---------------------------------------------------------------
    // MacroPresser.Start (2026-09-17, O28) - the non-blocking entry point
    // POST /api/press now uses. The three synchronous outcomes (guard
    // refused / busy / cancelled) come back as an Immediate result with the
    // Completion task ALREADY complete; a genuine start comes back with no
    // Immediate and a Completion that carries the real outcome and the
    // per-step read-back once the run ends.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Start_GuardRefuses_ReturnsImmediateGameNotForeground_NotStarted_WithCompletionAlreadyDone()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);
        var runner = new MacroRunner(injector, new GameStateStore(clock), new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var start = MacroPresser.Start(injector, runner, macro, bindings);

        Assert.False(start.Started);
        Assert.NotNull(start.Immediate);
        Assert.Equal("GameNotForeground", start.Immediate!.Outcome);
        Assert.False(start.Immediate.Fired);
        Assert.Empty(start.Immediate.Steps);
        // Nothing left running: the completion is already done and says
        // the same thing, so a caller can await it without ever blocking.
        Assert.True(start.Completion.IsCompletedSuccessfully);
        Assert.Same(start.Immediate, await start.Completion);
        Assert.Empty(runner.RunningMacroIds);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Start_DifferentMacroWhileInjecting_ReturnsImmediateBusy_NotStarted()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var runner = new MacroRunner(injector, new GameStateStore(clock), new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"), ("OtherAction", "Key_S"));
        var macroA = new MacroDefinition("macro-a", "A", new[] { new PressStep("TestAction", Repeat: 1, Hold: null) });
        var macroB = new MacroDefinition("macro-b", "B", new[] { new PressStep("OtherAction", Repeat: 1, Hold: null) });

        var startA = MacroPresser.Start(injector, runner, macroA, bindings);
        Assert.True(startA.Started);

        var startB = MacroPresser.Start(injector, runner, macroB, bindings);

        Assert.False(startB.Started);
        Assert.Equal("Busy", startB.Immediate!.Outcome);
        Assert.False(startB.Immediate.Fired);
        Assert.True(startB.Completion.IsCompletedSuccessfully);
        Assert.DoesNotContain("macro-b", runner.RunningMacroIds);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal("Sent", (await startA.Completion.WaitAsync(RunHangGuard)).Outcome);
    }

    [Fact]
    public async Task Start_SameMacroAgain_ReturnsImmediateCancelled_NotStarted_AndTheFirstRunsCompletionReportsStopped()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var runner = new MacroRunner(injector, new GameStateStore(clock), new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 3, Hold: null));

        var first = MacroPresser.Start(injector, runner, macro, bindings);
        Assert.True(first.Started);

        var second = MacroPresser.Start(injector, runner, macro, bindings);

        Assert.False(second.Started);
        Assert.Equal("Cancelled", second.Immediate!.Outcome);
        Assert.Equal("Macro 'test-macro' stopped - you pressed it again.", second.Immediate.Reason);
        Assert.True(second.Completion.IsCompletedSuccessfully);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var firstResult = await first.Completion.WaitAsync(RunHangGuard);

        // The stopped run's OWN completion carries the stop and what ran
        // before it - this is what the live channel later shows.
        Assert.Equal("Cancelled", firstResult.Outcome);
        Assert.False(firstResult.Fired);
        Assert.Equal(2, sender.Sent.Count); // one press down/up, repeats 2 and 3 never happened
    }

    [Fact]
    public async Task Start_GuardPasses_ReturnsStartedWithNoImmediate_AndCompletionCarriesSentWithOneStep()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var runner = new MacroRunner(injector, new GameStateStore(clock), new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var start = MacroPresser.Start(injector, runner, macro, bindings);

        // Started, and NOT finished: the whole point is that the caller
        // holds the answer "it started" while the run is still in flight.
        Assert.True(start.Started);
        Assert.Null(start.Immediate);
        Assert.False(start.Completion.IsCompleted);
        Assert.Contains("test-macro", runner.RunningMacroIds);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await start.Completion.WaitAsync(RunHangGuard);

        Assert.True(result.Fired);
        Assert.Equal("Sent", result.Outcome);
        Assert.Equal(2, sender.Sent.Count);
        var step = Assert.Single(result.Steps);
        Assert.Equal(0, step.StepIndex);
        Assert.Equal(MacroStepOutcome.Succeeded, step.Outcome);
    }

    /// <summary>
    /// The steps list is appended to by the run and read by whoever awaits
    /// <see cref="MacroPresser.MacroPressStart.Completion"/> - under the new
    /// design, a different thread from the one that appended. This pins that
    /// the read-back the completion carries is the complete list up to the
    /// failure, exactly what the blocking path reports, for the abort case
    /// where the count is the whole assertion.
    /// </summary>
    [Fact]
    public async Task Start_GuardPasses_MacroAbortsPartway_CompletionCarriesStepsUpToAndIncludingTheFailure()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var runner = new MacroRunner(injector, new GameStateStore(clock), new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new PressStep("TestAction", Repeat: 1, Hold: null),
            new PressStep("Unbound", Repeat: 1, Hold: null));

        var start = MacroPresser.Start(injector, runner, macro, bindings);
        Assert.True(start.Started);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await start.Completion.WaitAsync(RunHangGuard);

        Assert.False(result.Fired);
        Assert.Equal("MacroAborted", result.Outcome);
        Assert.Equal(1, result.FailedStepIndex);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(MacroStepOutcome.Succeeded, result.Steps[0].Outcome);
        Assert.Equal(MacroStepOutcome.Failed, result.Steps[1].Outcome);
    }

    // ---------------------------------------------------------------
    // MacroPresser.MacroPressResult mapping - pure, no injector/runner
    // needed at all.
    // ---------------------------------------------------------------

    private static readonly IReadOnlyList<MacroStepProgress> NoSteps = Array.Empty<MacroStepProgress>();

    [Fact]
    public void FromRunResult_Success_MapsToSent_AndFired()
    {
        var result = MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Success, NoSteps);

        Assert.True(result.Fired);
        Assert.Equal("Sent", result.Outcome);
        Assert.Null(result.Reason);
        Assert.Null(result.FailedStepIndex);
    }

    [Fact]
    public void FromRunResult_Aborted_MapsToMacroAborted_WithReasonAndStepIndex_NotFired()
    {
        var result = MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Aborted(2, "gave up"), NoSteps);

        Assert.False(result.Fired);
        Assert.Equal("MacroAborted", result.Outcome);
        Assert.Equal("gave up", result.Reason);
        Assert.Equal(2, result.FailedStepIndex);
    }

    [Fact]
    public void FromRunResult_Busy_MapsToBusy_NotFired()
    {
        var result = MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Busy, NoSteps);

        Assert.False(result.Fired);
        Assert.Equal("Busy", result.Outcome);
        Assert.Null(result.FailedStepIndex);
    }

    [Fact]
    public void FromRunResult_Cancelled_MapsToCancelled_NotFired_CarryingTheStoppedReason()
    {
        var result = MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Cancelled("request-docking"), NoSteps);

        // NOT fired: the panel shows Reason as a toast exactly when Fired is
        // false, and "you stopped it" is the one thing the commander needs
        // to see after pressing a running macro's button again.
        Assert.False(result.Fired);
        Assert.Equal("Cancelled", result.Outcome);
        Assert.Equal("Macro 'request-docking' stopped - you pressed it again.", result.Reason);
        Assert.Null(result.FailedStepIndex);
    }

    /// <summary>
    /// The one thing that changed about this mapping when <c>Steps</c> was
    /// added: the collected list is carried through into every outcome's
    /// result unmodified, not just re-derived from the outcome.
    /// </summary>
    [Fact]
    public void FromRunResult_CarriesThroughTheStepsListItWasGiven_ForEveryOutcome()
    {
        var steps = new[] { new MacroStepProgress(0, "press", MacroStepOutcome.Succeeded, TimeSpan.FromMilliseconds(150)) };

        Assert.Same(steps, MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Success, steps).Steps);
        Assert.Same(steps, MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Aborted(0, "x"), steps).Steps);
        Assert.Same(steps, MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Busy, steps).Steps);
        Assert.Same(steps, MacroPresser.MacroPressResult.FromRunResult(MacroRunResult.Cancelled("m"), steps).Steps);
    }

    [Fact]
    public void GuardRefused_MapsOutcomeAndReason_NotFired()
    {
        var guardResult = new InjectionAttemptResult(InjectionOutcome.GameNotForeground, "Elite Dangerous is not the active window. Click on the game, then try again.");

        var result = MacroPresser.MacroPressResult.GuardRefused(guardResult);

        Assert.False(result.Fired);
        Assert.Equal("GameNotForeground", result.Outcome);
        Assert.Equal(guardResult.Reason, result.Reason);
        Assert.Null(result.FailedStepIndex);
        Assert.Empty(result.Steps);
    }
}
