namespace LunaPanel.Core.Latching;

/// <summary>
/// Why a latched key was released. <b>One member per release rule in
/// <c>ref/docs/latching-keys.md</c>, and the five there are all five here</b>
/// - a rule that gains no member is a rule nothing implements, which is the
/// specific failure that page exists to prevent ("the feature is mostly its
/// release rules, not its press"). Reported in the diagnostics log so a
/// commander reading it can tell "I tapped it again" apart from "the panel
/// went to sleep" apart from "nothing released it and the backstop had to".
/// </summary>
public enum LatchRelease
{
    /// <summary>Rule 1 - the commander tapped the latched button again. The feature itself.</summary>
    TappedAgain,

    /// <summary>Rule 2 - the device's live channel dropped (screen sleep, wifi loss, the tab closing, or a page change reopening the stream).</summary>
    ChannelClosed,

    /// <summary>Rule 3 - Elite is no longer the foreground window. A held key would otherwise land in whatever the commander alt-tabbed to.</summary>
    ForegroundLost,

    /// <summary>Rule 4 - the server is shutting down. Nothing would be left running to release it afterwards.</summary>
    ServerShutdown,

    /// <summary>Rule 5 - <see cref="LatchDefaults.Backstop"/> elapsed. Protects against a ship firing unattended because something failed silently elsewhere.</summary>
    Backstop,

    /// <summary>
    /// [2026-09-12] Added for the hold gesture (<c>LayoutSlot.Hold</c>,
    /// <c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension) - the
    /// commander lifted their finger off a held control
    /// (<see cref="LatchRegistry.Release"/>). Deliberately its own member
    /// rather than reusing <see cref="TappedAgain"/>: that one names a
    /// second TAP toggling a latch off, which is a different gesture from a
    /// pointerup ending a hold, even though both end in the same key going
    /// up.
    /// </summary>
    HoldReleased,
}
