using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Drives <see cref="GameStateStore"/> with a hand-written fake
/// <see cref="TimeProvider"/> that virtualizes <see cref="TimeProvider.CreateTimer"/>
/// as well as <see cref="TimeProvider.GetUtcNow"/> (the existing
/// <c>ManualTimeProvider</c> used elsewhere in this suite only overrides
/// <c>GetUtcNow</c>, which is enough for timestamping but not for anything
/// that actually waits - <see cref="GameStateStore.WaitForAsync"/> needs its
/// <c>Task.Delay(..., TimeProvider, ...)</c> calls driven by a fake clock, so
/// a test can advance virtual time and observe the timeout fire without ever
/// sleeping for real).
/// </summary>
public class GameStateStoreTests
{
    /// <summary>
    /// A <see cref="TimeProvider"/> whose clock only moves when
    /// <see cref="Advance"/> is called, and whose <see cref="ITimer"/>s fire
    /// synchronously, inline, from that same call - so advancing time and
    /// observing its effect never requires a real wait.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly object _lock = new();
        private DateTimeOffset _utcNow;
        private readonly List<PendingTimer> _pending = new();

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
            }

            return timer;
        }

        /// <summary>
        /// Moves the virtual clock forward by <paramref name="delta"/> and
        /// fires every timer now due, synchronously, on the calling thread.
        /// </summary>
        public void Advance(TimeSpan delta)
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
            }

            foreach (var timer in due)
            {
                timer.InvokeCallback();
            }
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
                if (!Disposed)
                {
                    _callback(_state);
                }
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

    private static StatusSnapshot Snapshot(uint flags) => new(flags, null, null, GameRunning: true, SignedIn: true);

    [Fact]
    public void Current_BeforeAnyUpdate_IsNull()
    {
        var store = new GameStateStore(new FakeTimeProvider(DateTimeOffset.UtcNow));

        Assert.Null(store.Current);
    }

    [Fact]
    public void UpdateSnapshot_MakesTheSnapshotAvailableAsCurrent()
    {
        var store = new GameStateStore(new FakeTimeProvider(DateTimeOffset.UtcNow));

        store.UpdateSnapshot(Snapshot(1));

        Assert.Equal(Snapshot(1), store.Current);
    }

    [Fact]
    public void UpdateSnapshot_RaisesChanged_WithTheNewSnapshot()
    {
        var store = new GameStateStore(new FakeTimeProvider(DateTimeOffset.UtcNow));
        StatusSnapshot? seen = null;
        var raisedCount = 0;
        store.Changed += s => { seen = s; raisedCount++; };

        store.UpdateSnapshot(Snapshot(7));

        Assert.Equal(1, raisedCount);
        Assert.Equal(Snapshot(7), seen);
    }

    [Fact]
    public async Task WaitForAsync_ConditionAlreadyTrue_ReturnsTrue_Immediately()
    {
        var store = new GameStateStore(new FakeTimeProvider(DateTimeOffset.UtcNow));
        store.UpdateSnapshot(Snapshot(1));

        var result = await store.WaitForAsync(s => s?.Flags == 1, TimeSpan.FromSeconds(10));

        Assert.True(result);
    }

    [Fact]
    public async Task WaitForAsync_CompletesTrue_WhenACHangeSatisfiesTheCondition_WithoutWaitingForTimeout()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);

        var waitTask = store.WaitForAsync(s => s?.Flags == 42, TimeSpan.FromSeconds(30));

        Assert.False(waitTask.IsCompleted);

        store.UpdateSnapshot(Snapshot(1)); // does not satisfy the condition
        store.UpdateSnapshot(Snapshot(42)); // satisfies it

        // WaitAsync is a failure bound, not a delay (same convention as
        // JournalStateStoreTests): waitTask's own 30-second timeout is
        // measured against this FakeTimeProvider's clock, which nothing
        // advances after this point, so a mutation that stopped the wait
        // from noticing the second UpdateSnapshot would otherwise hang this
        // test forever rather than fail it (O27).
        var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
    }

    [Fact]
    public async Task WaitForAsync_TimesOut_WhenConditionNeverBecomesTrue_ViaAdvancingTheFakeClockOnly()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);

        var waitTask = store.WaitForAsync(s => s?.Flags == 999, TimeSpan.FromMilliseconds(50));

        Assert.False(waitTask.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(60));

        // Bounded for the same reason as the sibling test above: nothing
        // advances the fake clock again after this point, so a mutation to
        // the deadline check that needed one more advance would otherwise
        // hang rather than fail (O27).
        var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result);
    }

    [Fact]
    public async Task WaitForAsync_UpdateLandsBetweenTheConditionCheckAndTheSignalCapture_StillSeesIt_WithoutWaitingForTimeout()
    {
        // Regression for O25: WaitForAsync used to check the condition FIRST
        // and only afterward capture the change signal, so an update landing
        // in that gap was missed until the NEXT update or the timeout. The
        // condition delegate is the only seam available to force that gap
        // deterministically (O25's own note: neither store exposes a hook to
        // interleave a record between the check and the capture) - its first
        // call fires the update itself, standing in for "something else
        // updated the store right here", then answers as if that update had
        // not yet been observed; its second call answers against the real,
        // now-current snapshot.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new GameStateStore(clock);
        var checkCount = 0;

        var waitTask = store.WaitForAsync(snapshot =>
        {
            checkCount++;
            if (checkCount == 1)
            {
                store.UpdateSnapshot(Snapshot(42));
                return false;
            }

            return snapshot?.Flags == 42;
        }, TimeSpan.FromSeconds(30));

        // With the signal captured before the check, the loop's very next
        // iteration observes the already-completed old signal and re-checks
        // the condition immediately - no clock advance or second real update
        // needed. With the old (buggy) ordering, the loop captures the NEW
        // signal instead, which nothing ever completes, so this would hang
        // against the fake clock's 30-second timeout (which nothing here
        // advances) until WaitAsync's own bound fails it (O27's convention).
        var result = await waitTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result);
        Assert.Equal(2, checkCount);
    }

    [Fact]
    public async Task WaitForAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var store = new GameStateStore(new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var cts = new CancellationTokenSource();

        var waitTask = store.WaitForAsync(s => s?.Flags == 999, TimeSpan.FromSeconds(30), cts.Token);
        cts.Cancel();

        // The loop only observes cancellation on its next condition check,
        // which happens as soon as *anything* wakes it - cancelling the
        // linked token wakes the pending Task.Delay immediately.
        //
        // WaitAsync bounds this the same as the two tests above: if a
        // mutation stopped the loop from observing cancellation, this would
        // otherwise hang against the fake clock's 30-second timeout, which
        // nothing here ever advances (O27).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
