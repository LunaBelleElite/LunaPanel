namespace LunaPanel.Core.GameState;

/// <summary>
/// Holds the current, immutable <see cref="StatusSnapshot"/> read from
/// Status.json and lets callers wait for a condition over it rather than
/// polling by hand. This type knows nothing about files or watchers - it is
/// handed snapshots by <see cref="StatusFileWatcher"/> via
/// <see cref="UpdateSnapshot"/> and never discovers anything itself, which is
/// what lets the macro engine depend on it without depending on I/O.
/// </summary>
/// <remarks>
/// <see cref="Current"/> is <see langword="null"/> until the first successful
/// read - the game may simply not be running yet, and that is not an error.
/// </remarks>
public sealed class GameStateStore
{
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private StatusSnapshot? _current;
    private TaskCompletionSource _changeSignal = NewSignal();

    public GameStateStore(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>The most recently applied snapshot, or <see langword="null"/> if none has arrived yet.</summary>
    public StatusSnapshot? Current
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>
    /// Raised every time <see cref="UpdateSnapshot"/> is called, with the new
    /// snapshot. Fired outside any internal lock, so a handler is free to
    /// call back into this store (e.g. read <see cref="Current"/>) without
    /// deadlocking.
    /// </summary>
    public event Action<StatusSnapshot?>? Changed;

    /// <summary>
    /// Replaces <see cref="Current"/> and wakes every pending
    /// <see cref="WaitForAsync"/> call so it can re-check its condition.
    /// Callers only ever pass a snapshot they trust - this store never
    /// second-guesses it (that judgment, e.g. "keep the last-good snapshot
    /// on a torn read," belongs to whoever calls this, not to the store).
    /// </summary>
    public void UpdateSnapshot(StatusSnapshot? snapshot)
    {
        TaskCompletionSource previousSignal;
        lock (_gate)
        {
            _current = snapshot;
            previousSignal = _changeSignal;
            _changeSignal = NewSignal();
        }

        previousSignal.TrySetResult();
        Changed?.Invoke(snapshot);
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> is true of <see cref="Current"/>,
    /// or until <paramref name="timeout"/> elapses (per the injected
    /// <see cref="TimeProvider"/>, not the wall clock), or
    /// <paramref name="cancellationToken"/> is cancelled. A simple
    /// check-then-wait-for-the-next-change loop - deliberately not reactive,
    /// since every condition check is a cheap synchronous delegate call over
    /// whatever snapshot happens to be current.
    /// </summary>
    /// <returns><see langword="true"/> if the condition was met, <see langword="false"/> on timeout.</returns>
    public async Task<bool> WaitForAsync(Func<StatusSnapshot?, bool> condition, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var deadline = _clock.GetUtcNow() + timeout;

        while (true)
        {
            // The change signal is captured BEFORE the condition is checked,
            // not after - the same ordering as JournalStateStore.WaitForEdgeAsync,
            // and for the same reason (see its own comment). The other order
            // loses an update that lands in the gap between the check and the
            // capture: the waiter would then park on a signal that has already
            // been replaced, so it stops noticing an update it should have
            // seen immediately. Bounded here rather than fatal (O25) - this
            // wait has a timeout and a safety poll re-fires roughly once a
            // second, so the old ordering's symptom was a wait resolving up
            // to a poll interval late, not a permanent hang.
            Task changeTask;
            lock (_gate) { changeTask = _changeSignal.Task; }

            if (condition(Current))
            {
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var remaining = deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delayTask = Task.Delay(remaining, _clock, iterationCts.Token);

            await Task.WhenAny(changeTask, delayTask).ConfigureAwait(false);
            iterationCts.Cancel();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
