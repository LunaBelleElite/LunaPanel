using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Input;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Drives the real <see cref="Win32KeyInjector"/> through its test-seam
/// constructor, which takes both OS-facing dependencies (foreground
/// capture, the actual <c>SendInput</c> call) as delegates. Every test here
/// substitutes fakes for both, so the guard call, the logging, and the
/// struct-building inside <see cref="Win32KeyInjector"/> all run for real
/// while <see cref="Win32NativeInputSender.Send"/> and
/// <see cref="Win32ForegroundInspector.Capture"/> - the only two members in
/// this whole feature that touch an actual Win32 API - are never invoked by
/// any test in this file, this class, or this project. That is how "no test
/// may actually inject a keystroke" is guaranteed: the real send delegate is
/// structurally unreachable from test code, not merely unexercised by
/// coincidence.
/// </summary>
public class Win32KeyInjectorTests
{
    private const uint ScanCodeFlag = 0x0008;
    private const uint ExtendedFlag = 0x0001;
    private const uint KeyUpFlag = 0x0002;

    private sealed class RecordingSender
    {
        public List<NativeMethods.INPUT> Sent { get; } = new();
        public uint InsertedToReturn { get; set; } = 1;
        public int Win32ErrorToReturn { get; set; }

        public (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
        {
            Sent.AddRange(inputs);
            return (InsertedToReturn, Win32ErrorToReturn);
        }
    }

    private static ForegroundContext EliteOk() => new("EliteDangerous64", IntegrityLevel.Medium, IntegrityLevel.Medium);
    private static ForegroundContext NotElite() => new("Discord", IntegrityLevel.Medium, IntegrityLevel.Medium);

    // ---------------------------------------------------------------
    // Guard integration - KeyDown
    // ---------------------------------------------------------------

