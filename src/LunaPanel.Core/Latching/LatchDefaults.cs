namespace LunaPanel.Core.Latching;

/// <summary>
/// The two timing constants the latch feature owns. Deliberately NOT in
/// <c>MacroTimingDefaults</c>: those are measured properties of Elite's own
/// input handling (how long a transient press must be held to register at
/// all), and the commander's macro timing settings pane can move them. These
/// two are safety limits on a key that is already down, they are not
/// measured against the game, and nothing in the settings pane reaches them.
/// </summary>
public static class LatchDefaults
{
    /// <summary>
    /// Rule 5, the backstop (<c>ref/docs/latching-keys.md</c>): a latched key
    /// is released this long after it went down, whatever else happens. The
    /// commander holds secondary fire for fifteen to thirty seconds to fire
    /// mining limpets, so two minutes never bites real use.
    ///
    /// <b>Why a cap exists at all, given this project's standing "never gate
    /// a macro" rule.</b> A cap is technically a limit placed on the
    /// commander, and the user ruled on it directly (2026-09-09) rather than
    /// having it assumed. It earns its exception because of what it protects
    /// against: not a refused action, but the commander's ship firing
    /// unattended because something failed silently somewhere else. Every
    /// other release rule depends on something still working - a live
    /// channel that notices it dropped, a foreground check that still runs,
    /// a shutdown that is clean enough to run a hook. This one does not.
    /// </summary>
    public static readonly TimeSpan Backstop = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How often <see cref="LatchSweeper"/> re-checks the two release rules
    /// nothing pushes at us - rule 3 (Elite lost foreground) and rule 5 (the
    /// backstop elapsed). Neither has an event to subscribe to, so both are
    /// polled. Only ever polled while at least one key is actually latched
    /// (see <see cref="LatchSweeper.RunAsync"/>), so an idle server does no
    /// work and makes no Win32 call on this path at all.
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(250);
}
