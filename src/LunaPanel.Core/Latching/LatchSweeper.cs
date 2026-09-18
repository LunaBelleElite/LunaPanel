namespace LunaPanel.Core.Latching;

/// <summary>
/// The polling half of the release rules. Three of the five have something
/// that pushes at us - the commander taps (rule 1), a live channel's own
/// stream ends (rule 2), the host signals shutdown (rule 4). The other two
/// have nothing to subscribe to: Windows raises no "Elite stopped being
/// foreground" event LunaPanel can hook (rule 3), and a deadline passing is
/// not an event at all (rule 5). So both are polled, here.
///
/// <b>Nothing is polled while nothing is latched.</b> <see cref="Tick"/>
/// checks <see cref="LatchRegistry.Any"/> before it evaluates the foreground
/// predicate at all, so an idle server makes no <c>OpenProcess</c>/
/// <c>OpenProcessToken</c> chain call on this path - the cost only exists
/// while a key is actually being held, which is the only time either rule
/// could fire.
/// </summary>
public static class LatchSweeper
{
    /// <summary>
    /// One poll. Returns how many latches it released, so a caller (or a
    /// test) can tell a tick that did something from a tick that did not.
    /// <paramref name="eliteIsForeground"/> is a delegate rather than a
    /// value because evaluating it costs a Win32 round trip
    /// (<c>Win32KeyInjector.CheckGuard</c>) that must not be spent on an
    /// idle server.
    /// </summary>
    public static int Tick(LatchRegistry latches, Func<bool> eliteIsForeground, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(latches);
        ArgumentNullException.ThrowIfNull(eliteIsForeground);
        ArgumentNullException.ThrowIfNull(clock);

        if (!latches.Any)
        {
            return 0;
        }

        return latches.Sweep(eliteIsForeground(), clock.GetUtcNow());
    }

    /// <summary>
    /// Polls forever at <paramref name="interval"/> until cancelled.
    /// Cancellation is the normal way this ends (host shutdown), so it
    /// returns rather than throwing - rule 4's own release is registered
    /// separately on the host lifetime and does not depend on this loop
    /// still running.
    /// </summary>
    public static async Task RunAsync(
        LatchRegistry latches,
        Func<bool> eliteIsForeground,
        TimeProvider clock,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(latches);
        ArgumentNullException.ThrowIfNull(eliteIsForeground);
        ArgumentNullException.ThrowIfNull(clock);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, clock, cancellationToken).ConfigureAwait(false);
                Tick(latches, eliteIsForeground, clock);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping - normal, not an error.
        }
    }
}
