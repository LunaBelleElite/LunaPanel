using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Drives <see cref="PlainPresser.PressAsync"/> - the single-tap counterpart
/// to <c>MacroRunner</c>'s plain <c>press</c> step that also feeds
/// <see cref="PanelTabTracker"/>, closing the hole where a button pressed
/// once (rather than through a macro) never touched the tracker at all
/// (<c>ref/docs/panel-tab-tracking.md</c>'s "Fix 6"). Same test-seam
/// discipline as <see cref="ChordPresserTests"/>: every foreground/send
/// delegate is faked, so no test here can reach a real Win32 API.
/// </summary>
public class PlainPresserTests
{
    private static ForegroundContext EliteOk() => new("EliteDangerous64", IntegrityLevel.Medium, IntegrityLevel.Medium);
    private static ForegroundContext NotElite() => new("Discord", IntegrityLevel.Medium, IntegrityLevel.Medium);

    // Same fake clock shape as ChordPresserTests/MacroPresserTests - see
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
    /// Upper bound for every <c>await</c> on a <see cref="PlainPresser.PressAsync"/>
    /// task in this file - same rationale as <c>MacroRunnerTests.RunHangGuard</c> (O27).
    /// </summary>
    private static readonly TimeSpan PressHangGuard = TimeSpan.FromSeconds(10);

    private sealed class RecordingSender
    {
        public List<NativeMethods.INPUT> Sent { get; } = new();

        public (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
        {
            Sent.AddRange(inputs);
            return (1u, 0);
        }
    }

    private static ResolvedChord Chord(string key)
    {
        Scancodes.TryGet(key, out var info);
        return new ResolvedChord(info, Array.Empty<ScancodeInfo>(), key);
    }

    // -----------------------------------------------------------------
    // FocusLeftPanel: armed before the press, kept on success.
    // -----------------------------------------------------------------

    [Fact]
    public async Task FocusLeftPanel_Sent_KeepsTheCredit_SoTheConfirmingEdgeIsNotMistakenForExternal()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        var pressTask = PlainPresser.PressAsync(injector, Chord("Key_1"), PanelTabTracker.LeftPanelToggleAction, tracker, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);

        // Simulate the confirming GuiFocus edge, as ServerHostBuilder's own
        // subscription would report it.
        tracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // armed - not mistaken for external
    }

