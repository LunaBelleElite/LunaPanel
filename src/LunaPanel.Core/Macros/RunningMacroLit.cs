using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Core.Macros;

/// <summary>
/// How a macro that is running right now looks: <see cref="SlotLitLevel.Full"/>,
/// the same "full beam" level a latched key already uses
/// (<c>ref/docs/lit-state.md</c>).
///
/// <b>The commander asked for this in the same breath as the latch glow</b>
/// ("do that for any macro as long as it's running as well"), and it reuses
/// the same vocabulary for the same reason
/// <see cref="Latching.LatchedLit"/> does: a button doing something the
/// commander cannot see is the failure, and the lit levels already mean
/// "how engaged is this control". A macro that is mid-run is as engaged as a
/// control gets. No fourth visual state, no new colour role, nothing new for
/// the client to learn.
///
/// <b>Machine-wide, not per device - and that is the honest scope here,
/// unlike a latch.</b> <see cref="MacroRunner"/> holds one set of running
/// macro ids for the whole PC and knows nothing about devices, so every
/// paired panel showing the same macro sees it lit. That is not a shortcut:
/// pressing that button on a SECOND device stops the run (a second press of
/// a running macro id cancels it, wherever it comes from), so a lit button
/// on another panel is offering exactly the action it looks like it is
/// offering. A latch is the opposite case - it is keyed by (device, action)
/// and a second device's tap would latch its own key rather than release
/// anyone else's - which is why <see cref="Latching.LatchedLit"/> is handed
/// one device's latches and this is not.
/// </summary>
public static class RunningMacroLit
{
    /// <summary>
    /// Raises to <see cref="SlotLitLevel.Full"/> every level whose slot names
    /// a macro id in <paramref name="runningMacroIds"/>. Everything else is
    /// returned exactly as it arrived - and when nothing is running (the
    /// overwhelmingly common case) the very same instance is, allocating
    /// nothing.
    ///
    /// A slot naming a macro is never lit by
    /// <see cref="Layouts.LitStateResolver"/> itself (a macro has no curated
    /// <c>lit</c> condition and no single control to read), so there is no
    /// level here for this to fight with - unlike a latch, which deliberately
    /// leaves a not-currently-held latchable slot showing its own curated
    /// state.
    /// </summary>
    public static IReadOnlyList<SlotLitState> Apply(
        IReadOnlyList<SlotLitState> levels,
        LayoutPage page,
        IReadOnlySet<string> runningMacroIds)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(runningMacroIds);

        if (runningMacroIds.Count == 0)
        {
            return levels;
        }

        var runningIndices = page.Slots
            .Where(s => s.Macro is not null && runningMacroIds.Contains(s.Macro))
            .Select(s => s.Index)
            .ToHashSet();

        if (runningIndices.Count == 0)
        {
            return levels;
        }

        return levels
            .Select(l => runningIndices.Contains(l.Index) ? l with { Level = SlotLitLevel.Full } : l)
            .ToList();
    }
}
