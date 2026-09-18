namespace LunaPanel.Core.Bindings;

/// <summary>
/// Outcome of resolving one action to a keyboard chord: either a usable
/// <see cref="Chord"/>, or an <see cref="UnboundReason"/> specific enough to
/// explain to a player why the button does nothing.
/// </summary>
/// <param name="IsBound">Whether a keyboard chord was resolved.</param>
/// <param name="Chord">The resolved chord, or <see langword="null"/> when <paramref name="IsBound"/> is false.</param>
/// <param name="Reason">Why resolution failed, or <see langword="null"/> when <paramref name="IsBound"/> is true.</param>
/// <param name="ReasonDetail">Extra context for the reason - currently only populated for <see cref="UnboundReason.UnknownKey"/>, naming the offending key.</param>
public sealed record BindResolution(bool IsBound, ResolvedChord? Chord, UnboundReason? Reason, string? ReasonDetail)
{
    public static BindResolution Bound(ResolvedChord chord) => new(true, chord, null, null);

    public static BindResolution Unbound(UnboundReason reason, string? detail = null) => new(false, null, reason, detail);
}
