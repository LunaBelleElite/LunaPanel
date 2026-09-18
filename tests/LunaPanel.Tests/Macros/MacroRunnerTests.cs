using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Drives <see cref="MacroRunner"/> against a recording <see cref="IKeyInjector"/>
/// fake and a hand-driven <see cref="GameStateStore"/>, with a fake
/// <see cref="TimeProvider"/> (same shape as <c>GameStateStoreTests</c>'
/// own) so every delay - hold, inter-press gap, modifier settle, a
/// <c>wait</c> step, and every gated step's poll - is advanced virtually,
/// never by a real sleep. Actions resolve against a hand-crafted
/// <see cref="BindingsFile"/> built from inline XML, matching the
/// established convention in <c>BindResolverTests</c>.
/// </summary>
public class MacroRunnerTests
{
    // ---------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------

    /// <summary>
    /// A <see cref="TimeProvider"/> whose clock only moves when
    /// <see cref="Advance"/> is called, and whose <see cref="ITimer"/>s fire
    /// synchronously, inline, from that same call. Same shape as
    /// <c>GameStateStoreTests.FakeTimeProvider</c> - see that file's remarks
    /// for why <c>CreateTimer</c> (not just <c>GetUtcNow</c>) must be
    /// virtualized for anything that actually waits.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _utcNow;
        private readonly List<PendingTimer> _pending = new();

