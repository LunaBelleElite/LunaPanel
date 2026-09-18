using LunaPanel.Core.Bindings;
using LunaPanel.Core.Macros;

namespace LunaPanel.Server.Input;

/// <summary>
/// The single-tap counterpart to <c>MacroRunner.PressChordOnceAsync</c> -
/// modifiers down in file order, a settle gap (only when the chord has
/// modifiers), main key down, held, main key up, modifiers up in reverse.
/// This is exactly the choreography <c>ref/docs/injection.md</c> describes
/// as <see cref="Win32KeyInjector"/>'s "future single-tap caller" (<c>POST
/// /api/press</c>).
///
/// Deliberately does not run through <c>MacroRunner</c>: that type has no
/// way to observe a refusal - <c>IKeyInjector.KeyDown</c>/<c>KeyUp</c> are
/// both <see langword="void"/> - so it would report a press step as
/// <c>Succeeded</c> even when <see cref="InjectionGuard"/> refused it and
/// nothing was actually sent. That silent-success failure mode is precisely
/// what this whole feature exists to avoid, so this type calls
/// <see cref="Win32KeyInjector.KeyDownWithOutcome"/> for the main key
/// instead, and returns that verdict to the caller.
///
/// One deliberate difference from <c>MacroRunner.PressChordOnceAsync</c>: the
/// hold delay is skipped when the main key was refused, since nothing was
/// sent to hold - <c>MacroRunner</c> holds unconditionally because it has no
/// way to know the difference. The main key's release and every modifier's
/// release are still sent unconditionally either way, matching
/// <c>MacroRunner</c>'s own accepted "harmless noise" tolerance for a
/// release with no matching press (see <c>ref/docs/injection.md</c>'s "Key-up
/// bypasses the foreground guard").
/// </summary>
public static class ChordPresser
{
    public static async Task<InjectionAttemptResult> PressAsync(
        Win32KeyInjector injector,
        ResolvedChord chord,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(injector);
        ArgumentNullException.ThrowIfNull(chord);
        ArgumentNullException.ThrowIfNull(clock);

        foreach (var modifier in chord.ModifierKeys)
        {
            injector.KeyDown(modifier);
        }

        if (chord.ModifierKeys.Count > 0)
        {
            await DelayAsync(MacroTimingDefaults.ModifierSettleGap, clock, cancellationToken).ConfigureAwait(false);
        }

        var result = injector.KeyDownWithOutcome(chord.MainKey);

        if (result.Outcome == InjectionOutcome.Sent)
        {
            await DelayAsync(MacroTimingDefaults.DefaultHoldDuration, clock, cancellationToken).ConfigureAwait(false);
        }

        injector.KeyUp(chord.MainKey);

        for (var i = chord.ModifierKeys.Count - 1; i >= 0; i--)
        {
            injector.KeyUp(chord.ModifierKeys[i]);
        }

        return result;
    }

    private static Task DelayAsync(TimeSpan delay, TimeProvider clock, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, clock, cancellationToken);
}
