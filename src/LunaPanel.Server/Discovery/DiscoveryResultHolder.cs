namespace LunaPanel.Server.Discovery;

/// <summary>
/// A mutable, swap-in-place holder for the current <see cref="PathDiscoveryResult"/>,
/// letting "Refresh bindings" (<c>ref/docs/bindings-source.md</c>) re-run the
/// whole discovery sweep and have every subsequent read see the fresh result,
/// without restarting the host. Registered once as a DI singleton;
/// <see cref="LunaPanel.Server.Bindings.LiveBindingsReader"/> reads
/// <see cref="Current"/> fresh on every call, so a refresh takes effect on
/// the very next panel request.
///
/// <b>Why this shape, not another one.</b> <see cref="PathDiscoveryResult"/>
/// is itself an immutable record, so the only thing that ever needs to
/// change is which instance of it is "current" - a plain field with an
/// atomic reference swap does that with no torn reads possible: a caller
/// that reads <see cref="Current"/> mid-refresh gets either the complete old
/// result or the complete new one, never a partially-updated mix of both,
/// because a reference assignment is indivisible on .NET regardless of
/// which CPU core reads it. Two other shapes were considered and rejected:
/// an event/callback raised on refresh would let every consumer react
/// immediately, but adds an ordering hazard between "old result cleared" and
/// "new result delivered" that a plain swap does not have; re-registering a
/// new <see cref="PathDiscoveryResult"/> singleton through the DI container
/// is not possible once the container is built (<c>WebApplication.Build()</c>
/// has already run by the time a refresh request can arrive). A lock is not
/// needed for the swap itself (<see cref="Volatile"/> already guarantees the
/// read/write are not reordered or torn); one would only be needed if a
/// caller needed several reads of <see cref="Current"/> to observe the same
/// instance across multiple statements, which no consumer here does - every
/// read is a single field access at the top of a method, used in full before
/// the next read of it happens.
/// </summary>
public sealed class DiscoveryResultHolder
{
    private PathDiscoveryResult _current;

    public DiscoveryResultHolder(PathDiscoveryResult initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public PathDiscoveryResult Current => Volatile.Read(ref _current);

    /// <summary>Swaps in a freshly re-run discovery result. Safe to call while another thread is mid-<see cref="Current"/> read.</summary>
    public void Replace(PathDiscoveryResult next)
    {
        Volatile.Write(ref _current, next ?? throw new ArgumentNullException(nameof(next)));
    }
}
