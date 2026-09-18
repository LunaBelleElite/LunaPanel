using LunaPanel.Core.Bindings;
using LunaPanel.Core.GameState;

namespace LunaPanel.Server.Input;

/// <summary>
/// Wraps <see cref="ChordPresser.PressAsync"/> with the same left-panel tab
/// tracking a macro's plain <c>press</c> step already gets from
/// <c>LunaPanel.Core.Macros.MacroRunner</c> - both call the shared decision
/// in <see cref="PanelTabPressEffect"/>, so a single tap and a macro step
/// can never drift about what pressing <c>FocusLeftPanel</c>/
/// <c>CycleNextPanel</c>/<c>CyclePreviousPanel</c> means for
/// <see cref="PanelTabTracker"/> (<c>ref/docs/panel-tab-tracking.md</c>'s
/// "Fix 6"). This is the type <c>POST /api/press</c> calls instead of
/// <see cref="ChordPresser.PressAsync"/> directly.
///
/// Unlike <c>MacroRunner</c>, this type CAN observe whether the keystroke
/// actually reached Elite (<see cref="ChordPresser"/> returns an
/// <see cref="InjectionAttemptResult"/>), so it resolves the pending effect
/// with the real outcome rather than always assuming success:
/// <list type="bullet">
/// <item><description>
/// <c>FocusLeftPanel</c>'s toggle credit is armed BEFORE the keystroke is
/// sent (same timing <c>MacroRunner</c> already uses, required to win the
/// race against <c>Status.json</c>'s asynchronous confirming <c>GuiFocus</c>
/// edge), then revoked if the press turns out to have been refused - the
/// keystroke never reached Elite, so no edge is ever coming for it, and
/// leaving the credit to merely expire would risk it wrongly absorbing a
/// genuine hand-driven edge in the meantime.
/// </description></item>
/// <item><description>
/// <c>CycleNextPanel</c>/<c>CyclePreviousPanel</c> are the mirror case:
/// nothing ever confirms them (LC3 - nothing Elite writes reveals the active
/// tab), so they are only applied to the tracker AFTER a confirmed
/// <see cref="InjectionOutcome.Sent"/> - applying one speculatively and
/// then discovering the press was refused would leave the tracker
/// confidently wrong with no way to ever correct it.
/// </description></item>
/// </list>
/// </summary>
public static class PlainPresser
{
    public static async Task<InjectionAttemptResult> PressAsync(
        Win32KeyInjector injector,
        ResolvedChord chord,
        string action,
        PanelTabTracker tabTracker,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(chord);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(tabTracker);
        ArgumentNullException.ThrowIfNull(clock);

        var pending = PanelTabPressEffect.Arm(tabTracker, action);
        var result = await ChordPresser.PressAsync(injector, chord, clock, cancellationToken).ConfigureAwait(false);
        pending.Resolve(tabTracker, sent: result.Outcome == InjectionOutcome.Sent);

        return result;
    }
}