        /// <summary>
        /// Completed while the code under test is known to be parked on a
        /// timer this clock holds, and reset by any <see cref="Advance"/>
        /// that actually fires one - because from that moment until the
        /// awaiting continuation re-arms, there is nothing for a further
        /// advance to land on, and any time added in that window is
        /// silently lost. <see cref="ArmedAsync"/> is how
        /// <see cref="PumpAsync"/> waits that window out without guessing at
        /// a duration. Continuations run asynchronously so a waiter can
        /// never resume inline underneath <c>_lock</c>.
        /// </summary>
        private TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Incremented on every <see cref="CreateTimer"/> call, never reset.
        /// Where <see cref="ArmedAsync"/> can only say "armed again since the
        /// last fire", this lets a caller ask the more specific question
        /// "armed again since I last looked" - <see cref="NextArmAsync"/> -
        /// which is what O42's per-test overload needs: a deterministic
        /// signal for the re-arm that follows one particular advance, not
        /// just any re-arm.
        /// </summary>
        private int _armGeneration;

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
                _armGeneration++;
                _armed.TrySetResult();
            }

            return timer;
        }

        /// <summary>
        /// Completes once the code under test is parked on a timer again -
        /// immediately, if no advance since the last arming actually fired
        /// anything (a partial advance leaves the awaiting code parked on
        /// the same timer it was already on, so there is nothing to wait
        /// for).
        /// </summary>
        public Task ArmedAsync()
        {
            lock (_lock) { return _armed.Task; }
        }

        /// <summary>
        /// The current arm generation, read BEFORE an advance so
        /// <see cref="NextArmAsync"/> can later be asked for the specific
        /// re-arm that advance releases - see
        /// <c>PumpAndWaitForNextArmAsync</c>.
        /// </summary>
        public int ArmGeneration
        {
            get { lock (_lock) { return _armGeneration; } }
        }

        /// <summary>
        /// Completes the first time <see cref="CreateTimer"/> is called with
        /// a generation number greater than <paramref name="sinceGeneration"/>
        /// - a real signal off the clock's own bookkeeping, not a duration,
        /// so it can be awaited with a generous bound at no cost to a passing
        /// run: a genuine re-arm completes it in microseconds. Unlike
        /// <see cref="ArmedAsync"/>, which only distinguishes "armed" from
        /// "not armed since the last fire", this distinguishes a re-arm that
        /// happened before the caller looked from one that happens after.
        /// </summary>
        public async Task NextArmAsync(int sinceGeneration)
        {
            while (true)
            {
                Task armedTask;
                lock (_lock)
                {
                    if (_armGeneration > sinceGeneration) return;
                    armedTask = _armed.Task;
                }

                await armedTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Moves the clock and fires everything now due, returning HOW MANY
        /// callbacks fired - zero meaning the code under test cannot
        /// possibly have moved, which is what lets <see cref="PumpAsync"/>
        /// skip both of its waits for a deliberate partial advance.
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

                // Reset BEFORE the callbacks run: a continuation that re-arms
                // synchronously from inside InvokeCallback must complete the
                // fresh signal, not the stale one it is replacing.
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

    private sealed record KeyEvent(bool IsDown, ScancodeInfo Key, DateTimeOffset At);

    /// <summary>
    /// Records every raw key event, stamped with the same fake clock the
    /// runner uses, so a test can assert not just how many presses happened
    /// but exactly how long each hold/gap actually was.
    /// </summary>
    private sealed class RecordingKeyInjector : IKeyInjector
    {
        private readonly TimeProvider _clock;
        public List<KeyEvent> Events { get; } = new();

        public RecordingKeyInjector(TimeProvider clock) => _clock = clock;

        public void KeyDown(ScancodeInfo key) => Events.Add(new KeyEvent(true, key, _clock.GetUtcNow()));
        public void KeyUp(ScancodeInfo key) => Events.Add(new KeyEvent(false, key, _clock.GetUtcNow()));

        public int DownCount => Events.Count(e => e.IsDown);
        public int UpCount => Events.Count(e => !e.IsDown);
    }

    /// <summary>
    /// Records like <see cref="RecordingKeyInjector"/>, but throws
    /// <see cref="InvalidOperationException"/> instead of recording the down
    /// stroke of one nominated scancode - so a chord can be interrupted
    /// after its modifiers are already held. Exists for exactly one pin:
    /// that <c>MacroRunner.PressChordOnceAsync</c>'s release path is a
    /// <c>finally</c>, not merely the next statements in line.
    /// </summary>
    private sealed class ThrowingKeyInjector : IKeyInjector
    {
        private readonly TimeProvider _clock;
        private readonly ushort _throwOnScanCode;

        public ThrowingKeyInjector(TimeProvider clock, ushort throwOnScanCode)
        {
            _clock = clock;
            _throwOnScanCode = throwOnScanCode;
        }

        public List<KeyEvent> Events { get; } = new();

        public void KeyDown(ScancodeInfo key)
        {
            if (key.ScanCode == _throwOnScanCode)
            {
                throw new InvalidOperationException($"Injector refused scancode 0x{key.ScanCode:X2}.");
            }

            Events.Add(new KeyEvent(true, key, _clock.GetUtcNow()));
        }

        public void KeyUp(ScancodeInfo key) => Events.Add(new KeyEvent(false, key, _clock.GetUtcNow()));
    }

    /// <summary>
    /// Asserts every key that went down came back up - the guarantee that a
    /// stopped or failed macro can never leave a key held in the commander's
    /// game. Counts per scancode rather than in total, so a spurious extra
    /// key-up for one key cannot mask a missing one for another.
    /// </summary>
    private static void AssertNothingLeftHeld(IEnumerable<KeyEvent> events)
    {
        var held = new Dictionary<ushort, int>();
        foreach (var e in events)
        {
            held[e.Key.ScanCode] = held.GetValueOrDefault(e.Key.ScanCode) + (e.IsDown ? 1 : -1);
        }

        foreach (var (scanCode, net) in held)
        {
            Assert.True(net == 0, $"Scancode 0x{scanCode:X2} was left held (net downs-minus-ups = {net}).");
        }
    }

    // ---------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------

    private static BindingsFile BuildBindings(params (string Action, string Key)[] bindings)
    {
        var elements = string.Concat(bindings.Select(b =>
            $"<{b.Action}><Primary Device=\"Keyboard\" Key=\"{b.Key}\" /></{b.Action}>"));
        var xml = $"<Root MajorVersion=\"4\" MinorVersion=\"2\">{elements}</Root>";
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    /// <summary>
    /// One action bound to a real chord - <c>Ctrl+Alt+W</c> - so a test can
    /// watch modifiers go down and come back up. Same inline-XML convention
    /// as <see cref="BuildBindings"/>, which only handles a bare key.
    /// </summary>
    private static BindingsFile BuildChordBindings()
    {
        const string xml = "<Root MajorVersion=\"4\" MinorVersion=\"2\">" +
            "<ChordAction><Primary Device=\"Keyboard\" Key=\"Key_W\">" +
            "<Modifier Device=\"Keyboard\" Key=\"Key_LeftControl\" />" +
            "<Modifier Device=\"Keyboard\" Key=\"Key_LeftAlt\" />" +
            "</Primary></ChordAction></Root>";
        var result = BindingsFile.Parse(xml);
        Assert.True(result.Success, result.Error);
        return result.File!;
    }

    private static StatusSnapshot Snapshot(bool docked) =>
        new(docked ? 1u : 0u, 0u, null, GameRunning: true, SignedIn: true);

    private static MacroDefinition MacroWithSteps(params MacroStep[] steps) =>
        new("test-macro", "Test Macro", steps);

    private static MacroDefinition MacroWithId(string id, params MacroStep[] steps) =>
        new(id, "Test Macro", steps);

    /// <summary>
    /// Waits until the code under test is parked on a timer this fake clock
    /// holds, and only then advances it.
    ///
    /// <b>Rewritten 2026-09-17 (O6).</b> This used to advance first and then
    /// cede a fixed <c>Task.Delay(50)</c> of REAL time, hoping the chained
    /// continuation would resume within it - <c>MacroRunner</c>'s call depth
    /// (RunAsync -&gt; ExecuteSequenceAsync -&gt; ExecuteStepAsync -&gt;
    /// Execute*Async -&gt; PressChordOnceAsync -&gt; DelayAsync) is past the
    /// CLR's synchronous-continuation stack-dive guard, so the resumption is
    /// deferred to the thread pool rather than run inline from
    /// <see cref="FakeTimeProvider.Advance"/>. On a loaded machine 50ms was
    /// not always enough, and an advance issued into that gap is not merely
    /// early: it is <b>lost</b>, because the timer the continuation goes on
    /// to create is dated from the already-advanced clock and so falls due
    /// at a moment nothing will ever reach. That is the hang behind the
    /// <c>TimeoutException</c>s seen from
    /// <see cref="RunHangGuard"/> (observed on a full-suite baseline run,
    /// 2026-09-17, in <c>Win32KeyInjectorTests</c>).
    ///
    /// The margin is now a signal rather than a duration: the clock knows
    /// when it last fired a timer and when one was next created
    /// (<see cref="FakeTimeProvider.ArmedAsync"/>), so this returns the
    /// instant the code under test is observably ready, typically in well
    /// under a millisecond. The wait that matters is the one BEFORE the
    /// advance - it is the only one whose absence can lose time outright,
    /// and it also covers the very first advance of a run. The second wait,
    /// after the advance, exists only so a test asserting between two pumps
    /// sees what that advance released; it is the one that still spends real
    /// time (<see cref="PumpSettleGuard"/>), because the advance that
    /// finishes a run never re-arms and so always runs it out.
    /// <see cref="PumpArmGuard"/> keeps a genuinely never-arriving arm from
    /// hanging the suite; the first wait is what the pins depend on, and
    /// every timing assertion in this file still measures a delta between
    /// two <see cref="DateTimeOffset"/> values read from the fake clock
    /// inside <see cref="RecordingKeyInjector"/>, never real elapsed time.
    ///
    /// <b>Narrowed 2026-09-17 (O42).</b> The second wait is now a
    /// best-effort guess only for the call sites that DON'T need better -
    /// the eight tests that genuinely depend on seeing the state a firing
    /// advance released now call <see cref="PumpAndWaitForNextArmAsync"/>
    /// instead, which waits on a real re-arm signal rather than a bounded
    /// guess. This method is unchanged for everyone else.
    /// </summary>
    private static async Task PumpAsync(FakeTimeProvider clock, TimeSpan advanceBy)
    {
        // Barrier one, and the load-bearing one: never advance into the gap
        // where the code under test is mid-resumption and holds no timer.
        await clock.ArmedAsync().WaitAsync(PumpArmGuard);

        if (clock.Advance(advanceBy) == 0)
        {
            // A deliberate partial advance - nothing was due, so nothing
            // resumed, and there is no settling to wait for either.
            return;
        }

        // Barrier two, best-effort: let what this advance released become
        // observable, for the tests that assert between two pumps rather
        // than after the run task. Returns the moment the code under test
        // parks on its next timer; falls through on the one case where that
        // never happens - the advance that finished the run - which is why
        // this one is short and swallowed rather than fatal. Correctness
        // does not rest here: if a loaded machine outruns this guard
        // mid-run, barrier one above still holds the next advance back.
        try
        {
            await clock.ArmedAsync().WaitAsync(PumpSettleGuard);
        }
        catch (TimeoutException)
        {
        }
    }

    /// <summary>
    /// Variant of <see cref="PumpAsync"/> for a call site where the
    /// assertion right after it depends on having reliably observed the
    /// state a firing advance released - the eight <c>MacroRunnerTests</c>
    /// named in open item O42. Where <see cref="PumpAsync"/>'s second
    /// barrier is a bounded guess (<see cref="PumpSettleGuard"/>, 250ms of
    /// real time) that can in principle undershoot on a loaded machine, this
    /// captures the clock's arm generation BEFORE advancing and, if the
    /// advance fired anything, awaits <see cref="FakeTimeProvider.NextArmAsync"/>
    /// for that exact generation - a real signal, so it can use the same
    /// generous <see cref="PumpArmGuard"/> bound as barrier one without
    /// slowing a passing run down at all.
    ///
    /// <b>Only safe where more clock-driven work is known to be pending
    /// after the advance.</b> The one case the fake clock never re-arms is
    /// the advance that finishes a run, and using this method there would
    /// wait out the full <see cref="PumpArmGuard"/> for an arm that will
    /// never come. Every call site this is used from has been checked
    /// against that.
    /// </summary>
    private static async Task PumpAndWaitForNextArmAsync(FakeTimeProvider clock, TimeSpan advanceBy)
    {
        await clock.ArmedAsync().WaitAsync(PumpArmGuard);

        var generationBeforeAdvance = clock.ArmGeneration;
        if (clock.Advance(advanceBy) == 0)
        {
            return;
        }

        await clock.NextArmAsync(generationBeforeAdvance).WaitAsync(PumpArmGuard);
    }

    /// <summary>
    /// Upper bound on <see cref="PumpAsync"/>'s wait for the code under test
    /// to re-arm before advancing again. Reached only when nothing ever will
    /// - a pump issued after the run it was driving has already finished -
    /// so it is a hang guard, not a margin: a passing test spends
    /// microseconds here, not seconds.
    /// </summary>
    private static readonly TimeSpan PumpArmGuard = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound on <see cref="PumpAsync"/>'s post-advance settle. Paid in
    /// full exactly once per test - on the advance that completes the run,
    /// after which there is no next timer to wait for - so it is kept short;
    /// every other advance leaves this the instant the next timer is armed.
    /// </summary>
    private static readonly TimeSpan PumpSettleGuard = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Upper bound for every <c>await</c> on a <see cref="MacroRunner.RunAsync"/>
    /// (or <c>MacroPresser.RunAsync</c>, in siblings of this file) run task
    /// driven by this file's hand-pumped fake clock. Before this existed, a
    /// mutation that made the code under test need more clock advances than
    /// a test supplied (e.g. reducing a repeat count below what the test
    /// pumps for) hung the whole suite for 10+ minutes with zero output
    /// instead of failing (2026-09-07, three times, open item O27) - the
    /// await just never returned, because nothing was ever going to advance
    /// the fake clock again. <see cref="Task.WaitAsync(TimeSpan)"/> turns
    /// that silent hang into a <see cref="TimeoutException"/> naming the
    /// awaited call (and, via the test framework, the test).
    ///
    /// 10 seconds is deliberately generous relative to what these tests
    /// actually take. <b>Corrected 2026-09-17:</b> this used to say the
    /// real-time cost per <see cref="PumpAsync"/> call was "a single,
    /// bounded <c>Task.Delay(50)</c> (O6 ... not touched here)". O6 has
    /// since been closed and that blind sleep is gone - a pump now waits on
    /// a signal the fake clock raises, so it costs microseconds, except for
    /// the one advance per test that finishes the run and runs out
    /// <see cref="PumpSettleGuard"/>'s 250ms. The conclusion is unchanged
    /// and now has more headroom, not less. The longest sequence in this file
    /// (<see cref="ShippedDisembarkMacro_Docked_TakesTheStationArm_AndProducesTheExpectedInjectionSequence"/>)
    /// issues five pumps, on the order of 250ms of real wall-clock time. 10
    /// seconds is nowhere close to that even under heavy machine load, so it
    /// never flakes a passing run, while still turning a genuine hang into a
    /// failure reported in seconds rather than minutes.
    /// </summary>
    private static readonly TimeSpan RunHangGuard = TimeSpan.FromSeconds(10);

    // ---------------------------------------------------------------
    // the pumping mechanism itself (O6)
    // ---------------------------------------------------------------

    /// <summary>
    /// Pins what <see cref="PumpAsync"/> actually has to guarantee: a second
    /// <see cref="FakeTimeProvider.Advance"/> must not be issued until the
    /// code under test has re-armed a timer on the fake clock. An advance
    /// issued into that gap is <b>silently lost</b> - the next timer is then
    /// created relative to the already-advanced clock, so it is due at a
    /// time nothing will ever reach, and the run hangs until
    /// <see cref="RunHangGuard"/> fires.
    ///
    /// The chain below stands in for <c>MacroRunner</c>'s own: a wait on the
    /// fake clock, a resumption cost, then another wait on the fake clock.
    /// The resumption cost is a real 600ms - deliberately longer than both
    /// the fixed 50ms margin <c>PumpAsync</c> used to cede AND the 250ms
    /// <see cref="PumpSettleGuard"/> - so what this pins is specifically
    /// <c>PumpAsync</c>'s first barrier, the wait BEFORE the advance, and it
    /// cannot be kept green by the best-effort settle afterwards.
    /// Observed red, 2026-09-17, against the old advance-then-sleep body:
    /// this test alone,
    /// <c>System.TimeoutException : The operation has timed out.</c>
    /// </summary>
    [Fact]
    public async Task PumpAsync_DoesNotAdvancePastAContinuationThatHasNotReArmedYet()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var step = TimeSpan.FromMilliseconds(100);
        var reached = 0;

        async Task SlowChainAsync()
        {
            await Task.Delay(step, clock).ConfigureAwait(false);
            Interlocked.Exchange(ref reached, 1);
            await Task.Delay(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            await Task.Delay(step, clock).ConfigureAwait(false);
            Interlocked.Exchange(ref reached, 2);
        }

        var chain = SlowChainAsync();

        await PumpAsync(clock, step);
        await PumpAsync(clock, step);

        await chain.WaitAsync(RunHangGuard);
        Assert.Equal(2, Volatile.Read(ref reached));
    }

    // ---------------------------------------------------------------
    // press / repeat
    // ---------------------------------------------------------------

    [Fact]
    public async Task PressStep_InjectsExactlyRepeatTimes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 3, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);

        // 3 repeats: hold, gap, hold, gap, hold (no trailing gap after the last press).
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(3, injector.DownCount);
        Assert.Equal(3, injector.UpCount);
    }

    [Fact]
    public async Task PressStep_HoldDurationAndInterPressGap_MatchDefaults_Exactly()
    {
        // Literal pins, not just internal self-consistency: every delta
        // assertion below references MacroTimingDefaults.* symbolically, so
        // it would stay green even if the underlying constants drifted away
        // from the measured minimums the brief calls for (>=100ms hold,
        // 100ms between presses). Pinning the literal values here too closes
        // that gap.
        Assert.Equal(TimeSpan.FromMilliseconds(150), MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), MacroTimingDefaults.InterPressGap);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 2, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(4, injector.Events.Count);
        var down1 = injector.Events[0];
        var up1 = injector.Events[1];
        var down2 = injector.Events[2];
        var up2 = injector.Events[3];

        Assert.True(down1.IsDown);
        Assert.False(up1.IsDown);
        Assert.True(down2.IsDown);
        Assert.False(up2.IsDown);

        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, up1.At - down1.At);
        Assert.Equal(MacroTimingDefaults.InterPressGap, down2.At - up1.At);
        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, up2.At - down2.At);
    }

    [Fact]
    public async Task PressStep_HoldMsOverride_UsesOverride_NotTheDefault()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var overrideHold = TimeSpan.FromMilliseconds(400);
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: overrideHold));

        var runTask = runner.RunAsync(macro, bindings);

        clock.Advance(overrideHold);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(overrideHold, injector.Events[1].At - injector.Events[0].At);
    }

    // ---------------------------------------------------------------
    // macro timing settings (ref/docs/macro-timing.md) - server-wide,
    // configurable hold duration and inter-press gap. This section proves
    // MacroRunner actually CONSUMES a stored MacroTimingSettingsStore value -
    // not merely that the value round-trips through the store itself (see
    // MacroTimingSettingsStoreTests for that half) - by driving a real run
    // through the fake clock and reading back the exact deltas between
    // recorded key events, the same technique
    // PressStep_HoldDurationAndInterPressGap_MatchDefaults_Exactly already
    // uses for the shipped defaults.
    // ---------------------------------------------------------------

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "macro-runner-timing", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// <b>Deliberately NOT a single big <c>PumpAsync</c> to the configured
    /// value.</b> <see cref="FakeTimeProvider.Advance"/> jumps <c>_utcNow</c>
    /// forward FIRST and only then fires whichever timers are now due, so a
    /// timer's recorded fire time is always exactly the JUMPED clock value,
    /// never the timer's own due time - which means a single pump of
    /// <c>configuredHold</c> cannot tell "the code waited exactly
    /// <c>configuredHold</c>" apart from "the code waited only the shipped
    /// default and the extra time was simply never needed". Confirmed
    /// empirically while writing this test: an early version pumped once to
    /// each configured value and stayed GREEN even when
    /// <c>MacroRunner</c> was mutated back to the shipped defaults - a
    /// silent undershoot this boundary-check shape exists to close. Every
    /// timing assertion in this section instead pumps to just short of the
    /// shipped default first (asserting the event has NOT fired), then pumps
    /// the remainder up to the configured value (asserting it now HAS) -
    /// which only passes if the runtime value actually sits strictly above
    /// the shipped default, in either direction.
    /// </summary>
    [Fact]
    public async Task PressStep_TimingSettingsStoreConfigured_UsesStoredHoldAndGap_NotShippedDefaults()
    {
        var configuredHold = TimeSpan.FromMilliseconds(275);
        var configuredGap = TimeSpan.FromMilliseconds(180);
        Assert.True(configuredHold > MacroTimingDefaults.DefaultHoldDuration);
        Assert.True(configuredGap > MacroTimingDefaults.InterPressGap);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var timingStore = new MacroTimingSettingsStore(NewTempDir(), log);
        timingStore.Save(new MacroTimingSettings(configuredHold, configuredGap));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log, timingStore);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 2, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);

        // Boundary 1: at the shipped default hold, the key must NOT yet be
        // released - only the configured (larger) hold governs this run.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Single(injector.Events);
        await PumpAndWaitForNextArmAsync(clock, configuredHold - MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(2, injector.Events.Count);

        // Boundary 2: same discipline for the inter-press gap.
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        Assert.Equal(2, injector.Events.Count);
        await PumpAndWaitForNextArmAsync(clock, configuredGap - MacroTimingDefaults.InterPressGap);
        Assert.Equal(3, injector.Events.Count);

        await PumpAsync(clock, configuredHold);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(4, injector.Events.Count);
    }

    /// <summary>
    /// <b>A timing change applies to the very next macro run, with nothing
    /// restarted.</b> The neighbouring test above saves before the runner is
    /// even constructed, so it cannot tell "read per run" apart from "read
    /// once at construction"; this one runs a macro first, changes the
    /// setting on the same store instance the same runner is still holding,
    /// and runs again.
    ///
    /// The second run's assertions are the boundary pair this file's own
    /// remarks explain: pumping straight to the configured value cannot
    /// distinguish "waited the configured amount" from "waited less", because
    /// the fake clock jumps forward and then fires whatever is now due. So
    /// the shipped default is pumped first and the key must still be DOWN.
    /// </summary>
    [Fact]
    public async Task PressStep_TimingSavedAfterAnEarlierRun_AppliesToTheNextRun_WithNoRestart()
    {
        var configuredHold = TimeSpan.FromMilliseconds(275);
        Assert.True(configuredHold > MacroTimingDefaults.DefaultHoldDuration);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var timingStore = new MacroTimingSettingsStore(NewTempDir(), log);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log, timingStore);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        // Run one, on nothing but the shipped default - the file does not
        // exist yet.
        var firstRun = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await firstRun.WaitAsync(RunHangGuard)).Outcome);
        Assert.Equal(2, injector.Events.Count);

        // The commander changes it mid-session. Nothing is rebuilt, nothing
        // is restarted - the same runner, the same store instance.
        timingStore.Save(new MacroTimingSettings(configuredHold, MacroTimingDefaults.InterPressGap));

        var secondRun = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(3, injector.Events.Count); // key down only - not released at the old default
        await PumpAsync(clock, configuredHold - MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(4, injector.Events.Count);

        Assert.Equal(MacroRunOutcome.Success, (await secondRun.WaitAsync(RunHangGuard)).Outcome);
    }

    [Fact]
    public async Task PressStep_PerStepHoldOverride_BeatsTimingSettingsStore()
    {
        // Precedence pinned per ref/docs/macro-timing.md's "Defaults and
        // compatibility": a per-step holdMs beats the commander's setting,
        // which beats the shipped default. The commander's setting is
        // deliberately configured here too (and deliberately LARGER than the
        // per-step override, not smaller - see this section's own remarks on
        // why a single big pump cannot distinguish "used the smaller value"
        // from "used the larger one"), so this test cannot pass merely
        // because the store was never consulted.
        var configuredHold = TimeSpan.FromMilliseconds(275);
        var perStepHold = TimeSpan.FromMilliseconds(100);
        Assert.True(perStepHold < configuredHold);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var timingStore = new MacroTimingSettingsStore(NewTempDir(), log);
        timingStore.Save(new MacroTimingSettings(configuredHold, MacroTimingDefaults.InterPressGap));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log, timingStore);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: perStepHold));

        var runTask = runner.RunAsync(macro, bindings);

        // If the (larger) stored setting won instead of the per-step
        // override, this pump would not be enough to release the key, and
        // the run would still be waiting when RunHangGuard's timeout fires -
        // a real, bounded failure rather than a silent pass.
        await PumpAsync(clock, perStepHold);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(perStepHold, injector.Events[1].At - injector.Events[0].At);
    }

    [Fact]
    public async Task PressUntilStep_TimingSettingsStoreConfigured_UsesStoredHoldDuration()
    {
        var configuredHold = TimeSpan.FromMilliseconds(275);
        Assert.True(configuredHold > MacroTimingDefaults.DefaultHoldDuration);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var timingStore = new MacroTimingSettingsStore(NewTempDir(), log);
        timingStore.Save(new MacroTimingSettings(configuredHold, MacroTimingDefaults.InterPressGap));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log, timingStore);
        var bindings = BuildBindings(("ToggleDock", "Key_W"));
        var macro = MacroWithSteps(new PressUntilStep(
            "ToggleDock", ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromSeconds(5), MaxAttempts: 3));

        var runTask = runner.RunAsync(macro, bindings);

        // Boundary: at the shipped default hold, the key must NOT yet be
        // released - only the configured (larger) hold governs this run.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Single(injector.Events);
        await PumpAndWaitForNextArmAsync(clock, configuredHold - MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(2, injector.Events.Count);

        store.UpdateSnapshot(Snapshot(docked: true));
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(configuredHold, injector.Events[1].At - injector.Events[0].At);
    }

    [Fact]
    public async Task GotoLeftPanelTabStep_TimingSettingsStoreConfigured_UsesStoredHoldAndGap()
    {
        var configuredHold = TimeSpan.FromMilliseconds(275);
        var configuredGap = TimeSpan.FromMilliseconds(180);
        Assert.True(configuredHold > MacroTimingDefaults.DefaultHoldDuration);
        Assert.True(configuredGap > MacroTimingDefaults.InterPressGap);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // anchors at Navigation
        var log = new DiagnosticRingBuffer(64);
        var timingStore = new MacroTimingSettingsStore(NewTempDir(), log);
        timingStore.Save(new MacroTimingSettings(configuredHold, configuredGap));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log, timingStore);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macro = MacroWithSteps(new GotoLeftPanelTabStep(PanelTab.Contacts));

        var runTask = runner.RunAsync(macro, bindings);

        // Navigation -> Contacts is 2 presses (Navigation -> Transactions -> Contacts).
        // Boundary 1: the first hold.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Single(injector.Events);
        await PumpAndWaitForNextArmAsync(clock, configuredHold - MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(2, injector.Events.Count);

        // Boundary 2: the gap between the two presses.
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        Assert.Equal(2, injector.Events.Count);
        await PumpAndWaitForNextArmAsync(clock, configuredGap - MacroTimingDefaults.InterPressGap);
        Assert.Equal(3, injector.Events.Count);

        // The second hold uses the exact same `timing.HoldDuration` source
        // line as the first (ExecuteGotoLeftPanelTabAsync's loop body) - not
        // a second, independently-mutable arm - so a single pump to the
        // configured value suffices here without repeating the boundary
        // check.
        await PumpAsync(clock, configuredHold);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(4, injector.Events.Count);
    }

    [Fact]
    public async Task PressStep_ActionNotBound_Aborts_WithZeroInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(); // no actions at all
        var macro = MacroWithSteps(new PressStep("NotBound", Repeat: 1, Hold: null));

        var result = await runner.RunAsync(macro, bindings);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        Assert.Empty(injector.Events);
    }

    // ---------------------------------------------------------------
    // pressKey - "press a key" (ref/docs/macros.md): a raw physical key,
    // resolved through Scancodes.TryGet directly - no BindingsFile/
    // BindResolver involvement, unlike every test above.
    // ---------------------------------------------------------------

    [Fact]
    public async Task PressKeyStep_InjectsExactlyRepeatTimes_UsingTheKeysOwnScancode()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(); // no actions bound at all - pressKey needs none
        var macro = MacroWithSteps(new PressKeyStep("Key_F5", Repeat: 2, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(2, injector.DownCount);
        Assert.Equal(2, injector.UpCount);
        // Key_F5's real scancode (Scancodes.cs), not a bound action's - the
        // whole point of this step kind is that no BindingsFile lookup ever
        // happens for it.
        Assert.All(injector.Events, e => Assert.Equal(new ScancodeInfo(0x3F, false), e.Key));
    }

    [Fact]
    public async Task PressKeyStep_HoldMsOverride_UsesOverride_NotTheDefault()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var overrideHold = TimeSpan.FromMilliseconds(400);
        var macro = MacroWithSteps(new PressKeyStep("Key_F5", Repeat: 1, Hold: overrideHold));

        var runTask = runner.RunAsync(macro, bindings);

        clock.Advance(overrideHold);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(overrideHold, injector.Events[1].At - injector.Events[0].At);
    }

    /// <summary>
    /// Defensive only - <see cref="MacroDefinition.Parse"/> already rejects
    /// an unrecognized key at load time, and the client only ever sends a
    /// name straight out of <c>Scancodes.All</c>. Constructing the step
    /// directly (bypassing Parse) is the only way to reach this branch at
    /// all, which is exactly why it needs its own pin: nothing upstream of
    /// <c>MacroRunner</c> can be trusted to have already caught it.
    /// </summary>
    [Fact]
    public async Task PressKeyStep_UnrecognizedKey_AbortsWithZeroInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new PressKeyStep("Key_NotARealKey", Repeat: 1, Hold: null));

        var result = await runner.RunAsync(macro, bindings);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        Assert.Empty(injector.Events);
    }

    // ---------------------------------------------------------------
    // wait
    // ---------------------------------------------------------------

    [Fact]
    public async Task WaitStep_DelaysExactlyTheConfiguredDuration()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitStep(TimeSpan.FromMilliseconds(750)));

        var runTask = runner.RunAsync(macro, bindings);
        Assert.False(runTask.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(749));
        Assert.False(runTask.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
    }

    // ---------------------------------------------------------------
    // require
    // ---------------------------------------------------------------

    [Fact]
    public async Task RequireStep_ConditionFalse_FiresAnywayWithWarning()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new RequireStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }),
            new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(1, injector.DownCount);

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("Docked", warning.Message);
    }

    [Fact]
    public async Task RequireStep_NoSnapshotYet_TreatedAsNotSatisfied_FiresAnywayWithWarning()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock); // Current is null - game not running yet
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new RequireStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }),
            new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(1, injector.DownCount);

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("no snapshot yet", warning.Message);
    }

    [Fact]
    public async Task RequireStep_ConditionTrue_Succeeds_AndContinues()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new RequireStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }),
            new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(1, injector.DownCount);
    }

    // ---------------------------------------------------------------
    // require: the LeftPanelTabKnown gate built 2026-09-07 was REMOVED
    // 2026-09-08 at the user's explicit governing decision reversing
    // refuse-when-unknown: "We should never NOT be allowed to use a macro."
    // See ref/docs/panel-tab-tracking.md's "Reversed 2026-09-08".
    // RequireStep no longer carries any notion of PanelTabTracker at all -
    // it only ever evaluates ConditionList against GameStateStore.Current.
    // The pin below proves that directly: a require step succeeds
    // regardless of the tab tracker's confidence, because MacroRunner never
    // looks at it for this step kind anymore.
    // ---------------------------------------------------------------

    [Fact]
    public async Task RequireStep_NeverConsultsTheTabTracker_SucceedsEvenAtLowConfidence()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var log = new DiagnosticRingBuffer(64);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordLeftPanelEdge(); // uncredited edge -> Low confidence; tab itself stays Navigation
        Assert.Equal(TabConfidence.Low, tabTracker.LeftTabConfidence);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new RequireStep(ConditionList.Parse(Array.Empty<string>()), Array.Empty<string>()),
            new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(1, injector.DownCount);
    }

    // ---------------------------------------------------------------
    // waitFor
    // ---------------------------------------------------------------

    [Fact]
    public async Task WaitForStep_SatisfiedWhenStoreFlips_Succeeds()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);
        Assert.False(runTask.IsCompleted);

        store.UpdateSnapshot(Snapshot(docked: true));
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task WaitForStep_TimesOut_AbortsNamingTheStep()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForStep(ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromMilliseconds(500)));

        var runTask = runner.RunAsync(macro, bindings);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        Assert.Contains("Docked", result.FailureReason);
    }

    // ---------------------------------------------------------------
    // pressUntil
    // ---------------------------------------------------------------

    [Fact]
    public async Task PressUntilStep_SucceedsOnFirstPress_WhenConditionArrives_ExactlyOneInjection()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ToggleDock", "Key_W"));
        var macro = MacroWithSteps(new PressUntilStep(
            "ToggleDock", ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromSeconds(5), MaxAttempts: 3));

        var runTask = runner.RunAsync(macro, bindings);

        // let the single press's hold elapse, then simulate the game
        // reacting to that press before the per-attempt timeout fires.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        store.UpdateSnapshot(Snapshot(docked: true));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(1, injector.DownCount);
    }

    [Fact]
    public async Task PressUntilStep_RepressesWhenConditionDoesNotArrive_ThenSucceeds_ExactlyTwoInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ToggleDock", "Key_W"));
        var perAttemptTimeout = TimeSpan.FromMilliseconds(500);
        var macro = MacroWithSteps(new PressUntilStep(
            "ToggleDock", ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, perAttemptTimeout, MaxAttempts: 3));

        var runTask = runner.RunAsync(macro, bindings);

        // Attempt 1: press, then the condition never arrives - times out.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, perAttemptTimeout);

        // Attempt 2: press again, and this time the condition arrives.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        store.UpdateSnapshot(Snapshot(docked: true));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(2, injector.DownCount);
    }

    [Fact]
    public async Task PressUntilStep_GivesUpAfterMaxAttempts_NamingTheStep_ExactlyMaxAttemptsInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ToggleDock", "Key_W"));
        var perAttemptTimeout = TimeSpan.FromMilliseconds(500);
        const int maxAttempts = 3;
        var macro = MacroWithSteps(new PressUntilStep(
            "ToggleDock", ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, perAttemptTimeout, maxAttempts));

        var runTask = runner.RunAsync(macro, bindings);

        for (var i = 0; i < maxAttempts; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            await PumpAsync(clock, perAttemptTimeout);
        }

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        Assert.Contains("3", result.FailureReason);
        Assert.Equal(maxAttempts, injector.DownCount);
    }

    [Fact]
    public async Task PressUntilStep_ActionNotBound_Aborts_WithZeroInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new PressUntilStep(
            "NotBound", ConditionList.Parse(new[] { "Docked" }), new[] { "Docked" }, TimeSpan.FromSeconds(1), MaxAttempts: 3));

        var result = await runner.RunAsync(macro, bindings);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Empty(injector.Events);
    }

    // ---------------------------------------------------------------
    // pressUntil - condJournal (journal-event gate, added 2026-09-13)
    // ---------------------------------------------------------------

    [Fact]
    public async Task PressUntilStep_JournalGate_SucceedsAsSoonAsTheNamedEventArrives_ExactlyOneInjection()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("RefuelAllAction", "Key_W"));
        var macro = MacroWithSteps(new PressUntilStep(
            "RefuelAllAction", Conditions: null, ConditionTokens: null, TimeSpan.FromSeconds(5), MaxAttempts: 3,
            JournalCondition: EdgeCondition.Parse("Journal:RefuelAll")));

        var runTask = runner.RunAsync(macro, bindings);

        // let the single press's hold elapse, then simulate the journal
        // event arriving before the per-attempt timeout fires.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        journal.Record(new JournalEvent("RefuelAll", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(1, injector.DownCount);
    }

    [Fact]
    public async Task PressUntilStep_JournalGate_RepressesWhenEventDoesNotArrive_ThenSucceeds_ExactlyTwoInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("RefuelAllAction", "Key_W"));
        var perAttemptTimeout = TimeSpan.FromMilliseconds(500);
        var macro = MacroWithSteps(new PressUntilStep(
            "RefuelAllAction", Conditions: null, ConditionTokens: null, perAttemptTimeout, MaxAttempts: 3,
            JournalCondition: EdgeCondition.Parse("Journal:RefuelAll")));

        var runTask = runner.RunAsync(macro, bindings);

        // Attempt 1: press, then the event never arrives - times out.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, perAttemptTimeout);

        // Attempt 2: press again, and this time the event arrives.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        journal.Record(new JournalEvent("RefuelAll", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(2, injector.DownCount);
    }

    [Fact]
    public async Task PressUntilStep_JournalGate_GivesUpAfterMaxAttempts_NamingTheStep_ExactlyMaxAttemptsInjections()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("RefuelAllAction", "Key_W"));
        var perAttemptTimeout = TimeSpan.FromMilliseconds(500);
        const int maxAttempts = 3;
        var macro = MacroWithSteps(new PressUntilStep(
            "RefuelAllAction", Conditions: null, ConditionTokens: null, perAttemptTimeout, maxAttempts,
            JournalCondition: EdgeCondition.Parse("Journal:RefuelAll")));

        var runTask = runner.RunAsync(macro, bindings);

        for (var i = 0; i < maxAttempts; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            await PumpAsync(clock, perAttemptTimeout);
        }

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        Assert.Contains("3", result.FailureReason);
        Assert.Contains("Journal:RefuelAll", result.FailureReason);
        Assert.Equal(maxAttempts, injector.DownCount);
    }

    [Fact]
    public async Task PressUntilStep_JournalGate_StepProgressReportsTheSameShapeAsTheStatusGatedPath()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("RefuelAllAction", "Key_W"));
        var macro = MacroWithSteps(new PressUntilStep(
            "RefuelAllAction", Conditions: null, ConditionTokens: null, TimeSpan.FromSeconds(5), MaxAttempts: 3,
            JournalCondition: EdgeCondition.Parse("Journal:RefuelAll")));
        var reported = new List<MacroStepProgress>();
        // SynchronousProgress, not the framework's Progress<T> - changed
        // 2026-09-17. Progress<T> posts through the SynchronizationContext
        // captured at construction (here, xUnit's), so the callback and this
        // test's own continuation after `await runTask` are two independently
        // queued work items: the assertion below can and did run first,
        // finding an empty list. That is a second, wholly separate cause of
        // this test's intermittent failure, unrelated to the fake clock - it
        // was the last site in this file still using Progress<T>, which
        // SynchronousProgress already exists to avoid.
        var progress = new SynchronousProgress<MacroStepProgress>(reported.Add);

        var runTask = runner.RunAsync(macro, bindings, progress);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        journal.Record(new JournalEvent("RefuelAll", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        var stepProgress = Assert.Single(reported);
        Assert.Equal(0, stepProgress.StepIndex);
        Assert.Equal("pressUntil", stepProgress.StepKind);
        Assert.Equal(MacroStepOutcome.Succeeded, stepProgress.Outcome);
    }

    // ---------------------------------------------------------------
    // one macro at a time
    //
    // RETIRED 2026-09-09: `SecondConcurrentRun_RefusedAsBusy_WhileOneInFlight`
    // lived here. It was the last survivor of the single-flag era and
    // asserted that a second run of the SAME macro while one was in flight
    // returned MacroRunOutcome.Busy. The commander's ruling made that
    // outcome Cancelled, and the test's two halves now live apart, each
    // still pinned: a DIFFERENT macro is still refused
    // (`DifferentMacro_RefusedAsBusy_WhileFirstMacroStillInjecting`), and
    // the SAME macro stops instead
    // (`SameMacro_PressedAgainDuringInjection_StopsTheRun_AndBothPressesReportStopped`,
    // which also pins that the second press executes no steps of its own -
    // exactly the no-overlap claim this test was really about). Retired
    // rather than edited into agreement: nothing was left for it to guard
    // that those two do not.
    // ---------------------------------------------------------------

    [Fact]
    public async Task AfterARunCompletes_ARunnerAcceptsAnotherRun_NotPermanentlyBusy()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var firstRun = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var firstResult = await firstRun.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, firstResult.Outcome);

        var secondRun = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var secondResult = await secondRun.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, secondResult.Outcome);
        Assert.Equal(2, injector.DownCount);
    }

    // ---------------------------------------------------------------
    // One lock and one switch (2026-09-09) - the injection lock (guards the
    // keyboard against a DIFFERENT macro, released once the run's last
    // press-emitting step completes) and the same-macro STOP: a second
    // press of a running macro's own button cancels the run in flight
    // instead of refusing it, per the commander's ruling. See MacroRunner's
    // own remarks on _injecting/_runningMacroIds and ref/docs/macros.md's
    // "One lock and one switch" section for the full reasoning.
    // ---------------------------------------------------------------

    [Fact]
    public async Task DifferentMacro_RefusedAsBusy_WhileFirstMacroStillInjecting()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a", new PressStep("ActionA", Repeat: 1, Hold: null));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        // A's single press hasn't held long enough to complete yet - the
        // keyboard is still claimed.
        var runA = runner.RunAsync(macroA, bindings);
        Assert.False(runA.IsCompleted);

        // .WaitAsync(RunHangGuard) even on a call expected to resolve
        // synchronously as Busy - see this file's own O27 lesson: if a
        // mutation ever breaks the synchronous-refusal guarantee, this call
        // would actually start executing macroB and need clock advances
        // this test never supplies, hanging the whole suite instead of
        // failing cleanly.
        var resultB = await runner.RunAsync(macroB, bindings).WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Busy, resultB.Outcome);
        // Pinned by content, not merely non-empty (this task's own brief -
        // a refusal message with only a NotEmpty assertion has already
        // shipped once and could have said anything).
        Assert.Equal(MacroRunResult.InjectionBusy.FailureReason, resultB.FailureReason);
        Assert.DoesNotContain("macro-a", resultB.FailureReason);

        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        var resultA = await runA.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, resultA.Outcome);
    }

    [Fact]
    public async Task DifferentMacro_AcceptedOnceInjectionEnds_WhileFirstMacroStillWaiting()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a",
            new PressStep("ActionA", Repeat: 1, Hold: null),
            new WaitStep(TimeSpan.FromSeconds(5)));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);

        // A's press completes - the injection lock releases even though A's
        // run continues into its wait step. Must reliably observe that
        // release before starting B below (O42): a bounded guess here could
        // undershoot and see B refused as Busy instead of accepted.
        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.False(runA.IsCompleted);

        // B, a DIFFERENT macro id, is accepted and runs to completion while
        // A is still sitting in its 5s wait.
        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultB = await runB.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, resultB.Outcome);
        Assert.False(runA.IsCompleted); // still waiting - unaffected by B

        clock.Advance(TimeSpan.FromSeconds(5));
        var resultA = await runA.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, resultA.Outcome);
    }

    /// <summary>
    /// The exact message a stopped run reports, pinned as a literal in one
    /// place. Both halves of the exchange (the run that was stopped and the
    /// press that stopped it) must say this, and the panel puts it on screen
    /// as a toast whenever <c>fired</c> is false - so this is copy the
    /// commander actually reads, not an internal string. Pinned by CONTENT,
    /// deliberately: a refusal message asserted only as non-empty has
    /// already shipped once in this project and could have said anything.
    /// </summary>
    private const string StoppedReason = "Macro 'stop-macro' stopped - you pressed it again.";

    [Fact]
    public async Task SameMacro_PressedAgainDuringInjection_StopsTheRun_AndBothPressesReportStopped()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"));
        var macroA = MacroWithId("stop-macro", new PressStep("ActionA", Repeat: 3, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);
        Assert.False(runA.IsCompleted); // still holding the first press's key down

        // The same button, pressed again. Resolves synchronously, exactly as
        // the refusal it replaced did - .WaitAsync(RunHangGuard) anyway, per
        // this file's O27 lesson.
        var secondPress = await runner.RunAsync(macroA, bindings).WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);
        Assert.NotEqual(MacroRunOutcome.Busy, secondPress.Outcome); // NOT a refusal any more
        Assert.Equal(StoppedReason, secondPress.FailureReason);

        // The chord already in flight is allowed to finish releasing (one
        // hold) - then the run stops instead of going on to repeats 2 and 3.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultA = await runA.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Cancelled, resultA.Outcome);
        Assert.NotEqual(MacroRunOutcome.Success, resultA.Outcome); // it did not succeed
        Assert.NotEqual(MacroRunOutcome.Aborted, resultA.Outcome); // and nothing failed
        Assert.Equal(StoppedReason, resultA.FailureReason);
        Assert.Null(resultA.FailedStepIndex);

        Assert.Equal(1, injector.DownCount); // repeats 2 and 3 never happened
        Assert.Equal(1, injector.UpCount);
    }

    [Fact]
    public async Task StopArrivingDuringTheLastPressOfAStep_IsActedOnAtTheNextStepBoundary_RunningNoFurtherStep()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("stop-macro",
            new PressStep("ActionA", Repeat: 1, Hold: null),
            new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);
        var secondPress = await runner.RunAsync(macroA, bindings).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);

        // Step 0's single press has no trailing inter-press gap for the stop
        // to be noticed in, and its hold is deliberately not cancellable -
        // so the boundary between step 0 and step 1 is the ONLY place left
        // that can act on it. This is the test that keeps that check alive.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultA = await runA.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Cancelled, resultA.Outcome);
        Assert.Equal(1, injector.DownCount);
        Assert.DoesNotContain(injector.Events, e => e.Key.ScanCode == 0x12); // ActionB (Key_E) never fired
        AssertNothingLeftHeld(injector.Events);
    }

    [Fact]
    public async Task SameMacro_PressedAgainWhileOnlyWaiting_StopsTheWait_InsteadOfRefusingAsBusy()
    {
        // SUPERSEDED 2026-09-09. This test was
        // `SameMacro_RefusedAsBusy_ThroughoutTheWholeRun_IncludingTheWait`,
        // and it pinned ver-0.29.1.0-dev's duplicate-run GUARD: a re-press
        // was refused as MacroRunOutcome.Busy with the reason "Macro
        // '{id}' is already running and waiting on the game. Try again once
        // it finishes." for the run's whole duration, wait included. The
        // commander's ruling of 2026-09-09 ("to stop a running macro, just
        // hit the same key again while it's going") replaced that refusal
        // with a cancel, so this is not a weakened assertion - it is the
        // opposite claim, and the old expectation is recorded here rather
        // than deleted.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"));
        var macroA = MacroWithId("stop-macro",
            new PressStep("ActionA", Repeat: 1, Hold: null),
            new WaitStep(TimeSpan.FromSeconds(5)));

        var runA = runner.RunAsync(macroA, bindings);

        // A's press completes and the injection lock releases; A is now
        // only waiting on the game, holding nothing.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.False(runA.IsCompleted);

        var secondPress = await runner.RunAsync(macroA, bindings).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);
        Assert.Equal(StoppedReason, secondPress.FailureReason);

        // No clock advance at all from here: the 5-second wait is cut short
        // by the stop rather than waited out. A run that ignored the stop
        // would still be sitting in that wait when RunHangGuard fires,
        // because nothing is ever going to advance this clock again.
        var resultA = await runA.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, resultA.Outcome);
        Assert.Equal(StoppedReason, resultA.FailureReason);
        Assert.Equal(1, injector.DownCount);
        Assert.Equal(1, injector.UpCount);
    }

    // ---------------------------------------------------------------
    // TryStart (2026-09-17, O28) - the synchronous half of RunAsync, split
    // out so POST /api/press can answer "it started" without waiting for
    // the run to end. Both guard decisions and the Changed raise stay on
    // the synchronous side; the pins below hold that boundary in place.
    // ---------------------------------------------------------------

    [Fact]
    public async Task TryStart_GenuineStart_ReturnsTrue_WithTheRunInFlight_AndTheIdAlreadyPublished()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"));
        var macroA = MacroWithId("macro-a", new PressStep("ActionA", Repeat: 1, Hold: null));
        var changedRaised = 0;
        runner.Changed += () => changedRaised++;

        var started = runner.TryStart(macroA, bindings, out var run);

        // Everything a caller needs to answer "it started" is true the
        // instant TryStart returns - no await in between: the run is in
        // flight, the id is published, and the start edge has been raised.
        Assert.True(started);
        Assert.False(run.IsCompleted);
        Assert.Contains("macro-a", runner.RunningMacroIds);
        Assert.Equal(1, changedRaised);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await run.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Empty(runner.RunningMacroIds);
        Assert.Equal(2, changedRaised);
    }

    [Fact]
    public async Task TryStart_DifferentMacroWhileInjecting_ReturnsFalse_WithBusyAlreadyCompleted_AndNothingPublished()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a", new PressStep("ActionA", Repeat: 1, Hold: null));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        Assert.True(runner.TryStart(macroA, bindings, out var runA));
        var changedRaised = 0;
        runner.Changed += () => changedRaised++;

        var startedB = runner.TryStart(macroB, bindings, out var runB);

        // Refused synchronously: false, and the task handed back is ALREADY
        // complete carrying the refusal - a caller awaiting it uniformly
        // never blocks. Nothing was published for B and no edge was raised,
        // the same no-flash guarantee RunningMacroIds' own remarks make.
        Assert.False(startedB);
        Assert.True(runB.IsCompleted);
        var resultB = await runB;
        Assert.Equal(MacroRunOutcome.Busy, resultB.Outcome);
        Assert.Equal(MacroRunResult.InjectionBusy.FailureReason, resultB.FailureReason);
        Assert.DoesNotContain("macro-b", runner.RunningMacroIds);
        Assert.Equal(0, changedRaised);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runA.WaitAsync(RunHangGuard)).Outcome);
    }

    [Fact]
    public async Task TryStart_SameMacroAgain_ReturnsFalse_WithCancelledAlreadyCompleted_AndStopsTheRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"));
        var macroA = MacroWithId("stop-macro", new PressStep("ActionA", Repeat: 3, Hold: null));

        Assert.True(runner.TryStart(macroA, bindings, out var runA));

        var startedAgain = runner.TryStart(macroA, bindings, out var secondPress);

        Assert.False(startedAgain);
        Assert.True(secondPress.IsCompleted);
        var secondResult = await secondPress;
        Assert.Equal(MacroRunOutcome.Cancelled, secondResult.Outcome);
        Assert.Equal(StoppedReason, secondResult.FailureReason);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultA = await runA.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Cancelled, resultA.Outcome);
        Assert.Equal(1, injector.DownCount); // repeats 2 and 3 never happened
    }

    /// <summary>
    /// <b>The ordering pin.</b> <c>_runningMacroIds.TryAdd</c> is decided
    /// BEFORE the <c>_injecting</c> compare-exchange, and the split into
    /// <see cref="MacroRunner.TryStart"/> must not reorder them. The one
    /// scenario that tells the two orders apart: macro A is running but only
    /// WAITING (its press is done, so the keyboard is free), macro B is
    /// mid-injection and holds the keyboard, and A's own button is pressed
    /// again. TryAdd-first sees A already running and stops it -
    /// <see cref="MacroRunOutcome.Cancelled"/>. Lock-first would see B
    /// holding the keyboard and refuse the commander's stop as
    /// <see cref="MacroRunOutcome.Busy"/> - and A would go on waiting.
    /// </summary>
    [Fact]
    public async Task TryStart_SameMacroPressedAgain_WhileADifferentMacroHoldsTheKeyboard_IsCancelled_NotBusy()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("stop-macro",
            new PressStep("ActionA", Repeat: 1, Hold: null),
            new WaitStep(TimeSpan.FromSeconds(5)));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 3, Hold: null));

        Assert.True(runner.TryStart(macroA, bindings, out var runA));

        // A's press completes and releases the keyboard; A is now only
        // waiting. Observed reliably, not guessed (O42).
        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.False(runA.IsCompleted);

        // B takes the keyboard and keeps it (three presses, none complete).
        Assert.True(runner.TryStart(macroB, bindings, out var runB));
        Assert.False(runB.IsCompleted);

        // A's own button again, while B holds the keyboard.
        var startedAgain = runner.TryStart(macroA, bindings, out var secondPressOfA);

        Assert.False(startedAgain);
        Assert.True(secondPressOfA.IsCompleted);
        var secondResultOfA = await secondPressOfA;
        Assert.Equal(MacroRunOutcome.Cancelled, secondResultOfA.Outcome);
        Assert.NotEqual(MacroRunOutcome.Busy, secondResultOfA.Outcome);
        Assert.Equal(StoppedReason, secondResultOfA.FailureReason);

        // And A really did stop - its 5s wait is cut short with no further
        // clock advance, exactly as the waiting-stop pin above proves.
        Assert.Equal(MacroRunOutcome.Cancelled, (await runA.WaitAsync(RunHangGuard)).Outcome);

        // B was never touched by any of this.
        Assert.False(runB.IsCompleted);
        Assert.Contains("macro-b", runner.RunningMacroIds);
        Assert.DoesNotContain("stop-macro", runner.RunningMacroIds);
    }

    [Fact]
    public async Task StoppedRun_ReleasesBothGuards_SoADifferentMacroRuns_AndAThirdPressStartsAFreshRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("stop-macro", new PressStep("ActionA", Repeat: 3, Hold: null));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        // Stopped mid-INJECTION on purpose: the injection lock is still held
        // at the moment of the stop, so only RunAsync's `finally` can
        // release it. A stop that left it set would wedge every macro.
        var runA = runner.RunAsync(macroA, bindings);
        var secondPress = await runner.RunAsync(macroA, bindings).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Cancelled, (await runA.WaitAsync(RunHangGuard)).Outcome);

        // Guard 1, the injection lock: a DIFFERENT macro runs to completion.
        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runB.WaitAsync(RunHangGuard)).Outcome);

        // Guard 2, the running-id entry: a THIRD press of the stopped macro
        // starts a fresh run rather than stopping a ghost. Not completed
        // synchronously is the tell - a leftover entry would have returned
        // Cancelled right here without running anything.
        var runC = runner.RunAsync(macroA, bindings);
        Assert.False(runC.IsCompleted);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultC = await runC.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, resultC.Outcome);
        // 1 (A, stopped after one press) + 1 (B) + 3 (C, complete).
        Assert.Equal(5, injector.DownCount);
        Assert.Equal(5, injector.UpCount);
    }

    [Fact]
    public async Task StopArrivingMidChord_StillReleasesEveryKeyThatWentDown_InReverseOrder()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildChordBindings();
        var macroA = MacroWithId("stop-macro", new PressStep("ChordAction", Repeat: 2, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);

        // Past the modifier settle gap: both modifiers AND the main key are
        // down right now, and the hold is in flight. This is the one moment
        // in a macro's life where a stop could strand a key in the
        // commander's game. Reliably observed (O42), not guessed at - a
        // bounded settle could read this before the main key's down event
        // lands.
        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.ModifierSettleGap);
        Assert.Equal(3, injector.DownCount);
        Assert.Equal(0, injector.UpCount);

        var secondPress = await runner.RunAsync(macroA, bindings).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);

        // The chord is NOT cut short: its hold still runs to completion and
        // its release still happens. One pump, and nothing is left held.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultA = await runA.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, resultA.Outcome);

        Assert.Equal(3, injector.DownCount); // the second repeat never started
        Assert.Equal(3, injector.UpCount);
        AssertNothingLeftHeld(injector.Events);

        // Released in the documented order: main key first, then the
        // modifiers in reverse of the order they went down. Literal
        // scancodes, not values read back out of the chord the runner used.
        var ups = injector.Events.Where(e => !e.IsDown).Select(e => e.Key.ScanCode).ToArray();
        Assert.Equal(new ushort[] { 0x11, 0x38, 0x1D }, ups); // Key_W, Key_LeftAlt, Key_LeftControl
    }

    [Fact]
    public async Task PressChord_InjectorThrowsWithModifiersDown_StillReleasesThem_AndOnlyThem()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        // Throws on the MAIN key's down, i.e. with both modifiers already
        // held. Nothing in production throws here today - this is the
        // second half of the no-stranded-key guarantee, pinning that the
        // release path is a `finally` rather than three statements that
        // happen to be next in line.
        var injector = new ThrowingKeyInjector(clock, throwOnScanCode: 0x11);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildChordBindings();
        var macro = MacroWithId("throwing-macro", new PressStep("ChordAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.ModifierSettleGap);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runTask.WaitAsync(RunHangGuard));

        var events = injector.Events;
        Assert.Equal(2, events.Count(e => e.IsDown));  // both modifiers went down
        Assert.Equal(2, events.Count(e => !e.IsDown)); // both came back up
        AssertNothingLeftHeld(injector.Events);

        // ...and ONLY them: the main key never went down, so no key-up for
        // it may be sent. A blind release path would fabricate a key-up for
        // a key Elite never saw pressed.
        Assert.DoesNotContain(events, e => e.Key.ScanCode == 0x11);
        Assert.Equal(new ushort[] { 0x38, 0x1D }, events.Where(e => !e.IsDown).Select(e => e.Key.ScanCode).ToArray());
    }

    [Fact]
    public async Task StoppedRun_TabTrackerBelief_MatchesOnlyThePressesThatActuallyLanded()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // anchors at Navigation
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));

        // Navigation -> Galaxy is 3 presses (Navigation -> Transactions ->
        // Contacts -> Galaxy), so a run stopped after ONE press must leave
        // the belief at Transactions - not at Galaxy (where the whole route
        // would have gone) and not back at Navigation.
        var macro = MacroWithId("stop-macro", new GotoLeftPanelTabStep(PanelTab.Galaxy));
        Assert.Equal(PanelTab.Navigation, tabTracker.CurrentTab);

        var runA = runner.RunAsync(macro, bindings);
        var secondPress = await runner.RunAsync(macro, bindings).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultA = await runA.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, resultA.Outcome);

        Assert.Equal(1, injector.DownCount); // exactly one press landed
        Assert.Equal(PanelTab.Transactions, tabTracker.CurrentTab);

        // The belief stays usable rather than becoming a lie: asked to
        // finish the journey, the tracker now wants the 2 presses that
        // really are left.
        Assert.Equal(2, tabTracker.PressesToReach(PanelTab.Galaxy));
    }

    [Fact]
    public async Task SameMacro_AcceptedAgain_OnceAWaitingRunFullyCompletes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"));
        var macroA = MacroWithId("dup-macro",
            new PressStep("ActionA", Repeat: 1, Hold: null),
            new WaitStep(TimeSpan.FromSeconds(5)));

        // Must reliably observe the press completing (and the WaitStep
        // arming) before the manual Advance(5s) below (O42) - a bounded
        // guess that undershoots would let that advance land before the
        // WaitStep's timer even exists, losing it and hanging the run.
        var firstRun = runner.RunAsync(macroA, bindings);
        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        clock.Advance(TimeSpan.FromSeconds(5));
        var firstResult = await firstRun.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, firstResult.Outcome);

        var secondRun = runner.RunAsync(macroA, bindings);
        Assert.False(secondRun.IsCompleted); // accepted, not refused as Busy - now genuinely running

        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        clock.Advance(TimeSpan.FromSeconds(5));
        var secondResult = await secondRun.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, secondResult.Outcome);
        Assert.Equal(2, injector.DownCount);
    }

    // ---------------------------------------------------------------
    // progress and logging
    // ---------------------------------------------------------------

    [Fact]
    public async Task RunAsync_ReportsProgress_OncePerStep_WithIndexKindOutcomeAndElapsed()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(
            new PressStep("TestAction", Repeat: 1, Hold: null),
            new WaitStep(TimeSpan.FromMilliseconds(50)));

        var reports = new List<MacroStepProgress>();
        var progress = new SynchronousProgress<MacroStepProgress>(reports.Add);

        var runTask = runner.RunAsync(macro, bindings, progress);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, TimeSpan.FromMilliseconds(50));
        await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(2, reports.Count);
        Assert.Equal(0, reports[0].StepIndex);
        Assert.Equal("press", reports[0].StepKind);
        Assert.Equal(MacroStepOutcome.Succeeded, reports[0].Outcome);
        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, reports[0].Elapsed);

        Assert.Equal(1, reports[1].StepIndex);
        Assert.Equal("wait", reports[1].StepKind);
        Assert.Equal(TimeSpan.FromMilliseconds(50), reports[1].Elapsed);
    }

    [Fact]
    public async Task RunAsync_LogsEachStep_UnderMacroCategory()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithSteps(new PressStep("TestAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        clock.Advance(MacroTimingDefaults.DefaultHoldDuration);
        await runTask.WaitAsync(RunHangGuard);

        var events = log.Snapshot();
        Assert.Contains(events, e => e.Category == "Macro");
    }

    [Fact]
    public async Task RunAsync_OnAbort_LogsTheReason()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new PressStep("NotBound", Repeat: 1, Hold: null));

        await runner.RunAsync(macro, bindings);

        var events = log.Snapshot();
        Assert.Contains(events, e => e.Category == "Macro" && e.Message.Contains("aborted"));
    }

    // ---------------------------------------------------------------
    // waitForEdge - journal-backed, no timeout (LC18/LC19/LC17)
    // ---------------------------------------------------------------

    [Fact]
    public async Task WaitForEdgeStep_SucceedsAssoonAsASucceedOnEdgeArrives_NoKeysSent()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);
        Assert.False(runTask.IsCompleted);

        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Empty(injector.Events);
    }

    [Fact]
    public async Task WaitForEdgeStep_TwoSucceedOnTokens_EitherOneSatisfiesIt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:LaunchVessel"), EdgeCondition.Parse("Journal:DockSRV") },
            new[] { "Journal:LaunchVessel", "Journal:DockSRV" },
            Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);
        journal.Record(new JournalEvent("DockSRV", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task WaitForEdgeStep_FailOnEdgeArrives_AbortsWithReason_AndSurfacesTheDetailField()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            new[] { EdgeCondition.Parse("Journal:DockingDenied") }, new[] { "Journal:DockingDenied" },
            "Reason", TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);
        journal.Record(new JournalEvent("DockingDenied", null, new Dictionary<string, string> { ["Reason"] = "Distance" }));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        Assert.Contains("Distance", result.FailureReason);
    }

    [Fact]
    public async Task WaitForEdgeStep_FailOnFires_WithoutFailureDetailField_ReasonNamesTheTokenAlone()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            new[] { EdgeCondition.Parse("Journal:DockingDenied") }, new[] { "Journal:DockingDenied" },
            FailureDetailField: null, Timeout: TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);
        journal.Record(new JournalEvent("DockingDenied", null, new Dictionary<string, string> { ["Reason"] = "NoSpace" }));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Contains("Journal:DockingDenied", result.FailureReason);
        Assert.DoesNotContain("NoSpace", result.FailureReason);
    }

    [Fact]
    public async Task WaitForEdgeStep_EventRecordedBeforeThisStepButAfterTheRunStarted_StillCounts()
    {
        // The 0-second gap LC18 measured: DockingRequested and DockingGranted
        // landed in the same second. If the watermark were taken at the wait
        // step's own turn rather than once at RunAsync's start, an edge
        // recorded during an EARLIER step (here, the press step's own hold)
        // would already be "in the past" by the time the wait step marks,
        // and this step would hang forever - see MacroRunner.RunAsync's own
        // comment on runWatermark.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("RequestAction", "Key_Space"));
        var macro = MacroWithSteps(
            new PressStep("RequestAction", Repeat: 1, Hold: null),
            new WaitForEdgeStep(
                new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
                Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);

        // The event arrives WHILE the press step's hold is still elapsing -
        // i.e. before the waitForEdge step has even started.
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task WaitForEdgeStep_EventRecordedBeforeTheRunStarted_DoesNotSatisfyIt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        // A stale grant from long before this run even started must not be
        // read as an answer to THIS request.
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, TimeSpan.FromSeconds(30)));

        var runTask = runner.RunAsync(macro, bindings);
        await Task.Delay(50);
        Assert.False(runTask.IsCompleted);

        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));
        var result = await runTask.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task WaitForEdgeStep_IsCancellable_WhileWaiting()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, TimeSpan.FromSeconds(30)));
        using var cts = new CancellationTokenSource();

        var runTask = runner.RunAsync(macro, bindings, cancellationToken: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask.WaitAsync(RunHangGuard));
    }

    // ---------------------------------------------------------------
    // waitForEdge: timeoutMs (O28, 2026-09-08 - bounded, was unbounded)
    // ---------------------------------------------------------------

    [Fact]
    public async Task WaitForEdgeStep_EdgeArrivesInsideTheTimeoutWindow_StillSucceeds()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var timeout = TimeSpan.FromSeconds(20);
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, timeout));

        var runTask = runner.RunAsync(macro, bindings);

        // Advance well into the window (15s of a 20s timeout), then arrive -
        // proves the step is still open for business partway through, not
        // just at t=0 like the no-timeout sibling tests above.
        await PumpAsync(clock, TimeSpan.FromSeconds(15));
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task WaitForEdgeStep_NoEdgeArrivesWithinTheTimeout_AbortsAsFailed_WithThePinnedReason()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings();
        var timeout = TimeSpan.FromSeconds(20);
        var macro = MacroWithSteps(new WaitForEdgeStep(
            new[] { EdgeCondition.Parse("Journal:DockingGranted") }, new[] { "Journal:DockingGranted" },
            Array.Empty<EdgeCondition>(), Array.Empty<string>(), null, timeout));

        var runTask = runner.RunAsync(macro, bindings);

        // No journal event ever arrives - just let the whole window elapse.
        await PumpAsync(clock, timeout);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(0, result.FailedStepIndex);
        // Pinned by substance, not merely NotEmpty (see this task's own
        // brief on why that failure shape has already shipped once): the
        // exact message text a commander would read off the log, naming
        // the token, the elapsed bound, and the causality-filter
        // explanation from WaitForEdgeStep's own remarks.
        Assert.Equal(
            "waitForEdge [Journal:DockingGranted] timed out after 20000ms - " +
            "no grant or denial arrived within the timeout, so the request may not have been made. Check the panel.",
            result.FailureReason);
    }

    // ---------------------------------------------------------------
    // gotoLeftPanelTab - asks PanelTabTracker how far to move (panel-tab-tracking.md)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GotoLeftPanelTabStep_KnownTab_PressesExactlyTheComputedCount_AndAdvancesTheTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // anchors at Navigation
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macro = MacroWithSteps(new GotoLeftPanelTabStep(PanelTab.Contacts));

        var runTask = runner.RunAsync(macro, bindings);

        // Navigation -> Contacts is 2 presses (Navigation -> Transactions -> Contacts).
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(2, injector.DownCount);
        Assert.Equal(PanelTab.Contacts, tabTracker.CurrentTab);
    }

    [Fact]
    public async Task GotoLeftPanelTabStep_AlreadyOnTarget_SendsNoKeysAtAll()
    {
        // The payoff panel-tab-tracking.md names explicitly: a SECOND run
        // that left the panel on the target tab needs no tab presses at all.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump();
        tabTracker.RecordOwnTabAdvance(); // Navigation -> Transactions
        tabTracker.RecordOwnTabAdvance(); // Transactions -> Contacts
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        // Deliberately no binding for the advance action at all - if the
        // step tried to press it, this would abort as unbound rather than
        // succeed, so a green result here is proof no press was attempted.
        var bindings = BuildBindings();
        var macro = MacroWithSteps(new GotoLeftPanelTabStep(PanelTab.Contacts));

        var result = await runner.RunAsync(macro, bindings);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Empty(injector.Events);
    }

    [Fact]
    public async Task GotoLeftPanelTabStep_LowConfidence_StillPressesTheComputedRoute_AndLogsAWarning()
    {
        // Reversed 2026-09-08 (ref/docs/panel-tab-tracking.md, "Reversed
        // 2026-09-08"): this step used to abort with zero injections once
        // the tracker lost track. The user's governing decision - "We
        // should never NOT be allowed to use a macro" - removed that
        // refusal entirely. An uncredited edge now only drops confidence to
        // Low and leaves the believed tab (Navigation, the Fix 7
        // presumption) untouched, so PressesToReach(Contacts) still returns
        // 2 and the macro presses it - while logging a WARN naming the
        // believed tab, so a bad outcome stays explainable.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordLeftPanelEdge(); // uncredited - Low confidence, still Navigation
        Assert.Equal(PanelTab.Navigation, tabTracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tabTracker.LeftTabConfidence);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macro = MacroWithSteps(new GotoLeftPanelTabStep(PanelTab.Contacts));

        var runTask = runner.RunAsync(macro, bindings);
        // Navigation -> Contacts is 2 presses.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(2, injector.DownCount);
        Assert.Equal(PanelTab.Contacts, tabTracker.CurrentTab);

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("LOW-confidence", warning.Message);
        Assert.Contains("Navigation", warning.Message);
        Assert.Contains("Contacts", warning.Message);
    }

    [Fact]
    public async Task GotoLeftPanelTabStep_HighConfidence_LogsNoWarning()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // High confidence, Navigation
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macro = MacroWithSteps(new GotoLeftPanelTabStep(PanelTab.Contacts));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.DoesNotContain(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Message.Contains("LOW-confidence"));
    }

    [Fact]
    public async Task GotoLeftPanelTabStep_AdvanceActionNotBound_Aborts_WhenPressesAreActuallyNeeded()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings(); // CycleNextPanel unbound
        var macro = MacroWithSteps(new GotoLeftPanelTabStep(PanelTab.Contacts));

        var result = await runner.RunAsync(macro, bindings);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Empty(injector.Events);
    }

    // ---------------------------------------------------------------
    // A press step naming FocusLeftPanel arms PanelTabTracker (panel-tab-tracking.md)
    // ---------------------------------------------------------------

    [Fact]
    public async Task PressStep_FocusLeftPanelAction_ArmsTheTabTracker_SoItsGuiFocusEdgeIsNotMistakenForExternal()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // anchors at Navigation, so we can tell if it survives
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.LeftPanelToggleAction, "Key_1"));
        var macro = MacroWithSteps(new PressStep(PanelTabTracker.LeftPanelToggleAction, Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, result.Outcome);

        // Simulate the GuiFocus edge this press produces, exactly as
        // ServerHostBuilder's own subscription reports one.
        tabTracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tabTracker.CurrentTab); // armed - not mistaken for external

        // A SECOND edge with no further press from this macro IS external -
        // Reversed 2026-09-08: this drops confidence to Low rather than
        // nulling the tab; the tab itself stays Navigation.
        tabTracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tabTracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tabTracker.LeftTabConfidence);
    }

    [Fact]
    public async Task PressStep_SomeOtherAction_DoesNotArmTheTabTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings(("SomeOtherAction", "Key_1"));
        var macro = MacroWithSteps(new PressStep("SomeOtherAction", Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await runTask.WaitAsync(RunHangGuard);

        tabTracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tabTracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tabTracker.LeftTabConfidence);
    }

    // ---------------------------------------------------------------
    // A plain press step naming CycleNextPanel/CyclePreviousPanel also feeds
    // PanelTabTracker (Fix 6, panel-tab-tracking.md) - not just gotoLeftPanelTab.
    // Before this fix, a macro's plain press step for these two actions
    // never touched the tracker at all (the same hole the plain single-tap
    // press had, just inside a macro instead of at /api/press).
    // ---------------------------------------------------------------

    [Fact]
    public async Task PressStep_CycleNextPanelAction_AdvancesTheTabTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // Navigation
        tabTracker.RecordOwnLeftPanelToggle(); // Fix 7: belief -> Left, as a real FocusLeftPanel press would set
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macro = MacroWithSteps(new PressStep(PanelTabTracker.AdvanceTabAction, Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(PanelTab.Transactions, tabTracker.CurrentTab);
    }

    [Fact]
    public async Task PressStep_CycleNextPanelAction_Repeat3_AdvancesTheTabTrackerByThree_NotOne()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // Navigation
        tabTracker.RecordOwnLeftPanelToggle(); // Fix 7: belief -> Left
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.AdvanceTabAction, "Key_E"));
        var macro = MacroWithSteps(new PressStep(PanelTabTracker.AdvanceTabAction, Repeat: 3, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        for (var i = 0; i < 3; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            if (i < 2)
            {
                await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
            }
        }

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        // Navigation -> Transactions -> Contacts -> Galaxy
        Assert.Equal(PanelTab.Galaxy, tabTracker.CurrentTab);
    }

    [Fact]
    public async Task PressStep_CyclePreviousPanelAction_RetreatsTheTabTracker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // Navigation
        tabTracker.RecordOwnLeftPanelToggle(); // Fix 7: belief -> Left
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), tabTracker, clock, log);
        var bindings = BuildBindings((PanelTabTracker.RetreatTabAction, "Key_Q"));
        var macro = MacroWithSteps(new PressStep(PanelTabTracker.RetreatTabAction, Repeat: 1, Hold: null));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(PanelTab.Galaxy, tabTracker.CurrentTab);
    }

    // ---------------------------------------------------------------
    // shipped macro
    // ---------------------------------------------------------------

    // [SUPERSEDED 2026-09-17] Until this date this test loaded the shipped
    // single-system "Power to Weapons" preset macro (pip-preset-weapons.json,
    // a 3-step reset/wait/divert-x4 shape). That macro, and its two
    // siblings (pip-preset-engines, pip-preset-shields), were retired in
    // favor of six pairwise combo macros. This test now exercises one of
    // those combos (pip-preset-weapons-engines) instead, whose shape is one
    // step longer: reset, wait, then the SECONDARY system's action x4
    // followed by the PRIMARY system's action x4.
    [Fact]
    public async Task ShippedPipPresetMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.pip-preset-weapons-engines.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("pip-preset-weapons-engines", macro.Id);
        Assert.Equal(4, macro.Steps.Count);

        var resetStep = Assert.IsType<PressStep>(macro.Steps[0]);
        Assert.Equal("ResetPowerDistribution", resetStep.Action);
        Assert.Equal(1, resetStep.Repeat);

        var waitStep = Assert.IsType<WaitStep>(macro.Steps[1]);

        var secondaryStep = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal("IncreaseEnginesPower", secondaryStep.Action);
        Assert.Equal(4, secondaryStep.Repeat);

        var primaryStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("IncreaseWeaponsPower", primaryStep.Action);
        Assert.Equal(4, primaryStep.Repeat);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("ResetPowerDistribution", "Key_1"),
            ("IncreaseEnginesPower", "Key_2"),
            ("IncreaseWeaponsPower", "Key_3"));

        var runTask = runner.RunAsync(macro, bindings);

        // Step 0: press ResetPowerDistribution, repeat 1 - one hold, no gap.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        // Step 1: wait.
        await PumpAsync(clock, waitStep.Duration);
        // Step 2: press IncreaseEnginesPower (secondary), repeat 4 - hold/gap x4, no trailing gap.
        for (var i = 0; i < secondaryStep.Repeat; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            if (i < secondaryStep.Repeat - 1)
            {
                await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
            }
        }
        // Step 3: press IncreaseWeaponsPower (primary), repeat 4 - hold/gap x4, no trailing gap.
        for (var i = 0; i < primaryStep.Repeat; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            if (i < primaryStep.Repeat - 1)
            {
                await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
            }
        }

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(18, injector.Events.Count); // (down,up) x (1 reset + 4 secondary + 4 primary)

        Scancodes.TryGet("Key_1", out var resetKey);
        Scancodes.TryGet("Key_2", out var secondaryKey);
        Scancodes.TryGet("Key_3", out var primaryKey);

        // First press is the reset action.
        Assert.Equal(resetKey, injector.Events[0].Key);
        Assert.True(injector.Events[0].IsDown);
        Assert.Equal(resetKey, injector.Events[1].Key);
        Assert.False(injector.Events[1].IsDown);

        // Next four presses are the secondary action.
        for (var i = 0; i < 4; i++)
        {
            var down = injector.Events[2 + i * 2];
            var up = injector.Events[3 + i * 2];
            Assert.Equal(secondaryKey, down.Key);
            Assert.True(down.IsDown);
            Assert.Equal(secondaryKey, up.Key);
            Assert.False(up.IsDown);
        }

        // Final four presses are the primary action.
        for (var i = 0; i < 4; i++)
        {
            var down = injector.Events[10 + i * 2];
            var up = injector.Events[11 + i * 2];
            Assert.Equal(primaryKey, down.Key);
            Assert.True(down.IsDown);
            Assert.Equal(primaryKey, up.Key);
            Assert.False(up.IsDown);
        }
    }

    /// <summary>
    /// Loads the real shipped <c>disembark.json</c>.
    ///
    /// [SUPERSEDED 2026-09-17] Until this date the macro was a single
    /// unconditional sequence of four steps -
    /// <c>require ["Docked","GuiFocus:NoFocus"]</c>, <c>UI_Down x3</c>,
    /// <c>wait 150</c>, <c>UI_Select x1</c> - and this test asserted
    /// <c>macro.Steps.Count == 4</c> against exactly that. It now has ONE
    /// top-level step, a <c>branch</c> on <c>Docked</c>, whose <c>then</c>
    /// arm is that same four-step sequence (minus the <c>Docked</c> token,
    /// which the branch itself now tests) and whose <c>else</c> arm is the
    /// surface-landing route the commander confirmed directly: open the
    /// ship's own radar panel, one Down, confirm. The old expectation is
    /// recorded here rather than deleted - it was correct, and it was
    /// superseded by a feature, not weakened to pass.
    /// </summary>
    [Fact]
    public async Task ShippedDisembarkMacro_Docked_TakesTheStationArm_AndProducesTheExpectedInjectionSequence()
    {
        var macro = LoadShippedMacro("disembark");
        Assert.Equal("disembark", macro.Id);
        var branch = Assert.IsType<BranchStep>(Assert.Single(macro.Steps));
        Assert.Equal(new[] { "Docked" }, branch.ConditionTokens);

        Assert.Equal(4, branch.Then.Count);
        var requireStep = Assert.IsType<RequireStep>(branch.Then[0]);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var downStep = Assert.IsType<PressStep>(branch.Then[1]);
        Assert.Equal("UI_Down", downStep.Action);
        // Three, reduced from ten on 2026-09-07 after the commander ran the
        // macro for real: ten Down presses took 2413ms of a 2714ms macro and
        // they reported it as "way too many overclicks... it took too long".
        //
        // Three is not a guess. The docked list has three rows (LC14) and
        // CLAMPS rather than wrapping, so from ANY starting row three presses
        // reach the bottom - DISEMBARK - and further presses do nothing. Ten
        // was chosen when a station's list length was unknown, but over-
        // pressing only helps if the target is last, which is itself unmeasured
        // at a station. So the extra seven presses bought nothing and cost
        // 1.7 seconds every time.
        //
        // The commander's own steer, same day: "we shouldn't worry about
        // overclicks and just do our best to track where things are based on
        // behaviors that we CAN map."
        Assert.Equal(3, downStep.Repeat);

        var waitStep = Assert.IsType<WaitStep>(branch.Then[2]);

        var selectStep = Assert.IsType<PressStep>(branch.Then[3]);
        Assert.Equal("UI_Select", selectStep.Action);
        Assert.Equal(1, selectStep.Repeat);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        // Docked (Flags bit 0) and GuiFocus:NoFocus (0) - the two-condition
        // guard ref/docs/macros.md's "Disembark" section requires so the
        // Down/Select presses land on the docked overlay rather than some
        // other panel that happens to be open.
        store.UpdateSnapshot(new StatusSnapshot(1u, 0u, 0, GameRunning: true, SignedIn: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("UI_Down", "Key_S"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        // Step 0: branch - synchronous. Step 1: require - synchronous.
        // Step 2: press UI_Down, repeat 3 - hold/gap x3, no trailing gap.
        for (var i = 0; i < downStep.Repeat; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            if (i < downStep.Repeat - 1)
            {
                await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
            }
        }

        // Step 3: wait.
        await PumpAsync(clock, waitStep.Duration);

        // Step 4: press UI_Select, repeat 1 - one hold, no gap.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(8, injector.Events.Count); // (down,up) x (3 UI_Down + 1 UI_Select)

        Scancodes.TryGet("Key_S", out var downKey);
        Scancodes.TryGet("Key_Space", out var selectKey);

        // Indices come from downStep.Repeat rather than a literal. The count was
        // reduced from ten to three on 2026-09-07 and these hardcoded offsets
        // failed with a confusing "expected S, got Space" rather than pointing at
        // the count - the assertion that mattered was buried behind an offset
        // that had silently gone stale.
        for (var i = 0; i < downStep.Repeat; i++)
        {
            var down = injector.Events[i * 2];
            var up = injector.Events[i * 2 + 1];
            Assert.Equal(downKey, down.Key);
            Assert.True(down.IsDown);
            Assert.Equal(downKey, up.Key);
            Assert.False(up.IsDown);
        }

        var selectDown = injector.Events[downStep.Repeat * 2];
        var selectUp = injector.Events[(downStep.Repeat * 2) + 1];
        Assert.Equal(selectKey, selectDown.Key);
        Assert.True(selectDown.IsDown);
        Assert.Equal(selectKey, selectUp.Key);
        Assert.False(selectUp.IsDown);
    }

    /// <summary>
    /// Docked, but with a panel already open.
    ///
    /// [SUPERSEDED 2026-09-17] This was
    /// <c>ShippedDisembarkMacro_NotDocked_FiresAnywayWithWarning</c>, and its
    /// name had become false long before this change: it set
    /// <c>Flags = 1</c> (Docked) with <c>GuiFocus = InternalPanel</c>, so
    /// the thing it actually exercised was the <b>focus</b> half of the old
    /// two-token require, never the docked half. Renamed rather than left
    /// standing, per the same reasoning the file already applies elsewhere: a
    /// false name is read by more people than a passing assertion. The claim
    /// itself - the macro fires anyway and warns, never refuses
    /// (<c>never-gate-a-macro.md</c>) - is unchanged, and now runs against
    /// the <c>then</c> arm's own <c>require ["GuiFocus:NoFocus"]</c>.
    /// "Genuinely not docked" is a different scenario entirely now: it takes
    /// the other arm, and
    /// <see cref="ShippedDisembarkMacro_LandedOnAPlanet_TakesTheSurfaceArm"/>
    /// covers it.
    /// </summary>
    [Fact]
    public async Task ShippedDisembarkMacro_DockedButAPanelIsOpen_FiresAnywayWithWarning()
    {
        var macro = LoadShippedMacro("disembark");

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        // Docked, but a panel (InternalPanel = 1) is focused - the exact
        // scenario that used to abort before the require gate stopped
        // blocking macros on uncertainty (never-gate-a-macro.md).
        store.UpdateSnapshot(new StatusSnapshot(1u, 0u, 1, GameRunning: true, SignedIn: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("UI_Down", "Key_S"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        // then-arm: require, press UI_Down x3, wait 150, press UI_Select x1.
        for (var i = 0; i < 3; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            if (i < 2)
            {
                await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
            }
        }
        await PumpAsync(clock, TimeSpan.FromMilliseconds(150));
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(8, injector.Events.Count); // (down,up) x (3 UI_Down + 1 UI_Select)

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("GuiFocus:NoFocus", warning.Message);
    }

    /// <summary>
    /// The whole point of the feature, against the real shipped file: landed
    /// on a planet surface rather than docked, the macro takes the OTHER arm
    /// and presses a completely different sequence - open the ship's own
    /// radar panel, one Down, confirm. That sequence was confirmed directly
    /// by the commander in-game on 2026-09-17; it is not derived from
    /// anything.
    ///
    /// Asserted key by key, in order, rather than by count: a count alone
    /// would pass for the docked arm's three Downs plus a Select too.
    /// </summary>
    [Fact]
    public async Task ShippedDisembarkMacro_LandedOnAPlanet_TakesTheSurfaceArm()
    {
        var macro = LoadShippedMacro("disembark");
        var branch = Assert.IsType<BranchStep>(Assert.Single(macro.Steps));

        Assert.Equal(5, branch.Else.Count);
        Assert.Equal(new[] { "Landed", "GuiFocus:NoFocus" }, Assert.IsType<RequireStep>(branch.Else[0]).ConditionTokens);
        Assert.Equal("FocusRadarPanel", Assert.IsType<PressStep>(branch.Else[1]).Action);
        Assert.Equal(1, Assert.IsType<PressStep>(branch.Else[2]).Repeat);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        // Landed is Flags bit 1, so 2 - checked against
        // StatusVocabulary.FlagsConditions, not assumed. Docked (bit 0) is
        // therefore clear, and nothing is focused.
        store.UpdateSnapshot(new StatusSnapshot(2u, 0u, 0, GameRunning: true, SignedIn: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("UI_Down", "Key_S"), ("UI_Select", "Key_Space"), ("FocusRadarPanel", "Key_4"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // FocusRadarPanel
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Down
        await PumpAsync(clock, TimeSpan.FromMilliseconds(150));          // wait
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select

        Assert.Equal(MacroRunOutcome.Success, (await runTask.WaitAsync(RunHangGuard)).Outcome);

        Scancodes.TryGet("Key_4", out var radarKey);
        Scancodes.TryGet("Key_S", out var downKey);
        Scancodes.TryGet("Key_Space", out var selectKey);

        Assert.Equal(6, injector.Events.Count); // (down,up) x 3
        Assert.Equal(
            new[] { radarKey.ScanCode, downKey.ScanCode, selectKey.ScanCode },
            injector.Events.Where(e => e.IsDown).Select(e => e.Key.ScanCode));

        // No warning: Landed and GuiFocus:NoFocus both hold, so the arm's
        // own require is satisfied and nothing is proceeding on a belief.
        Assert.DoesNotContain(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
    }

    /// <summary>
    /// The shipped macro's own text, straight out of the embedded resource -
    /// the only honest source for "what LunaPanel actually ships", as opposed
    /// to a copy retyped into a test.
    /// </summary>
    private static MacroDefinition LoadShippedMacro(string id)
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream($"LunaPanel.Server.definitions.macros.{id}.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return MacroDefinition.Parse(reader.ReadToEnd());
    }

    [Fact]
    public async Task ShippedRequestDockingMacro_LoadsValidatesAndProducesExpectedInjectionSequence_FromNavigation()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.request-docking.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("request-docking", macro.Id);
        Assert.Equal(7, macro.Steps.Count);

        var requireStep = Assert.IsType<RequireStep>(macro.Steps[0]);
        // The LeftPanelTabKnown token that used to sit alongside GuiFocus:NoFocus
        // here was removed 2026-09-08 (governing decision reversing
        // refuse-when-unknown - ref/docs/panel-tab-tracking.md, "Reversed
        // 2026-09-08"). Only the focus guard remains.
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var openStep = Assert.IsType<PressStep>(macro.Steps[1]);
        Assert.Equal(PanelTabTracker.LeftPanelToggleAction, openStep.Action);

        var gotoStep = Assert.IsType<GotoLeftPanelTabStep>(macro.Steps[2]);
        Assert.Equal(PanelTab.Contacts, gotoStep.Target);

        var rightStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("UI_Right", rightStep.Action);

        var selectStep = Assert.IsType<PressStep>(macro.Steps[4]);
        Assert.Equal("UI_Select", selectStep.Action);

        var closeStep = Assert.IsType<PressStep>(macro.Steps[5]);
        Assert.Equal(PanelTabTracker.LeftPanelToggleAction, closeStep.Action);

        var ackStep = Assert.IsType<WaitForEdgeStep>(macro.Steps[6]);
        Assert.Equal(new[] { "Journal:DockingGranted" }, ackStep.SucceedOnTokens);
        Assert.Equal(new[] { "Journal:DockingDenied" }, ackStep.FailOnTokens);
        Assert.Equal("Reason", ackStep.FailureDetailField);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true)); // GuiFocus:NoFocus - the require's own gate
        var journal = new JournalStateStore();
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // panel starts on NAVIGATION after a reset
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, tabTracker, clock, log);
        var bindings = BuildBindings(
            (PanelTabTracker.LeftPanelToggleAction, "Key_1"),
            (PanelTabTracker.AdvanceTabAction, "Key_E"),
            ("UI_Right", "Key_D"),
            ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // open
        // NAVIGATION -> CONTACTS is 2 presses.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // close
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        // (down,up) x (open + 2 tab + right + select + close) = 6 chords.
        Assert.Equal(12, injector.Events.Count);
        Assert.Equal(PanelTab.Contacts, tabTracker.CurrentTab);
    }

    [Fact]
    public async Task ShippedRequestDockingMacro_SecondRun_AlreadyOnContacts_SendsNoTabPresses()
    {
        // The payoff panel-tab-tracking.md names explicitly: a macro that
        // leaves the panel on CONTACTS needs no E presses on its next run.
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.request-docking.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump();
        tabTracker.RecordOwnTabAdvance(); // Navigation -> Transactions
        tabTracker.RecordOwnTabAdvance(); // Transactions -> Contacts (as the prior run left it)
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, tabTracker, clock, log);
        var bindings = BuildBindings(
            (PanelTabTracker.LeftPanelToggleAction, "Key_1"),
            ("UI_Right", "Key_D"),
            ("UI_Select", "Key_Space"));
            // Deliberately no CycleNextPanel binding - if the gotoLeftPanelTab
            // step tried to press it, this run would abort as unbound.

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // open
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // close
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(8, injector.Events.Count); // (down,up) x (open + right + select + close) = 4 chords, ZERO tab presses.
    }

    [Fact]
    public async Task ShippedRequestDockingMacro_TwoConsecutiveRuns_BothGuiFocusEdgesArriveLate_SecondRunSendsNoTabPresses()
    {
        // The live 2026-09-07 defect, driven exactly as the commander hit
        // it - not seeded by hand like the test above. request-docking
        // presses FocusLeftPanel TWICE per run (open at step 1, close at
        // step 5), and both GuiFocus edges that confirm them arrive
        // asynchronously from Status.json polling, well after BOTH presses
        // have already fired - not interleaved with them. A one-shot bool
        // arm loses the first credit the instant the second
        // RecordOwnLeftPanelToggle() call happens (setting an already-true
        // flag is a no-op), so only one of the two late edges is ever
        // treated as "ours" and the tracked tab goes unknown immediately
        // after a run that had just succeeded - "left panel tab is unknown"
        // on the very next attempt, exactly the log the commander saw. This
        // is the deliverable pin: run the macro twice, deliver both edges
        // late after run 1 the way they actually arrived, and assert run 2
        // sends zero CycleNextPanel presses.
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.request-docking.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true)); // GuiFocus:NoFocus
        var journal = new JournalStateStore();
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump(); // panel starts on NAVIGATION
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, tabTracker, clock, log);

        // Run 1's bindings include CycleNextPanel - NAVIGATION -> CONTACTS
        // needs 2 presses of it.
        var bindingsRun1 = BuildBindings(
            (PanelTabTracker.LeftPanelToggleAction, "Key_1"),
            (PanelTabTracker.AdvanceTabAction, "Key_E"),
            ("UI_Right", "Key_D"),
            ("UI_Select", "Key_Space"));

        var run1 = runner.RunAsync(macro, bindingsRun1);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // open - toggle credit #1 banked
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // E
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // E
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // close - toggle credit #2 banked

        // Neither GuiFocus edge has been delivered to the tracker yet - both
        // toggle credits are still outstanding here, exactly as measured
        // live (both presses fire; both edges land afterward).
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));
        var result1 = await run1.WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Success, result1.Outcome);

        // BOTH GuiFocus edges now land late, back-to-back, exactly as
        // ServerHostBuilder's own subscription would report them once
        // Status.json's poller finally catches up.
        tabTracker.RecordLeftPanelEdge(); // the open edge
        tabTracker.RecordLeftPanelEdge(); // the close edge
        Assert.Equal(PanelTab.Contacts, tabTracker.CurrentTab); // still known - this is the fix

        // Run 2's bindings deliberately omit CycleNextPanel - if the goto
        // step tried to press it, this run would abort as unbound rather
        // than succeed, so a green Success below is proof zero tab presses
        // were attempted.
        var bindingsRun2 = BuildBindings(
            (PanelTabTracker.LeftPanelToggleAction, "Key_1"),
            ("UI_Right", "Key_D"),
            ("UI_Select", "Key_Space"));

        injector.Events.Clear(); // isolate run 2's own injections from run 1's
        var run2 = runner.RunAsync(macro, bindingsRun2);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // open
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // close
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));
        var result2 = await run2.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result2.Outcome);
        // (down,up) x (open + right + select + close) = 4 chords, ZERO tab presses.
        Assert.Equal(8, injector.Events.Count);
    }

    [Fact]
    public async Task ShippedRequestDockingMacro_UncreditedEdgeBeforehand_StillRuns_LoggingALowConfidenceWarning()
    {
        // Reversed 2026-09-08 (ref/docs/panel-tab-tracking.md, "Reversed
        // 2026-09-08"). This test used to be named
        // ...UnknownTab_AbortsAtTheRequireStep_BeforeOpeningThePanelAtAll and
        // proved the macro refused with zero injections once the tracker
        // lost track. The user's governing decision removed that refusal
        // entirely: "We should never NOT be allowed to use a macro." This is
        // the deliverable pin for that change - the macro now RUNS to
        // completion from a Low-confidence belief, exactly as it would from
        // a High-confidence one, while logging a WARN that says so.
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.request-docking.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        // An uncredited edge - the commander may have touched the panel by
        // hand - drops confidence to Low but leaves the believed tab at the
        // Fix 7 presumption, Navigation.
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordLeftPanelEdge();
        Assert.Equal(PanelTab.Navigation, tabTracker.CurrentTab);
        Assert.Equal(TabConfidence.Low, tabTracker.LeftTabConfidence);
        var runner = new MacroRunner(injector, store, journal, tabTracker, clock, log);
        var bindings = BuildBindings(
            (PanelTabTracker.LeftPanelToggleAction, "Key_1"),
            (PanelTabTracker.AdvanceTabAction, "Key_E"),
            ("UI_Right", "Key_D"),
            ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // open
        // NAVIGATION -> CONTACTS is still 2 presses - the belief never changed.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // close
        journal.Record(new JournalEvent("DockingGranted", null, new Dictionary<string, string>()));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        // (down,up) x (open + 2 tab + right + select + close) = 6 chords - the
        // same sequence a High-confidence run sends. Nothing was blocked.
        Assert.Equal(12, injector.Events.Count);

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro" && e.Message.Contains("LOW-confidence"));
        Assert.Contains("Navigation", warning.Message);
        Assert.Contains("Contacts", warning.Message);
    }

    [Fact]
    public async Task ShippedRequestDockingMacro_DeniedForDistance_AbortsWithReason_NoDockingRequestedNeeded()
    {
        // LC19's own measurement: a denied request emits DockingDenied ALONE,
        // with no preceding DockingRequested - so this macro's acknowledgement
        // must never wait on DockingRequested.
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.request-docking.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var tabTracker = new PanelTabTracker();
        tabTracker.RecordFsdJump();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, tabTracker, clock, log);
        var bindings = BuildBindings(
            (PanelTabTracker.LeftPanelToggleAction, "Key_1"),
            (PanelTabTracker.AdvanceTabAction, "Key_E"),
            ("UI_Right", "Key_D"),
            ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // open
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // E
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // E
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // D
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Space
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // close
        journal.Record(new JournalEvent("DockingDenied", null, new Dictionary<string, string> { ["Reason"] = "Distance" }));

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(6, result.FailedStepIndex);
        Assert.Contains("Distance", result.FailureReason);
    }

    [Fact]
    public async Task ShippedNomadDockLaunchMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.nomad-dock-launch.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("nomad-dock-launch", macro.Id);
        // Six, not five. [2026-09-12, superseded: was 5 - the macro never
        // closed the role panel it opened, live-confirmed missing (test 59:
        // "docking the Nomad is missing a final 3 keypress to exit the role
        // panel"). A trailing FocusRadarPanel close press was added, matching
        // request-docking's own close-what-you-opened shape.]
        //
        // The trailing waitForEdge on LaunchVessel/DockSRV was
        // removed on 2026-09-07 before this ever shipped: POST /api/press awaits
        // the whole macro run synchronously, and LC17 measured DockSRV arriving
        // 54 and 61 seconds after the presses, with an unbounded spread. That
        // would have hung the HTTP request for a minute with no client feedback.
        //
        // Nothing is lost by dropping it. LC17's own finding is that DockSRV is a
        // COMPLETION, not an acknowledgement - there is no accept/reject to wait
        // on, so waiting buys no decision. And the live channel already tells the
        // device when InSrv flips, which is the feedback a commander actually
        // sees. request-docking keeps its wait because grants arrive in under a
        // second (LC18).
        Assert.Equal(6, macro.Steps.Count);

        var requireStep = Assert.IsType<RequireStep>(macro.Steps[0]);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var rolePanelStep = Assert.IsType<PressStep>(macro.Steps[1]);
        Assert.Equal("FocusRadarPanel", rolePanelStep.Action);

        var downStep = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal("UI_Down", downStep.Action);

        var rightStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("UI_Right", rightStep.Action);

        var selectStep = Assert.IsType<PressStep>(macro.Steps[4]);
        Assert.Equal("UI_Select", selectStep.Action);

        var closeStep = Assert.IsType<PressStep>(macro.Steps[5]);
        Assert.Equal("FocusRadarPanel", closeStep.Action);


        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel", "Key_3"), ("UI_Down", "Key_S"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Down
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3 (close)
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(10, injector.Events.Count); // (down,up) x 5 presses
    }

    [Fact]
    public async Task ShippedNomadDockLaunchMacro_APanelAlreadyOpen_FiresAnywayWithWarning()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.nomad-dock-launch.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        // GuiFocus 2 (ExternalPanel) - a panel is already open.
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 2, GameRunning: true, SignedIn: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel", "Key_3"), ("UI_Down", "Key_S"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        // nomad-dock-launch.json: require, FocusRadarPanel, UI_Down, UI_Right,
        // UI_Select, FocusRadarPanel (close).
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Down
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3 (close)

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(10, injector.Events.Count); // (down,up) x 5 presses

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("GuiFocus", warning.Message);
    }

    /// <summary>
    /// TASK 4b (expressive-kindling-starfish.md): a deliberately separate file
    /// from nomad-dock-launch rather than a reuse of it - the button label is
    /// the feature (a commander in an SRV needs a "Board Ship" button), and
    /// the keystrokes coinciding today is a fact about Elite's current
    /// role-panel layout, not shared intent.
    /// </summary>
    [Fact]
    public async Task ShippedSrvBoardShipMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.srv-board-ship.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("srv-board-ship", macro.Id);
        Assert.Equal(6, macro.Steps.Count);

        var requireStep = Assert.IsType<RequireStep>(macro.Steps[0]);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var rolePanelStep = Assert.IsType<PressStep>(macro.Steps[1]);
        Assert.Equal("FocusRadarPanel", rolePanelStep.Action);

        var downStep = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal("UI_Down", downStep.Action);
        Assert.Equal(1, downStep.Repeat);

        var rightStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("UI_Right", rightStep.Action);

        var selectStep = Assert.IsType<PressStep>(macro.Steps[4]);
        Assert.Equal("UI_Select", selectStep.Action);

        var closeStep = Assert.IsType<PressStep>(macro.Steps[5]);
        Assert.Equal("FocusRadarPanel", closeStep.Action);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel", "Key_3"), ("UI_Down", "Key_S"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Down
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3 (close)
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(10, injector.Events.Count); // (down,up) x 5 presses
    }

    /// <summary>
    /// TASK 4b (expressive-kindling-starfish.md): one extra UI_Down versus
    /// srv-board-ship, per the user's reported key sequences (3,S,D,space vs
    /// 3,S,S,D,space). NOT verified against Elite's actual role-panel row
    /// order from source - a live pass in an SRV is still required, and if it
    /// finds a different row, the fix is the repeat count in this one file.
    ///
    /// [Part E, expressive-kindling-starfish.md, 2026-09-16: the closing
    /// confirm is now a gated <c>pressUntil</c>/<c>condJournal:LaunchSRV</c>
    /// step, not a bare unconditional <c>press</c>. Diagnosed live: the
    /// macro's steps all reported "Succeeded" within a second while the real
    /// `LaunchSRV` journal event never arrived at all - the SRV bay menu was
    /// left fully selected on "Launch" but never actually confirmed, and a
    /// single unrelated physical keypress fired the real launch immediately
    /// afterward. `LaunchSRV` fires in the same instant as the confirm when
    /// it lands (0s gap, per `LaunchVessel`'s identical mechanic measured in
    /// LC17) - unlike `DockSRV`, this is a safe gate to retry against.]
    /// </summary>
    [Fact]
    public async Task ShippedSrvLaunchMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.srv-launch.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("srv-launch", macro.Id);
        Assert.Equal(6, macro.Steps.Count);

        var requireStep = Assert.IsType<RequireStep>(macro.Steps[0]);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var rolePanelStep = Assert.IsType<PressStep>(macro.Steps[1]);
        Assert.Equal("FocusRadarPanel", rolePanelStep.Action);

        var downStep = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal("UI_Down", downStep.Action);
        Assert.Equal(2, downStep.Repeat);

        var rightStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("UI_Right", rightStep.Action);

        var selectStep = Assert.IsType<PressUntilStep>(macro.Steps[4]);
        Assert.Equal("UI_Select", selectStep.Action);
        Assert.Null(selectStep.Conditions);
        Assert.NotNull(selectStep.JournalCondition);
        Assert.Equal("LaunchSRV", selectStep.JournalCondition!.EventName);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), selectStep.Timeout);
        Assert.Equal(3, selectStep.MaxAttempts);

        var closeStep = Assert.IsType<PressStep>(macro.Steps[5]);
        Assert.Equal("FocusRadarPanel", closeStep.Action);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel", "Key_3"), ("UI_Down", "Key_S"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        // UI_Down, repeat 2 - hold, gap, hold (no trailing gap after the last press).
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select (pressUntil, 1st attempt)
        // O44: was a bare `await Task.Delay(50)` - a real lost-advance risk
        // of the O6 kind, because progress here is driven by journal.Record,
        // not the clock, so the arm signal is already completed and the next
        // PumpAsync would not wait at all if the runner's resumption (a
        // thread-pool hop, this call depth being past the CLR's synchronous-
        // continuation guard) took longer than 50ms. Capturing the arm
        // generation first and waiting on NextArmAsync is a real signal
        // instead of a guessed duration - the same primitive O42 built.
        var generationBeforeJournalRecord = clock.ArmGeneration;
        journal.Record(new JournalEvent("LaunchSRV", null, new Dictionary<string, string>()));
        await clock.NextArmAsync(generationBeforeJournalRecord).WaitAsync(PumpArmGuard);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3 (close)
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(12, injector.Events.Count); // (down,up) x 6 presses, confirm arrived on the 1st attempt
    }

    /// <summary>
    /// Part E, expressive-kindling-starfish.md: pins the exact fix for the
    /// silent-arm bug - if `LaunchSRV` never arrives at all (the diagnosed
    /// live failure), the macro must abort naming the confirm step rather
    /// than reporting a silent, false "Succeeded" the way the old
    /// unconditional `press` step did.
    /// </summary>
    [Fact]
    public async Task ShippedSrvLaunchMacro_ConfirmNeverArrives_AbortsRatherThanSilentlySucceeding()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.srv-launch.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());
        var selectStep = Assert.IsType<PressUntilStep>(macro.Steps[4]);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel", "Key_3"), ("UI_Down", "Key_S"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, MacroTimingDefaults.InterPressGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Down x2
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right

        // LaunchSRV never arrives - every attempt times out.
        for (var i = 0; i < selectStep.MaxAttempts; i++)
        {
            await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
            await PumpAsync(clock, selectStep.Timeout);
        }

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(4, result.FailedStepIndex);
        Assert.Contains("LaunchSRV", result.FailureReason);
        // The close step must never run - the panel is left open, matching
        // the real diagnosed failure (menu sat armed, never confirmed).
        Assert.Equal(4 * 2 + selectStep.MaxAttempts * 2, injector.Events.Count);
    }

    /// <summary>
    /// Promoted 2026-09-17 from the commander's own personal user macro
    /// (live-tested on their paired tablet, id <c>user-47e55a8e748b</c>) to a
    /// shipped default: toggle landing gear, wait for the <c>Docked</c>
    /// journal edge (bounded, like <c>request-docking</c>'s own
    /// <c>waitForEdge</c> - O28), settle three seconds, then walk the
    /// station-services list to request refuelling, using a
    /// <c>pressUntil</c>/<c>condJournal</c> gate on <c>RefuelAll</c> the same
    /// way <c>srv-launch</c> gates on <c>LaunchSRV</c>.
    /// </summary>
    [Fact]
    public async Task ShippedPrepareToDockMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var macro = LoadShippedMacro("prepare-to-dock");
        Assert.Equal("prepare-to-dock", macro.Id);
        Assert.Equal(10, macro.Steps.Count);

        var gearStep = Assert.IsType<PressStep>(macro.Steps[0]);
        Assert.Equal("LandingGearToggle", gearStep.Action);
        Assert.Equal(1, gearStep.Repeat);

        var dockedStep = Assert.IsType<WaitForEdgeStep>(macro.Steps[1]);
        Assert.Equal(new[] { "Journal:Docked" }, dockedStep.SucceedOnTokens);
        Assert.Empty(dockedStep.FailOnTokens);
        Assert.Equal(TimeSpan.FromMilliseconds(300000), dockedStep.Timeout);

        var settleStep = Assert.IsType<WaitStep>(macro.Steps[2]);
        Assert.Equal(TimeSpan.FromMilliseconds(3000), settleStep.Duration);

        var upStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("UI_Up", upStep.Action);
        Assert.Equal(1, upStep.Repeat);

        var refuelStep = Assert.IsType<PressUntilStep>(macro.Steps[4]);
        Assert.Equal("UI_Select", refuelStep.Action);
        Assert.Null(refuelStep.Conditions);
        Assert.NotNull(refuelStep.JournalCondition);
        Assert.Equal("RefuelAll", refuelStep.JournalCondition!.EventName);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), refuelStep.Timeout);
        Assert.Equal(10, refuelStep.MaxAttempts);

        var rightStep1 = Assert.IsType<PressStep>(macro.Steps[5]);
        Assert.Equal("UI_Right", rightStep1.Action);
        Assert.Equal(1, rightStep1.Repeat);

        var selectStep1 = Assert.IsType<PressStep>(macro.Steps[6]);
        Assert.Equal("UI_Select", selectStep1.Action);
        Assert.Equal(1, selectStep1.Repeat);

        var rightStep2 = Assert.IsType<PressStep>(macro.Steps[7]);
        Assert.Equal("UI_Right", rightStep2.Action);
        Assert.Equal(1, rightStep2.Repeat);

        var selectStep2 = Assert.IsType<PressStep>(macro.Steps[8]);
        Assert.Equal("UI_Select", selectStep2.Action);
        Assert.Equal(1, selectStep2.Repeat);

        var downStep = Assert.IsType<PressStep>(macro.Steps[9]);
        Assert.Equal("UI_Down", downStep.Action);
        Assert.Equal(1, downStep.Repeat);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("LandingGearToggle", "Key_L"), ("UI_Up", "Key_W"), ("UI_Down", "Key_S"),
            ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // gear toggle
        var dockedGeneration = clock.ArmGeneration;
        journal.Record(new JournalEvent("Docked", null, new Dictionary<string, string>()));
        await clock.NextArmAsync(dockedGeneration).WaitAsync(PumpArmGuard);
        await PumpAsync(clock, settleStep.Duration); // 3s settle
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Up
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select (pressUntil, 1st attempt)
        var refuelGeneration = clock.ArmGeneration;
        journal.Record(new JournalEvent("RefuelAll", null, new Dictionary<string, string>()));
        await clock.NextArmAsync(refuelGeneration).WaitAsync(PumpArmGuard);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Select
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // UI_Down

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        // (down,up) x (gear + up + select(refuel) + right + select + right + select + down) = 8 chords.
        Assert.Equal(16, injector.Events.Count);
    }

    /// <summary>
    /// Replaces the ship's own "disembark" macro on the NOMAD page's first
    /// slot (2026-09-09) - that macro requires <c>Docked</c>, which is never
    /// true in an SRV, so it refused live with "require
    /// [Docked,GuiFocus:NoFocus] was not satisfied" (screenshotted by the
    /// commander). This macro presses the SRV's OWN role-panel bind
    /// (<c>FocusRadarPanel_Buggy</c>, curated 2026-09-09 alongside this
    /// macro - <c>ref/docs/catalogue.md</c>) rather than the ship's
    /// <c>FocusRadarPanel</c>, the same distinction that made
    /// <c>nomad-dock-launch</c> necessary for the sibling slot.
    /// </summary>
    [Fact]
    public async Task ShippedNomadDisembarkMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.nomad-disembark.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("nomad-disembark", macro.Id);
        Assert.Equal(4, macro.Steps.Count);

        var requireStep = Assert.IsType<RequireStep>(macro.Steps[0]);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var rolePanelStep = Assert.IsType<PressStep>(macro.Steps[1]);
        Assert.Equal("FocusRadarPanel_Buggy", rolePanelStep.Action);

        var rightStep = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal("UI_Right", rightStep.Action);

        var selectStep = Assert.IsType<PressStep>(macro.Steps[3]);
        Assert.Equal("UI_Select", selectStep.Action);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel_Buggy", "Key_3"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(6, injector.Events.Count); // (down,up) x 3 presses
    }

    [Fact]
    public async Task ShippedNomadDisembarkMacro_APanelAlreadyOpen_FiresAnywayWithWarning()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.nomad-disembark.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        // GuiFocus 2 (ExternalPanel) - a panel is already open.
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 2, GameRunning: true, SignedIn: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("FocusRadarPanel_Buggy", "Key_3"), ("UI_Right", "Key_D"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        // nomad-disembark.json: require, FocusRadarPanel_Buggy, UI_Right, UI_Select.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // 3
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Right
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(6, injector.Events.Count); // (down,up) x 3 presses

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("GuiFocus", warning.Message);
    }

    /// <summary>
    /// New button on the NOMAD page, 2026-09-09 - keys <c>E, Space</c>
    /// resolved against the commander's real <c>Custom.4.2.binds</c> to
    /// <c>HumanoidPrimaryInteractButton</c>/<c>UI_Select</c>.
    /// </summary>
    [Fact]
    public async Task ShippedEnterCockpitMacro_LoadsValidatesAndProducesExpectedInjectionSequence()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.enter-cockpit.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var json = reader.ReadToEnd();

        var macro = MacroDefinition.Parse(json);
        Assert.Equal("enter-cockpit", macro.Id);
        Assert.Equal(3, macro.Steps.Count);

        var requireStep = Assert.IsType<RequireStep>(macro.Steps[0]);
        Assert.Equal(new[] { "GuiFocus:NoFocus" }, requireStep.ConditionTokens);

        var interactStep = Assert.IsType<PressStep>(macro.Steps[1]);
        Assert.Equal("HumanoidPrimaryInteractButton", interactStep.Action);

        var selectStep = Assert.IsType<PressStep>(macro.Steps[2]);
        Assert.Equal("UI_Select", selectStep.Action);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 0, GameRunning: true, SignedIn: true));
        var journal = new JournalStateStore();
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, journal, new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("HumanoidPrimaryInteractButton", "Key_E"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // E
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(4, injector.Events.Count); // (down,up) x 2 presses
    }

    [Fact]
    public async Task ShippedEnterCockpitMacro_APanelAlreadyOpen_FiresAnywayWithWarning()
    {
        var assembly = System.Reflection.Assembly.Load("LunaPanel.Server");
        using var stream = assembly.GetManifestResourceStream("LunaPanel.Server.definitions.macros.enter-cockpit.json");
        using var reader = new StreamReader(stream!);
        var macro = MacroDefinition.Parse(reader.ReadToEnd());

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(new StatusSnapshot(0u, 0u, 2, GameRunning: true, SignedIn: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(
            ("HumanoidPrimaryInteractButton", "Key_E"), ("UI_Select", "Key_Space"));

        var runTask = runner.RunAsync(macro, bindings);

        // enter-cockpit.json: require, HumanoidPrimaryInteractButton, UI_Select.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // E
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration); // Select

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(4, injector.Events.Count); // (down,up) x 2 presses

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Category == "Macro");
        Assert.Contains("GuiFocus", warning.Message);
    }

    // ---------------------------------------------------------------
    // RunningMacroIds / Changed - what makes a running macro's button glow
    // for as long as the run lasts (2026-09-10, ref/docs/lit-state.md). These pin
    // the RUNNER's half; the wiring that turns it into a lit level on the
    // wire is driven end to end in ServerHostBuilderTests.
    // ---------------------------------------------------------------

    /// <summary>
    /// Records what <see cref="MacroRunner.RunningMacroIds"/> said at each
    /// <see cref="MacroRunner.Changed"/> raise, which is the only way to see
    /// the mid-run state of something that is over in milliseconds - reading
    /// the property after awaiting the run would always find it empty and
    /// would pass just as happily if it were never populated at all.
    /// </summary>
    private static List<IReadOnlySet<string>> RecordRunningSets(MacroRunner runner)
    {
        var seen = new List<IReadOnlySet<string>>();
        runner.Changed += () => seen.Add(runner.RunningMacroIds);
        return seen;
    }

    [Fact]
    public async Task RunningMacroIds_NameTheRunWhileItIsInFlight_AndAreEmptyOnceItSucceeds()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("TestAction", "Key_W"));
        var macro = MacroWithId("glow-macro", new PressStep("TestAction", Repeat: 1, Hold: null));
        var seen = RecordRunningSets(runner);

        var runTask = runner.RunAsync(macro, bindings);
        Assert.Equal(new[] { "glow-macro" }, runner.RunningMacroIds);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runTask.WaitAsync(RunHangGuard)).Outcome);

        Assert.Empty(runner.RunningMacroIds);
        // Exactly two edges - lights, then goes dark. A third would be a
        // button flickering for no reason a commander could explain.
        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { "glow-macro" }, seen[0]);
        Assert.Empty(seen[1]);
    }

    [Fact]
    public async Task RunningMacroIds_AreClearedWhenARunAborts()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var seen = RecordRunningSets(runner);
        // RequireStep no longer aborts a run (2026-09-12, never-gate-a-macro.md)
        // - an unbound action is used here instead purely to exercise a run
        // that genuinely aborts, which is what this pin is actually about.
        var macro = MacroWithId("aborting-macro",
            new PressStep("UnboundAction", Repeat: 1, Hold: null));

        var result = await runner.RunAsync(macro, BuildBindings()).WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Empty(runner.RunningMacroIds);
        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { "aborting-macro" }, seen[0]);
        Assert.Empty(seen[1]);
    }

    /// <summary>
    /// The commander's stop (<see cref="MacroRunOutcome.Cancelled"/>) has to
    /// put the button out like any other ending - a run stopped half way
    /// leaving a permanently glowing button would be the worst of the three,
    /// because nothing later would ever come along to correct it.
    /// </summary>
    [Fact]
    public async Task RunningMacroIds_AreClearedWhenTheCommanderStopsTheRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var seen = RecordRunningSets(runner);
        var macro = MacroWithId("stoppable-macro", new WaitStep(TimeSpan.FromSeconds(5)));

        var runTask = runner.RunAsync(macro, BuildBindings());
        Assert.Equal(new[] { "stoppable-macro" }, runner.RunningMacroIds);

        // The second press stops it; no clock advance at all, so a run that
        // ignored the stop would still be sitting in its 5-second wait.
        var secondPress = await runner.RunAsync(macro, BuildBindings()).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Cancelled, secondPress.Outcome);
        Assert.Equal(MacroRunOutcome.Cancelled, (await runTask.WaitAsync(RunHangGuard)).Outcome);

        Assert.Empty(runner.RunningMacroIds);
        Assert.Equal(2, seen.Count);
        Assert.Empty(seen[1]);
    }

    /// <summary>
    /// The discriminating case for where the start edge is raised: a press
    /// refused because a DIFFERENT macro holds the keyboard never ran, so it
    /// must never appear in the running set - not even for the instant
    /// between the id being claimed and the refusal handing it back. A
    /// button that flashed for a press that did nothing would be a lie told
    /// in the one place this project uses to say "something is happening".
    /// </summary>
    [Fact]
    public async Task RunningMacroIds_NeverNameAPressRefusedAsBusy()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a", new PressStep("ActionA", Repeat: 1, Hold: null));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));
        var seen = RecordRunningSets(runner);

        var runA = runner.RunAsync(macroA, bindings);
        Assert.False(runA.IsCompleted);

        var resultB = await runner.RunAsync(macroB, bindings).WaitAsync(RunHangGuard);
        Assert.Equal(MacroRunOutcome.Busy, resultB.Outcome);

        // Checked WHILE A is still running, so this cannot pass merely
        // because everything had already finished and emptied.
        Assert.Equal(new[] { "macro-a" }, runner.RunningMacroIds);
        Assert.DoesNotContain(seen, s => s.Contains("macro-b"));

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await runA.WaitAsync(RunHangGuard);

        // Still nothing about macro-b, and exactly A's own two edges.
        Assert.DoesNotContain(seen, s => s.Contains("macro-b"));
        Assert.Equal(2, seen.Count);
    }

    // ---------------------------------------------------------------
    // branch (2026-09-17) - the only step kind that redirects execution,
    // and therefore the only one for which "the step list is fixed and
    // fully known before the run starts" - the assumption the injection
    // lock's own bookkeeping was built on - is no longer true.
    // ---------------------------------------------------------------

    private static ConditionList DockedCondition() => ConditionList.Parse(new[] { "Docked" });

    private static BranchStep BranchOnDocked(MacroStep[] then, MacroStep[] otherwise) =>
        new(DockedCondition(), new[] { "Docked" }, then, otherwise);

    [Fact]
    public async Task Branch_ConditionTrue_RunsTheThenArm_AndNothingFromTheElseArm()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ThenAction", "Key_W"), ("ElseAction", "Key_E"));
        var macro = MacroWithSteps(BranchOnDocked(
            then: new MacroStep[] { new PressStep("ThenAction", Repeat: 1, Hold: null) },
            otherwise: new MacroStep[] { new PressStep("ElseAction", Repeat: 1, Hold: null) }));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Scancodes.TryGet("Key_W", out var thenKey);
        Scancodes.TryGet("Key_E", out var elseKey);
        Assert.Equal(1, injector.DownCount);
        Assert.Contains(injector.Events, e => e.Key.ScanCode == thenKey.ScanCode);
        Assert.DoesNotContain(injector.Events, e => e.Key.ScanCode == elseKey.ScanCode);
    }

    [Fact]
    public async Task Branch_ConditionFalse_RunsTheElseArm_AndNothingFromTheThenArm()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ThenAction", "Key_W"), ("ElseAction", "Key_E"));
        var macro = MacroWithSteps(BranchOnDocked(
            then: new MacroStep[] { new PressStep("ThenAction", Repeat: 1, Hold: null) },
            otherwise: new MacroStep[] { new PressStep("ElseAction", Repeat: 1, Hold: null) }));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Scancodes.TryGet("Key_W", out var thenKey);
        Scancodes.TryGet("Key_E", out var elseKey);
        Assert.Equal(1, injector.DownCount);
        Assert.Contains(injector.Events, e => e.Key.ScanCode == elseKey.ScanCode);
        Assert.DoesNotContain(injector.Events, e => e.Key.ScanCode == thenKey.ScanCode);
    }

    /// <summary>
    /// Unknown counts as the negative case - the same rule
    /// <c>Condition</c>/<c>ConditionList</c> already apply internally and
    /// <c>ExecuteRequire</c> already reports, not a new one invented for
    /// this step. The <c>WARN</c> matters as much as the arm: a commander
    /// whose macro took the surface-landing path because LunaPanel had not
    /// yet read a status file needs that to be findable in a log rather than
    /// mysterious.
    /// </summary>
    [Fact]
    public async Task Branch_NoSnapshotYet_TakesTheElseArm_AndWarnsSayingWhy()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock); // never given a snapshot at all
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ThenAction", "Key_W"), ("ElseAction", "Key_E"));
        var macro = MacroWithSteps(BranchOnDocked(
            then: new MacroStep[] { new PressStep("ThenAction", Repeat: 1, Hold: null) },
            otherwise: new MacroStep[] { new PressStep("ElseAction", Repeat: 1, Hold: null) }));

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await runTask.WaitAsync(RunHangGuard);

        Scancodes.TryGet("Key_E", out var elseKey);
        Assert.Contains(injector.Events, e => e.Key.ScanCode == elseKey.ScanCode);

        var warning = Assert.Single(log.Snapshot(), e => e.Level == DiagnosticLevel.Warn && e.Message.Contains("branch"));
        Assert.Contains("Docked", warning.Message);
        Assert.Contains("no snapshot yet", warning.Message);
    }

    /// <summary>
    /// <b>The pin that rules out the rejected design.</b> A conservative
    /// global upper bound - the greatest injecting position across BOTH arms,
    /// computed once before the run - would hold the keyboard for three
    /// presses' worth of time here even though the arm actually taken emits
    /// one. That is exactly the over-hold that blocked every other macro for
    /// 198 seconds live on 2026-09-09, reintroduced for no benefit.
    ///
    /// Proven by admission, not by inspection: a DIFFERENT macro is accepted
    /// the moment the taken arm's single press completes.
    /// </summary>
    [Fact]
    public async Task Branch_ShortArmTaken_ReleasesTheKeyboard_WithoutWaitingForTheLongerUntakenArm()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a",
            BranchOnDocked(
                then: new MacroStep[] { new PressStep("ActionA", Repeat: 1, Hold: null) },
                otherwise: new MacroStep[] { new PressStep("ActionA", Repeat: 3, Hold: null) }),
            new WaitStep(TimeSpan.FromSeconds(5)));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);

        // The then-arm's one press completes. Nothing after it, in this arm
        // or at the outer level, can ever inject again. Reliably observed
        // (O42) - B below must see the lock actually released, not read it
        // during a bounded settle that undershot.
        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.False(runA.IsCompleted); // still in its 5s wait

        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var resultB = await runB.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, resultB.Outcome);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MacroRunOutcome.Success, (await runA.WaitAsync(RunHangGuard)).Outcome);
    }

    /// <summary>
    /// The other direction, and the one a release-too-early mistake fails:
    /// when the LONG arm is the one taken, the keyboard stays claimed
    /// through every one of its press STEPS. Asserted at the point where the
    /// short arm's length has been reached and the long arm has not - a
    /// runner that released at the taken arm's first press step, or at the
    /// shorter arm's length, would let macro B interleave its keystrokes
    /// into the middle of macro A's sequence.
    ///
    /// The long arm is three SEPARATE press steps rather than one step with
    /// <c>Repeat: 3</c>, and that is load-bearing rather than incidental: the
    /// injection lock is decided per STEP, so a three-repeat single step
    /// releases at exactly the same moment whether or not the "is anything
    /// left" check is there at all. Written the first way, this test went
    /// green under a mutation that dropped that check entirely - an
    /// undershoot found on 2026-09-17 by predicting the red count before
    /// running the mutation, and fixed here rather than explained away.
    /// </summary>
    [Fact]
    public async Task Branch_LongArmTaken_KeepsTheKeyboardClaimed_UntilThatArmsLastPress()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: false));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a",
            BranchOnDocked(
                then: new MacroStep[] { new PressStep("ActionA", Repeat: 1, Hold: null) },
                otherwise: new MacroStep[]
                {
                    new PressStep("ActionA", Repeat: 1, Hold: null),
                    new PressStep("ActionA", Repeat: 1, Hold: null),
                    new PressStep("ActionA", Repeat: 1, Hold: null),
                }),
            new WaitStep(TimeSpan.FromSeconds(5)));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);

        // Press step 1 of 3 done - exactly where the short arm would have
        // ended, and where a per-step release with no look-ahead would fire.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Busy, (await runner.RunAsync(macroB, bindings).WaitAsync(RunHangGuard)).Outcome);

        // Press steps 2 and 3. No inter-press gap between them: separate
        // steps, not repeats of one.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Busy, (await runner.RunAsync(macroB, bindings).WaitAsync(RunHangGuard)).Outcome);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runB.WaitAsync(RunHangGuard)).Outcome);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MacroRunOutcome.Success, (await runA.WaitAsync(RunHangGuard)).Outcome);
        Assert.Equal(4, injector.DownCount); // A's three, plus B's one
    }

    /// <summary>
    /// <c>tailMightInject</c>: an arm's last press is NOT the run's last
    /// press when the branch is followed by an injecting step at the outer
    /// level. A runner that decided "release" from the arm alone would drop
    /// the keyboard with a press still to come.
    /// </summary>
    [Fact]
    public async Task Branch_FollowedByAnInjectingStep_KeepsTheKeyboardClaimedPastTheArm()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"), ("ActionC", "Key_R"));
        var macroA = MacroWithId("macro-a",
            BranchOnDocked(
                then: new MacroStep[] { new PressStep("ActionA", Repeat: 1, Hold: null) },
                otherwise: new MacroStep[] { new PressStep("ActionA", Repeat: 1, Hold: null) }),
            new PressStep("ActionC", Repeat: 1, Hold: null),
            new WaitStep(TimeSpan.FromSeconds(5)));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);

        // The arm's only press is done - but step 1 at the outer level still
        // has to fire.
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Busy, (await runner.RunAsync(macroB, bindings).WaitAsync(RunHangGuard)).Outcome);

        // The outer press step's release must be reliably observed (O42)
        // before B is started below - a bounded settle that undershot here
        // could see B refused as Busy when it should be accepted.
        await PumpAndWaitForNextArmAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runB.WaitAsync(RunHangGuard)).Outcome);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MacroRunOutcome.Success, (await runA.WaitAsync(RunHangGuard)).Outcome);
    }

    /// <summary>
    /// The conservatism ENDS the moment the branch resolves. Before the
    /// branch is reached, the untaken arm's press forces the keyboard to stay
    /// claimed - it is not yet knowable which arm will run. As soon as the
    /// arm is chosen, and that arm plus everything after it can press
    /// nothing, the keyboard is handed straight back rather than held for the
    /// rest of the run.
    ///
    /// [Added 2026-09-17 after a mutation that removed this early release
    /// produced ZERO reds - the release was a real improvement that nothing
    /// was holding in place. Proven by admission with no clock advance at
    /// all: macro B is accepted while A is still sitting in its 5s wait.]
    /// </summary>
    [Fact]
    public async Task Branch_TakenArmCannotInject_ReleasesTheKeyboardAsSoonAsTheArmIsChosen()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a",
            BranchOnDocked(
                // Taken. Presses nothing.
                then: new MacroStep[] { new WaitStep(TimeSpan.FromSeconds(5)) },
                // Not taken - but it is what keeps the lock claimed up to the
                // moment the branch resolves, which is the whole point.
                otherwise: new MacroStep[] { new PressStep("ActionA", Repeat: 1, Hold: null) }));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);
        Assert.False(runA.IsCompleted); // in the then-arm's 5s wait

        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runB.WaitAsync(RunHangGuard)).Outcome);

        // A pressed nothing at all: only B's single key went anywhere.
        Assert.Equal(1, injector.DownCount);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MacroRunOutcome.Success, (await runA.WaitAsync(RunHangGuard)).Outcome);
    }

    /// <summary>
    /// The upfront "nothing anywhere ever injects, drop the lock before step
    /// 0" fast path, generalized over a tree: neither arm of this branch can
    /// press anything, so macro B is accepted immediately, before A's wait
    /// has even been advanced.
    /// </summary>
    [Fact]
    public async Task Branch_NeitherArmCanInject_ReleasesTheKeyboardBeforeTheRunEvenStarts()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionB", "Key_E"));
        var macroA = MacroWithId("macro-a",
            BranchOnDocked(
                then: new MacroStep[] { new WaitStep(TimeSpan.FromSeconds(5)) },
                otherwise: new MacroStep[] { new WaitStep(TimeSpan.FromSeconds(5)) }));
        var macroB = MacroWithId("macro-b", new PressStep("ActionB", Repeat: 1, Hold: null));

        var runA = runner.RunAsync(macroA, bindings);
        Assert.False(runA.IsCompleted);

        var runB = runner.RunAsync(macroB, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Success, (await runB.WaitAsync(RunHangGuard)).Outcome);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MacroRunOutcome.Success, (await runA.WaitAsync(RunHangGuard)).Outcome);
    }

    /// <summary>
    /// Progress reports count <b>executed</b> steps in run order, at any
    /// depth - the branch itself, then each step of the arm it chose, then
    /// whatever follows at the outer level. Not a position in
    /// <c>macro.Steps</c>, which for a step inside an arm does not exist.
    /// </summary>
    [Fact]
    public async Task Branch_ProgressReports_CountExecutedStepsInRunOrder_ThroughTheTakenArm()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("ActionA", "Key_W"), ("ActionC", "Key_R"));
        var macro = MacroWithSteps(
            BranchOnDocked(
                then: new MacroStep[]
                {
                    new PressStep("ActionA", Repeat: 1, Hold: null),
                    new WaitStep(TimeSpan.FromMilliseconds(10)),
                },
                otherwise: new MacroStep[] { new WaitStep(TimeSpan.FromMilliseconds(10)) }),
            new PressStep("ActionC", Repeat: 1, Hold: null));

        var reports = new List<MacroStepProgress>();
        var runTask = runner.RunAsync(macro, bindings, new SynchronousProgress<MacroStepProgress>(reports.Add));

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await PumpAsync(clock, TimeSpan.FromMilliseconds(10));
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        Assert.Equal(MacroRunOutcome.Success, (await runTask.WaitAsync(RunHangGuard)).Outcome);

        Assert.Equal(new[] { 0, 1, 2, 3 }, reports.Select(r => r.StepIndex));
        Assert.Equal(new[] { "branch", "press", "wait", "press" }, reports.Select(r => r.StepKind));
        Assert.All(reports, r => Assert.Equal(MacroStepOutcome.Succeeded, r.Outcome));
    }

    /// <summary>
    /// The abort index counts executed steps too - here the branch (0) and
    /// then the arm's unbound press (1). A flat-list reading would have said
    /// 0, naming the branch as the thing that failed when the branch
    /// succeeded and the step inside it did not.
    /// </summary>
    [Fact]
    public async Task Branch_AStepInsideAnArmFailing_AbortsNamingItsExecutedPosition()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, new DiagnosticRingBuffer(64));
        var bindings = BuildBindings(("SomethingElse", "Key_W"));
        var macro = MacroWithSteps(
            BranchOnDocked(
                then: new MacroStep[]
                {
                    new WaitStep(TimeSpan.Zero),
                    new PressStep("NotBoundAnywhere", Repeat: 1, Hold: null),
                },
                otherwise: new MacroStep[] { new WaitStep(TimeSpan.Zero) }));

        var result = await runner.RunAsync(macro, bindings).WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Aborted, result.Outcome);
        Assert.Equal(2, result.FailedStepIndex);
    }

    /// <summary>
    /// The stop the commander presses, landing inside an arm: the log names
    /// the executed position (branch, then one press = stopped at step 2),
    /// not the arm-local loop index a flat reading would have produced.
    /// </summary>
    [Fact]
    public async Task Branch_StoppedInsideAnArm_NamesTheExecutedPosition_NotTheArmLocalIndex()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked: true));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var bindings = BuildBindings(("ActionA", "Key_W"));
        var macro = MacroWithId("stop-macro",
            BranchOnDocked(
                then: new MacroStep[]
                {
                    new PressStep("ActionA", Repeat: 1, Hold: null),
                    new PressStep("ActionA", Repeat: 1, Hold: null),
                },
                otherwise: new MacroStep[] { new WaitStep(TimeSpan.Zero) }));

        var runA = runner.RunAsync(macro, bindings);
        Assert.Equal(MacroRunOutcome.Cancelled, (await runner.RunAsync(macro, bindings).WaitAsync(RunHangGuard)).Outcome);

        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        Assert.Equal(MacroRunOutcome.Cancelled, (await runA.WaitAsync(RunHangGuard)).Outcome);

        Assert.Equal(1, injector.DownCount); // the second press never happened
        Assert.Contains(log.Snapshot(), e => e.Message.Contains("stopped at step 2"));
    }

    /// <summary>
    /// The chosen arm is named in the diagnostic log - the only place it is
    /// recorded in this pass (no new <c>MacroStepProgress</c> field), so a
    /// commander diagnosing "it pressed the wrong thing" can see which way
    /// the branch actually went.
    /// </summary>
    [Theory]
    [InlineData(true, "then")]
    [InlineData(false, "else")]
    public async Task Branch_LogsWhichArmItTook(bool docked, string expectedArm)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var injector = new RecordingKeyInjector(clock);
        var store = new GameStateStore(clock);
        store.UpdateSnapshot(Snapshot(docked));
        var log = new DiagnosticRingBuffer(64);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, log);
        var macro = MacroWithSteps(BranchOnDocked(
            then: new MacroStep[] { new WaitStep(TimeSpan.Zero) },
            otherwise: new MacroStep[] { new WaitStep(TimeSpan.Zero) }));

        await runner.RunAsync(macro, BuildBindings(("Unused", "Key_W"))).WaitAsync(RunHangGuard);

        var branchEntry = Assert.Single(log.Snapshot(), e => e.Message.Contains("(branch)"));
        Assert.Contains($"arm={expectedArm}", branchEntry.Detail);
        Assert.DoesNotContain($"arm={(expectedArm == "then" ? "else" : "then")}", branchEntry.Detail);
    }

    /// <summary>
    /// A synchronous <see cref="IProgress{T}"/> - the framework's own
    /// <see cref="Progress{T}"/> posts its callback through a captured
    /// <see cref="SynchronizationContext"/>, which would make report timing
    /// racy against this test's own synchronous clock-advance calls.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public SynchronousProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
