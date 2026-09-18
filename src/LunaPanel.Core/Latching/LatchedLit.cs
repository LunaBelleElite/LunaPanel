using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Core.Latching;

/// <summary>
/// How a latched button looks: <see cref="SlotLitLevel.Full"/>, the existing
/// "full beam" level (<c>ref/docs/lit-state.md</c>).
///
/// <b>Reusing the lit vocabulary rather than inventing a fourth one is the
/// point, not a shortcut.</b> A held key that is invisible on the panel is
/// the same failure as one that cannot be released - the commander cannot
/// act on what they cannot see - and the lit levels already mean exactly
/// "how engaged is this control", with the commander's own rendering already
/// decided ("off = not lit, regular = partial brightness on the edges, full
/// beam = high glowing edges"). A latched key is a control that is engaged
/// as hard as a control can be, so it is Full. No new colour role, no fourth
/// visual language, and nothing new for the client to learn.
///
/// <b>One function, called by both endpoints that project a lit level.</b>
/// <c>GET /api/panel</c> paints the first frame and <c>GET /api/panel/live</c>
/// keeps it current; a latch that showed on one and not the other would
/// light up and then go dark on the next push, or vice versa. Spelling the
/// override in both endpoints separately is how those two would eventually
/// disagree.
/// </summary>
public static class LatchedLit
{
    /// <summary>
    /// Raises to <see cref="SlotLitLevel.Full"/> every level whose slot names
    /// an action currently in <paramref name="latchedActions"/> and declares
    /// either <see cref="LayoutSlot.Latch"/> or, since 2026-09-12,
    /// <see cref="LayoutSlot.Hold"/> - a currently-held hold-slot lives in
    /// the same held-actions set a toggled latch does
    /// (<see cref="Latching.LatchRegistry.LatchedActions"/> reports both
    /// alike, keyed only by device+action, with no notion of which gesture
    /// put an entry there). Everything else is returned exactly as the
    /// resolver computed it - in particular a latch or hold slot that is NOT
    /// currently held keeps whatever its own curated <c>lit</c> condition
    /// says, so a latchable landing-gear button still reads as "gear is
    /// down" while nothing is latched.
    /// </summary>
    public static IReadOnlyList<SlotLitState> Apply(
        IReadOnlyList<SlotLitState> levels,
        LayoutPage page,
        IReadOnlySet<string> latchedActions)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(latchedActions);

        if (latchedActions.Count == 0)
        {
            return levels;
        }

        var latchedIndices = page.Slots
            .Where(s => (s.Latch || s.Hold) && s.Action is not null && latchedActions.Contains(s.Action))
            .Select(s => s.Index)
            .ToHashSet();

        if (latchedIndices.Count == 0)
        {
            return levels;
        }

        return levels
            .Select(l => latchedIndices.Contains(l.Index) ? l with { Level = SlotLitLevel.Full } : l)
            .ToList();
    }
}