    [Fact]
    public async Task FocusLeftPanel_Refused_NotForeground_RevokesTheCredit_SoTheNextEdgeIsExternal()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        var result = await PlainPresser.PressAsync(injector, Chord("Key_1"), PanelTabTracker.LeftPanelToggleAction, tracker, clock)
            .WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);

        // The refused press never reached Elite, so no GuiFocus edge is ever
        // coming for it - the credit must already be gone, not merely
        // waiting to expire.
        tracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    [Fact]
    public async Task FocusLeftPanel_Refused_Uipi_RevokesTheCredit()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        ForegroundContext Context() => new("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var result = await PlainPresser.PressAsync(injector, Chord("Key_1"), PanelTabTracker.LeftPanelToggleAction, tracker, clock)
            .WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.UipiSuspected, result.Outcome);

        tracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // FocusRightPanel (Fix 7, 2026-09-07): the exact mirror of
    // FocusLeftPanel above, through the same real Win32KeyInjector path.
    // -----------------------------------------------------------------

    [Fact]
    public async Task FocusRightPanel_Sent_KeepsTheCredit_SoTheConfirmingEdgeIsNotMistakenForExternal()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var tracker = new PanelTabTracker();

        var pressTask = PlainPresser.PressAsync(injector, Chord("Key_2"), PanelTabTracker.RightPanelToggleAction, tracker, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);

        tracker.RecordRightPanelEdge();
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab); // armed - not mistaken for external
    }

    [Fact]
    public async Task FocusRightPanel_Refused_NotForeground_RevokesTheCredit()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);
        var tracker = new PanelTabTracker();

        var result = await PlainPresser.PressAsync(injector, Chord("Key_2"), PanelTabTracker.RightPanelToggleAction, tracker, clock)
            .WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);

        tracker.RecordRightPanelEdge();
        Assert.Equal(RightPanelTab.Home, tracker.RightCurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.RightTabConfidence);
    }

    // -----------------------------------------------------------------
    // CycleNextPanel/CyclePreviousPanel: only applied on a confirmed Sent.
    // Fix 7: applies to whichever panel is believed focused - these tests
    // record an own-left-panel-toggle first, as a real FocusLeftPanel press
    // earlier in the same button sequence would (see PanelTabTrackerTests
    // for the routing itself, pinned directly there).
    // -----------------------------------------------------------------

    [Fact]
    public async Task CycleNextPanel_Sent_AdvancesTheTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation
        tracker.RecordOwnLeftPanelToggle(); // belief -> Left

        var pressTask = PlainPresser.PressAsync(injector, Chord("Key_E"), PanelTabTracker.AdvanceTabAction, tracker, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
        Assert.Equal(PanelTab.Transactions, tracker.CurrentTab);
    }

    [Fact]
    public async Task CycleNextPanel_Refused_LeavesTheTrackerUntouched()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation

        var result = await PlainPresser.PressAsync(injector, Chord("Key_E"), PanelTabTracker.AdvanceTabAction, tracker, clock)
            .WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab); // untouched, not confidently wrong
    }

    [Fact]
    public async Task CyclePreviousPanel_Sent_RetreatsTheTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump(); // Navigation
        tracker.RecordOwnLeftPanelToggle(); // belief -> Left

        var pressTask = PlainPresser.PressAsync(injector, Chord("Key_Q"), PanelTabTracker.RetreatTabAction, tracker, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
        Assert.Equal(PanelTab.Galaxy, tracker.CurrentTab);
    }

    [Fact]
    public async Task CyclePreviousPanel_Refused_LeavesTheTrackerUntouched()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var result = await PlainPresser.PressAsync(injector, Chord("Key_Q"), PanelTabTracker.RetreatTabAction, tracker, clock)
            .WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
    }

    // -----------------------------------------------------------------
    // An unrelated action never touches the tracker, regardless of outcome.
    // -----------------------------------------------------------------

    [Fact]
    public async Task UnrelatedAction_Sent_NeverTouchesTheTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);
        var tracker = new PanelTabTracker();
        tracker.RecordFsdJump();

        var pressTask = PlainPresser.PressAsync(injector, Chord("Key_G"), "LandingGearToggle", tracker, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        tracker.RecordLeftPanelEdge(); // no credit was ever armed
        Assert.Equal(PanelTab.Navigation, tracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tracker.LeftTabConfidence);
    }

    // -----------------------------------------------------------------
    // Parity: the same action, once through MacroRunner's plain press step
    // and once through PlainPresser, must leave the tracker in the same
    // state - the actual claim this whole extraction makes.
    // -----------------------------------------------------------------

    [Fact]
    public async Task FocusLeftPanel_ThroughPlainPresser_MatchesMacroRunnersPlainPressStep()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var log = new DiagnosticRingBuffer(16);

        // Path A: MacroRunner's own plain press step.
        var macroTracker = new PanelTabTracker();
        macroTracker.RecordFsdJump();
        var macroInjector = new RecordingSender();
        var macroKeyInjector = new Win32KeyInjector(log, () => false, EliteOk, macroInjector.Send);
        var gameState = new GameStateStore(clock);
        var runner = new MacroRunner(macroKeyInjector, gameState, new JournalStateStore(), macroTracker, clock, log);
        var bindings = BindingsFile.Parse(
            "<Root MajorVersion=\"4\" MinorVersion=\"2\"><FocusLeftPanel><Primary Device=\"Keyboard\" Key=\"Key_1\" /></FocusLeftPanel></Root>").File!;
        var macro = new MacroDefinition("test-macro", "Test", new MacroStep[] { new PressStep("FocusLeftPanel", Repeat: 1, Hold: null) });

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await runTask.WaitAsync(PressHangGuard);
        macroTracker.RecordLeftPanelEdge();

        // Path B: PlainPresser, same action, same starting state.
        var plainTracker = new PanelTabTracker();
        plainTracker.RecordFsdJump();
        var plainSender = new RecordingSender();
        var plainKeyInjector = new Win32KeyInjector(log, () => false, EliteOk, plainSender.Send);

        var pressTask = PlainPresser.PressAsync(plainKeyInjector, Chord("Key_1"), "FocusLeftPanel", plainTracker, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await pressTask.WaitAsync(PressHangGuard);
        plainTracker.RecordLeftPanelEdge();

        Assert.Equal(macroTracker.CurrentTab, plainTracker.CurrentTab);
    }
}
