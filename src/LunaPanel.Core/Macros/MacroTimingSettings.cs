namespace LunaPanel.Core.Macros;

/// <summary>
/// The two of <see cref="MacroTimingDefaults"/>'s three constants a commander
/// can override - <c>ModifierSettleGap</c> stays a constant, per
/// <c>ref/docs/macro-timing.md</c>'s "What becomes configurable" (it only
/// affects chords carrying a modifier, has never been implicated in a
/// failure, and a third control costs every commander to serve a case
/// nobody has hit).
///
/// Server-wide, not per device (<c>ref/docs/macro-timing.md</c>'s "Scope:
/// the PC, not the device") - see <see cref="MacroTimingSettingsStore"/>'s
/// own remarks for the storage consequence of that.
/// </summary>
/// <param name="HoldDuration">
/// How long a chord's main key is held down. Overrides
/// <see cref="MacroTimingDefaults.DefaultHoldDuration"/> for any macro press
/// that doesn't itself specify <c>holdMs</c> - a per-step <c>holdMs</c> still
/// wins over this (<c>ref/docs/macro-timing.md</c>'s "Defaults and
/// compatibility": per-step beats the commander's setting, which beats the
/// shipped default).
/// </param>
/// <param name="InterPressGap">
/// The pause between one press finishing and the next beginning, inside a
/// macro's <c>repeat</c> loop. Overrides <see cref="MacroTimingDefaults.InterPressGap"/>.
/// </param>
public sealed record MacroTimingSettings(TimeSpan HoldDuration, TimeSpan InterPressGap)
{
    /// <summary>
    /// Lowest value either field will accept, in milliseconds. Below this the
    /// number stops meaning "a duration" at all (zero or negative) - refused
    /// rather than persisted. Not the same thing as
    /// <see cref="MacroTimingDefaults.MeasuredMinimumHoldDuration"/>/
    /// <see cref="MacroTimingDefaults.MeasuredMinimumInterPressGap"/>, which
    /// are a softer, allowed-with-a-warning floor the spec deliberately does
    /// NOT enforce as a hard block (<c>ref/docs/macro-timing.md</c>'s "the
    /// cliff": "allow it, label it, and never let a commander arrive there
    /// without being told").
    /// </summary>
    public const int MinMs = 1;

    /// <summary>
    /// Highest value either field will accept, in milliseconds. The spec
    /// names no upper bound at all - only a measured floor - so this ceiling
    /// is this dispatch's own sanity guard against an obviously wrong entry
    /// (an extra typed zero, a pasted value) rather than a spec-mandated
    /// number. 5000ms is generous against the shipped defaults (100-150ms)
    /// while still catching that class of mistake before it makes every
    /// macro press take seconds.
    /// </summary>
    public const int MaxMs = 5000;

    /// <summary>
    /// The shipped defaults, exactly - so a commander who never opens the
    /// settings pane sees no change whatsoever.
    /// </summary>
    public static readonly MacroTimingSettings Default = new(
        MacroTimingDefaults.DefaultHoldDuration,
        MacroTimingDefaults.InterPressGap);

    /// <summary>
    /// Whether both millisecond values fall within [<see cref="MinMs"/>,
    /// <see cref="MaxMs"/>]. This is the HARD refusal boundary
    /// (<c>MacroTimingEndpoint.TryParse</c> refuses outside it); the
    /// measured-minimum warning is a separate, softer check the client
    /// itself renders - a value below the measured minimum is still valid
    /// here and gets persisted, just labelled.
    /// </summary>
    public static bool IsValid(int holdMs, int interPressGapMs) =>
        holdMs is >= MinMs and <= MaxMs && interPressGapMs is >= MinMs and <= MaxMs;
}
