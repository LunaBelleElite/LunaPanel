using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Input;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Input;

namespace LunaPanel.Tests.Injection;

/// <summary>
/// Drives <see cref="ChordPresser.PressAsync"/> - the single-tap counterpart
/// to <c>MacroRunner.PressChordOnceAsync</c> that <c>POST /api/press</c> uses
/// (see <c>ref/docs/injection.md</c>). Same test-seam discipline as
/// <see cref="Win32KeyInjectorTests"/>: every foreground/send delegate is
/// faked, so no test here can reach a real Win32 API.
/// </summary>
public class ChordPresserTests
{
    private const uint ScanCodeFlag = 0x0008;
    private const uint KeyUpFlag = 0x0002;

    private static ForegroundContext EliteOk() => new("EliteDangerous64", IntegrityLevel.Medium, IntegrityLevel.Medium);
    private static ForegroundContext NotElite() => new("Discord", IntegrityLevel.Medium, IntegrityLevel.Medium);

    // Same fake clock shape as Win32KeyInjectorTests.FakeTimeProvider /
    // MacroRunnerTests.FakeTimeProvider - see those files' remarks (and
    // tests/notes/open-items.md O6) for why CreateTimer must be virtualized
    // and why PumpAsync waits for the code under test to arm a timer before
    // it advances one.
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
    /// Upper bound for every <c>await</c> on a <see cref="ChordPresser.PressAsync"/>
    /// task in this file - same rationale and value as
    /// <c>MacroRunnerTests.RunHangGuard</c> (O27): a mutation that made the
    /// awaited call need one more clock advance than a test supplies would
    /// otherwise hang silently against a fake clock nothing ever advances
    /// again.
    /// </summary>
    private static readonly TimeSpan PressHangGuard = TimeSpan.FromSeconds(10);

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

    [Fact]
    public async Task PressAsync_NoModifiers_Foreground_SendsDownThenUp_WithDefaultHoldBetween_AndReturnsSent()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        var mainKey = new ScancodeInfo(0x39, false); // Key_Space
        var chord = new ResolvedChord(mainKey, Array.Empty<ScancodeInfo>(), "Space");

        var pressTask = ChordPresser.PressAsync(injector, chord, clock);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
        Assert.Equal(2, sender.Sent.Count);

        var down = sender.Sent[0];
        var up = sender.Sent[1];
        Assert.Equal((ushort)0x39, down.Input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag, down.Input.U.ki.dwFlags);
        Assert.Equal((ushort)0x39, up.Input.U.ki.wScan);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, up.Input.U.ki.dwFlags);
        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, up.At - down.At);
    }

    [Fact]
    public async Task PressAsync_TwoModifiers_Foreground_SendsInOrder_WithSettleGap_ThenReleasesInReverse()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, EliteOk, sender.Send);

        var ctrl = new ScancodeInfo(0x1D, false); // Key_LeftControl
        var alt = new ScancodeInfo(0x38, false);  // Key_LeftAlt
        var space = new ScancodeInfo(0x39, false); // Key_Space
        var chord = new ResolvedChord(space, new[] { ctrl, alt }, "Ctrl+Alt+Space");

        var pressTask = ChordPresser.PressAsync(injector, chord, clock);
        await PumpAsync(clock, MacroTimingDefaults.ModifierSettleGap);
        await PumpAsync(clock, MacroTimingDefaults.DefaultHoldDuration);
        var result = await pressTask.WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.Sent, result.Outcome);
        Assert.Equal(6, sender.Sent.Count);

        var ctrlDown = sender.Sent[0];
        var altDown = sender.Sent[1];
        var spaceDown = sender.Sent[2];
        var spaceUp = sender.Sent[3];
        var altUp = sender.Sent[4];
        var ctrlUp = sender.Sent[5];

        Assert.Equal((ushort)0x1D, ctrlDown.Input.U.ki.wScan);
        Assert.Equal((ushort)0x38, altDown.Input.U.ki.wScan);
        Assert.Equal((ushort)0x39, spaceDown.Input.U.ki.wScan);
        Assert.Equal((ushort)0x39, spaceUp.Input.U.ki.wScan);
        Assert.Equal((ushort)0x38, altUp.Input.U.ki.wScan); // Alt released first (reverse order)
        Assert.Equal((ushort)0x1D, ctrlUp.Input.U.ki.wScan); // Control released last

        Assert.Equal(MacroTimingDefaults.ModifierSettleGap, spaceDown.At - altDown.At);
        Assert.Equal(MacroTimingDefaults.DefaultHoldDuration, spaceUp.At - spaceDown.At);
    }

    [Fact]
    public async Task PressAsync_NotForeground_ReturnsGameNotForeground_NoKeyDownSent_ButKeyUpStillSent()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var log = new DiagnosticRingBuffer(16);
        var injector = new Win32KeyInjector(log, () => false, NotElite, sender.Send);

        // No modifiers deliberately: with modifiers present, ChordPresser
        // always awaits the settle gap regardless of whether any KeyDown
        // was actually refused (same as MacroRunner - see its own remarks),
        // and this test's whole point is that the call completes with no
        // clock advance needed when nothing was ever sent.
        var space = new ScancodeInfo(0x39, false);
        var chord = new ResolvedChord(space, Array.Empty<ScancodeInfo>(), "Space");

        // Not foreground refuses the KeyDown, so the hold is never reached -
        // this call needs no PumpAsync at all to complete. Bounded anyway
        // (O27): if a mutation made it reach the hold after all, nothing
        // here would ever advance the clock again to release it.
        var result = await ChordPresser.PressAsync(injector, chord, clock).WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.GameNotForeground, result.Outcome);

        // KeyUp always bypasses the guard (see Win32KeyInjector's own
        // remarks and ref/docs/injection.md's "Key-up bypasses the
        // foreground guard") - so the release is still sent even though the
        // key was never actually pressed. This is "harmless noise", not a
        // defect - this test exists to prove it is exactly this shape and
        // no more (no KeyDown flag anywhere).
        var release = Assert.Single(sender.Sent);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, release.Input.U.ki.dwFlags);
        Assert.Equal((ushort)0x39, release.Input.U.ki.wScan);
    }

    [Fact]
    public async Task PressAsync_UipiSuspected_ReturnsUipiSuspected_AndSkipsTheHoldDelay()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var sender = new TimedRecordingSender(clock);
        var log = new DiagnosticRingBuffer(16);
        ForegroundContext Context() => new("EliteDangerous64", IntegrityLevel.High, IntegrityLevel.Medium);
        var injector = new Win32KeyInjector(log, () => false, Context, sender.Send);

        var space = new ScancodeInfo(0x39, false);
        var chord = new ResolvedChord(space, Array.Empty<ScancodeInfo>(), "Space");

        // No PumpAsync call at all: if the hold delay were mistakenly still
        // awaited on a refused main key, this call would hang forever
        // against a clock nothing ever advances - completing at all is part
        // of what this test proves. Bounded per O27 so that exact hang
        // becomes a named timeout rather than a silent one, should it recur.
        var result = await ChordPresser.PressAsync(injector, chord, clock).WaitAsync(PressHangGuard);

        Assert.Equal(InjectionOutcome.UipiSuspected, result.Outcome);
        var release = Assert.Single(sender.Sent);
        Assert.Equal(ScanCodeFlag | KeyUpFlag, release.Input.U.ki.dwFlags);
    }
}
