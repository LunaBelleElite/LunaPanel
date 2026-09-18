namespace LunaPanel.Core.Macros;

/// <summary>
/// Timing constants for chord injection, measured (not guessed) against what
/// actually works with Elite Dangerous - see the macro grammar's timing
/// research recorded in the task brief and <c>ref/docs/design-decisions.md</c>.
///
/// <b>Do not "optimise" any of these to zero or to a smaller value.</b> A
/// hold shorter than ~100ms is the single most common cause of "the
/// keypress didn't register" - Elite's own input handling can miss a chord
/// whose main key goes down and back up within the same frame or two. These
/// are defaults, not floors enforced anywhere else in code - <see cref="PressStep.Hold"/>
/// can still override <see cref="DefaultHoldDuration"/> per step, but the
/// default itself must stay comfortably above the measured minimum.
/// </summary>
public static class MacroTimingDefaults
{
    /// <summary>
    /// How long a chord's main key is held down by default. The measured
    /// minimum that reliably registers is 100ms; 150ms is used here as the
    /// safer default so a macro author who never overrides <c>holdMs</c>
    /// still gets a comfortable margin rather than the bare minimum.
    /// </summary>
    public static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Minimum gap left between the release of one chord press and the next
    /// press beginning, within a single <c>press</c> step's <c>repeat</c>
    /// loop. Elite needs this much separation to see two presses as
    /// distinct keystrokes rather than one held-down chord.
    /// </summary>
    public static readonly TimeSpan InterPressGap = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The measured floor for <see cref="DefaultHoldDuration"/> - a hold
    /// shorter than this is where Elite starts silently missing keystrokes
    /// (see this type's own remarks above). Named as its own constant
    /// (2026-09-08, macro timing settings pane -
    /// <c>ref/docs/macro-timing.md</c>'s "the cliff") so the settings pane's
    /// "below the tested minimum" warning reads the same number this file
    /// already measured, rather than restating "100" a second time.
    /// </summary>
    public static readonly TimeSpan MeasuredMinimumHoldDuration = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The measured floor for <see cref="InterPressGap"/> - identical to that
    /// constant's own value, since 100ms already IS the smallest gap Elite
    /// reliably reads as two presses rather than one held key. Named
    /// separately anyway, symmetric with <see cref="MeasuredMinimumHoldDuration"/>,
    /// so the settings pane's warning check reads one deliberately-named
    /// "this is the floor" value for each field.
    /// </summary>
    public static readonly TimeSpan MeasuredMinimumInterPressGap = InterPressGap;

    /// <summary>
    /// How long a chord's modifiers are held down, alone, before its main
    /// key goes down. Elite can miss the modified interpretation of a chord
    /// entirely if the main key arrives before the modifiers have "settled".
    /// Not applied when a chord has no modifiers.
    /// </summary>
    public static readonly TimeSpan ModifierSettleGap = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Default <c>timeoutMs</c> for a <c>waitForEdge</c> step when the macro
    /// JSON does not name one. Measured live (2026-09-08, O28): grants
    /// observed at 51ms, 189ms and 12.8s, and an earlier live check recorded
    /// station/carrier docking acknowledged in under a second. 20s clears the
    /// slowest genuine observation with margin, while turning what used to be
    /// an unbounded wait into a bounded one.
    ///
    /// This bound is also a crude but real causality filter, not merely a
    /// convenience: an edge arriving three minutes after the keystrokes that
    /// supposedly caused it (the live O28 failure - a stray commander
    /// keypress landed the grant 198 seconds later, on the wrong tab, and the
    /// macro claimed credit for it) is not plausibly the macro's own doing.
    /// Bounding the wait means a late, unrelated edge can no longer be
    /// mistaken for the macro's result.
    /// </summary>
    public static readonly TimeSpan DefaultWaitForEdgeTimeout = TimeSpan.FromSeconds(20);
}