    [Fact]
    public void KeyDown_EliteForeground_CallsSendInput_WithCorrectlyBuiltStruct()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x11, false)); // Key_W

        var sent = Assert.Single(sender.Sent);
        Assert.Equal((ushort)0, sent.U.ki.wVk);
        Assert.Equal((ushort)0x11, sent.U.ki.wScan);
        Assert.Equal(ScanCodeFlag, sent.U.ki.dwFlags);
    }

    [Fact]
    public void KeyDown_NotForeground_NeverCallsSendInput_AndLogsWarning()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x11, false));

        Assert.Empty(sender.Sent);
        var evt = Assert.Single(log.Snapshot());
        Assert.Equal(DiagnosticLevel.Warn, evt.Level);
        Assert.Equal("Injection", evt.Category);
        Assert.Contains("GameNotForeground", evt.Message);
    }

    // ---------------------------------------------------------------
    // Guard integration - KeyUp. NOTE (O8, 2026-09-05): this used to be
    // "Guard integration - KeyUp (same guard applies)", and the test below
    // used to be KeyUp_NotForeground_NeverCallsSendInput_AndLogsWarning,
    // asserting the OLD, now-superseded behaviour - that a KeyUp refused by
    // the foreground guard never reaches SendInput, same as a KeyDown. That
    // test was pinning the defect O8 describes (a stuck modifier when focus
    // leaves Elite mid-chord), not a correct guarantee. It has been
    // rewritten below to pin the corrected behaviour instead: a KeyUp always
    // bypasses the guard and is always sent. See ref/docs/injection.md's
    // "Key-up bypasses the foreground guard" section for the ruling.
    // ---------------------------------------------------------------

    [Fact]
    public void KeyUp_NotForeground_StillCallsSendInput_BecauseKeyUpBypassesTheGuard()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);

        injector.KeyUp(new ScancodeInfo(0x11, false));

        var sent = Assert.Single(sender.Sent);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, sent.U.ki.dwFlags);
        // Diagnostic mode is off and the send "succeeded" (RecordingSender's
        // default), so there is nothing to log - no refusal (the guard was
        // never even evaluated) and no opt-in success noise.
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void KeyUp_EliteForeground_CallsSendInput()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        injector.KeyUp(new ScancodeInfo(0x11, false));

        var sent = Assert.Single(sender.Sent);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, sent.U.ki.dwFlags);
    }

    // ---------------------------------------------------------------
    // KeyDownWithOutcome - same as KeyDown, but returns the InjectionGuard
    // verdict instead of discarding it. Used by POST /api/press's
    // ChordPresser (see ref/docs/injection.md) so a refusal reaches the
    // phone with its reason, not just silence.
    // ---------------------------------------------------------------

    [Fact]
    public void KeyDownWithOutcome_EliteForeground_ReturnsSent_AndStillCallsSendInput()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        var result = injector.KeyDownWithOutcome(new ScancodeInfo(0x11, false));

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public void KeyDownWithOutcome_NotForeground_ReturnsGameNotForeground_AndNeverCallsSendInput()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);

        var result = injector.KeyDownWithOutcome(new ScancodeInfo(0x11, false));

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);
        Assert.Contains("active window", result.Reason);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void KeyDownWithOutcome_UipiSuspected_ReturnsUipiSuspected_WithOperatorReadableReason()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        ForegroundContext Context() => new("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);

        var result = injector.KeyDownWithOutcome(new ScancodeInfo(0x11, false));

        Assert.Equal(InjectionOutcome.UipiSuspected, result.Outcome);
        Assert.Contains("UIPI", result.Reason);
        Assert.Empty(sender.Sent);
    }

    // ---------------------------------------------------------------
    // CheckGuard - evaluates the guard without sending anything. Used by
    // MacroPresser (ref/docs/injection.md) to refuse a whole multi-step
    // macro run up front, before MacroRunner ever calls KeyDown/KeyUp - see
    // MacroPresserTests for the caller-level behaviour this exists for.
    // ---------------------------------------------------------------

    [Fact]
    public void CheckGuard_EliteForeground_ReturnsSent_ButNeverCallsSendInput()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        var result = injector.CheckGuard();

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public void CheckGuard_NotForeground_ReturnsGameNotForeground_AndLogsNothing()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);

        var result = injector.CheckGuard();

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);
        Assert.Contains("active window", result.Reason);
        Assert.Empty(sender.Sent);
        // Unlike Inject's own refusal path, CheckGuard logs nothing itself -
        // nothing was actually attempted yet, so the caller decides whether
        // and how to report it (MacroPresser's caller does, at the Press
        // diagnostics category, not Injection).
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void CheckGuard_UipiSuspected_ReturnsUipiSuspected_WithOperatorReadableReason()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        ForegroundContext Context() => new("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);

        var result = injector.CheckGuard();

        Assert.Equal(InjectionOutcome.UipiSuspected, result.Outcome);
        Assert.Contains("UIPI", result.Reason);
        Assert.Empty(sender.Sent);
    }

    // ---------------------------------------------------------------
    // UIPI outcome distinct from "not foreground"
    // ---------------------------------------------------------------

    [Fact]
    public void KeyDown_UipiSuspected_NeverCallsSendInput_AndLogsDistinctOutcome()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        ForegroundContext Context() => new("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x11, false));

        Assert.Empty(sender.Sent);
        var evt = Assert.Single(log.Snapshot());
        Assert.Contains("UipiSuspected", evt.Message);
        Assert.DoesNotContain("GameNotForeground", evt.Message);
    }

    // ---------------------------------------------------------------
    // O8's fix: a refused KeyDown releases whatever this instance already
    // sent as "down" and hasn't released yet, before the attempt returns -
    // see Win32KeyInjector's own remarks and ref/docs/injection.md.
    // ---------------------------------------------------------------

    [Fact]
    public void KeyDown_RefusedMidChord_ReleasesAlreadyHeldModifiers_InReverseOrder()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var context = EliteOk();
        ForegroundContext Context() => context;
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);

        var ctrl = new ScancodeInfo(0x1D, false); // Key_LeftControl
        var alt = new ScancodeInfo(0x38, false);  // Key_LeftAlt
        var space = new ScancodeInfo(0x39, false); // Key_Space (the main key)

        // Modifiers go down while Elite is foreground...
        injector.KeyDown(ctrl);
        injector.KeyDown(alt);

        // ...then focus changes before the main key-down, exactly the O8
        // scenario (an overlay, a notification, alt-tab mid-chord).
        context = NotElite();
        injector.KeyDown(space);

        // The main key was refused and never sent; the two modifiers that
        // WERE sent are released, most-recently-pressed first (LIFO) -
        // exactly like MacroRunner's own end-of-chord release order.
        Assert.Equal(4, sender.Sent.Count);
        Assert.Equal((ushort)0x1D, sender.Sent[0].U.ki.wScan);
        Assert.Equal(ScanCodeFlag, sender.Sent[0].U.ki.dwFlags);
        Assert.Equal((ushort)0x38, sender.Sent[1].U.ki.wScan);
        Assert.Equal(ScanCodeFlag, sender.Sent[1].U.ki.dwFlags);

        var firstRelease = sender.Sent[2];
        var secondRelease = sender.Sent[3];
        Assert.Equal((ushort)0x38, firstRelease.U.ki.wScan); // Alt released first
        Assert.Equal(ScanCodeFlag | KeyUpFlag, firstRelease.U.ki.dwFlags);
        Assert.Equal((ushort)0x1D, secondRelease.U.ki.wScan); // Control released last
        Assert.Equal(ScanCodeFlag | KeyUpFlag, secondRelease.U.ki.dwFlags);

        // The reported outcome is still the failure, not success - the
        // cleanup changes what gets sent afterward, not the verdict on the
        // refused key-down itself.
        var events = log.Snapshot();
        Assert.Equal(2, events.Count);
        Assert.Contains("GameNotForeground", events[0].Message);
        Assert.Equal(DiagnosticLevel.Warn, events[0].Level);
        Assert.Contains("released 2 already-pressed key(s)", events[1].Message);
        Assert.Equal(DiagnosticLevel.Warn, events[1].Level);
    }

    [Fact]
    public void KeyDown_RefusedBeforeAnyKeyWentDown_ReleasesNothing()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x39, false)); // Key_Space, refused immediately

        Assert.Empty(sender.Sent);

        // Exactly one log entry - the refusal itself. No cleanup log, since
        // there was nothing held to release.
        var evt = Assert.Single(log.Snapshot());
        Assert.Equal(DiagnosticLevel.Warn, evt.Level);
        Assert.Contains("GameNotForeground", evt.Message);
    }

    // ---------------------------------------------------------------
    // SendInput-reported failure (distinct from a refusal - the guard
    // passed, but the OS call itself failed)
    // ---------------------------------------------------------------

    [Fact]
    public void KeyDown_SendInputReturnsZero_LogsError_EvenWithDiagnosticModeOff()
    {
        var sender = new RecordingSender { InsertedToReturn = 0, Win32ErrorToReturn = 1400 };
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x11, false));

        var evt = Assert.Single(log.Snapshot());
        Assert.Equal(DiagnosticLevel.Error, evt.Level);
        Assert.Contains("SendInput returned 0", evt.Message);
        Assert.Contains("1400", evt.Detail);
    }

    // ---------------------------------------------------------------
    // Diagnostic-mode-gated success logging
    // ---------------------------------------------------------------

    [Fact]
    public void KeyDown_Sent_DiagnosticModeOff_LogsNothing()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x11, false));

        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void KeyDown_Sent_DiagnosticModeOn_LogsInfo()
    {
        var sender = new RecordingSender();
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => true, EliteOk, sender.Send);

        injector.KeyDown(new ScancodeInfo(0x11, false));

        var evt = Assert.Single(log.Snapshot());
        Assert.Equal(DiagnosticLevel.Info, evt.Level);
        Assert.Contains("Sent", evt.Message);
    }

    // ---------------------------------------------------------------
    // Fake clock, for the full-chord integration tests below. Same shape
    // as MacroRunnerTests.FakeTimeProvider - see that file's remarks for
    // why CreateTimer (not just GetUtcNow) must be virtualized, and why
    // PumpAsync waits for the code under test to arm a timer before it
    // advances one (O6).
    // ---------------------------------------------------------------

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
    /// Upper bound for every <c>await</c> on a <see cref="MacroRunner.RunAsync"/>
    /// task in this file - same rationale and value as
    /// <c>MacroRunnerTests.RunHangGuard</c> (O27).
    /// </summary>
    private static readonly TimeSpan RunHangGuard = TimeSpan.FromSeconds(10);

    private sealed record TimedSend(NativeMethods.INPUT Input, DateTimeOffset At);

    private sealed class TimedRecordingSender
    {
        private readonly TimeProvider _clock;
        public List<TimedSend> Sent { get; } = new();

        public TimedRecordingSender(TimeProvider clock) => _clock = clock;

        public (uint Inserted, int Win32Error) Send(NativeMethods.INPUT[] inputs)
        {
            foreach (var input in inputs)
            {
                Sent.Add(new TimedSend(input, _clock.GetUtcNow()));
            }

            return (1u, 0);
        }
    }

    // ---------------------------------------------------------------
    // Full chord, through the REAL MacroRunner + REAL Win32KeyInjector,
    // with only the two OS-facing delegates faked. Pins the exact
    // structures AND the ordering/timing together, against a two-modifier
    // chord (minimal.binds' existing "TwoModifiers" fixture action -
    // Key_Space main, Key_LeftControl then Key_LeftAlt modifiers, none
    // extended).
    // ---------------------------------------------------------------

    [Fact]
    public async Task MacroRunner_TwoModifierChord_ThroughRealInjector_ProducesCorrectStructures_Order_AndTiming()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var diagLog = new DiagnosticRingBuffer(64);
        var injector = new Win32KeyInjector(diagLog, () => false, EliteOk, sender.Send);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, diagLog);

        var bindingsResult = BindingsFile.Parse(ParseFixture("minimal.binds"));
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        var bindings = bindingsResult.File!;

        var macro = new MacroDefinition("chord-test", "Chord Test", new MacroStep[]
        {
            new PressStep("TwoModifiers", Repeat: 1, Hold: null),
        });

        var runTask = runner.RunAsync(macro, bindings);

        await PumpAsync(clock, MacroTimingDefaults.ModifierSettleGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);

        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(6, sender.Sent.Count);

        // Order: LeftControl down, LeftAlt down, Space down, Space up,
        // LeftAlt up, LeftControl up (modifiers released in reverse of the
        // order they went down - see MacroRunner.PressChordOnceAsync).
        // This test asserts the first four (structures + down-side
        // timing); MacroRunner_TwoModifierChord_ReleasesModifiers_InReverseOrder
        // asserts the remaining two.
        var ctrlDown = sender.Sent[0];
        var altDown = sender.Sent[1];
        var spaceDown = sender.Sent[2];
        var spaceUp = sender.Sent[3];

        Assert.Equal((ushort)0x1D, ctrlDown.Input.U.ki.wScan); // Key_LeftControl
        Assert.Equal(ScanCodeFlag, ctrlDown.Input.U.ki.dwFlags);

        Assert.Equal((ushort)0x38, altDown.Input.U.ki.wScan); // Key_LeftAlt
        Assert.Equal(ScanCodeFlag, altDown.Input.U.ki.dwFlags);

        Assert.Equal((ushort)0x39, spaceDown.Input.U.ki.wScan); // Key_Space
        Assert.Equal(ScanCodeFlag, spaceDown.Input.U.ki.dwFlags);

        Assert.Equal((ushort)0x39, spaceUp.Input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, spaceUp.Input.U.ki.dwFlags);

        // Timing: modifiers go down together (no gap modelled between
        // them), then the settle gap before the main key, then the hold
        // before its release.
        Assert.Equal(TimeSpan.Zero, altDown.At - ctrlDown.At);
        Assert.Equal(MacroTimingDefaults.ModifierSettleGap, spaceDown.At - altDown.At);
        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, spaceUp.At - spaceDown.At);
    }

    [Fact]
    public async Task MacroRunner_TwoModifierChord_ReleasesModifiers_InReverseOrder()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var diagLog = new DiagnosticRingBuffer(64);
        var injector = new Win32KeyInjector(diagLog, () => false, EliteOk, sender.Send);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, diagLog);

        var bindingsResult = BindingsFile.Parse(ParseFixture("minimal.binds"));
        var bindings = bindingsResult.File!;
        var macro = new MacroDefinition("chord-test-2", "Chord Test 2", new MacroStep[]
        {
            new PressStep("TwoModifiers", Repeat: 1, Hold: null),
        });

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.ModifierSettleGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(6, sender.Sent.Count);
        var altUp = sender.Sent[4];
        var ctrlUp = sender.Sent[5];

        Assert.Equal((ushort)0x38, altUp.Input.U.ki.wScan); // Key_LeftAlt released first
        Assert.Equal(ScanCodeFlag | KeyUpFlag, altUp.Input.U.ki.dwFlags);
        Assert.Equal((ushort)0x1D, ctrlUp.Input.U.ki.wScan); // Key_LeftControl released last
        Assert.Equal(ScanCodeFlag | KeyUpFlag, ctrlUp.Input.U.ki.dwFlags);
    }

    [Fact]
    public async Task MacroRunner_ExtendedKeyPress_BuildsExtendedFlagStructures_ForDownAndUp()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var diagLog = new DiagnosticRingBuffer(64);
        var injector = new Win32KeyInjector(diagLog, () => false, EliteOk, sender.Send);
        var store = new GameStateStore(clock);
        var runner = new MacroRunner(injector, store, new JournalStateStore(), new PanelTabTracker(), clock, diagLog);

        // Hand-crafted XML for an extended-key action, per the established
        // convention (ref/docs/bindings.md) of using inline XML for a
        // branch no committed fixture covers - minimal.binds/sample.binds
        // have no arrow-key action.
        var xml = """
            <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
              <UIUp>
                <Primary Device="Keyboard" Key="Key_UpArrow" />
                <Secondary Device="{NoDevice}" Key="" />
              </UIUp>
            </Root>
            """;
        var bindingsResult = BindingsFile.Parse(xml);
        Assert.True(bindingsResult.Success, bindingsResult.Error);
        var bindings = bindingsResult.File!;

        var macro = new MacroDefinition("extended-test", "Extended Test", new MacroStep[]
        {
            new PressStep("UIUp", Repeat: 1, Hold: null),
        });

        var runTask = runner.RunAsync(macro, bindings);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await runTask.WaitAsync(RunHangGuard);

        Assert.Equal(MacroRunOutcome.Success, result.Outcome);
        Assert.Equal(2, sender.Sent.Count);

        var down = sender.Sent[0];
        var up = sender.Sent[1];

        Assert.Equal((ushort)0x48, down.Input.U.ki.wScan); // Key_UpArrow
        Assert.Equal(ScanCodeFlag | ExtendedFlag, down.Input.U.ki.dwFlags);

        Assert.Equal((ushort)0x48, up.Input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag | ExtendedFlag | KeyUpFlag, up.Input.U.ki.dwFlags);
    }

    private static string ParseFixture(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bindings", relativePath);
        return File.ReadAllText(path);
    }
}
