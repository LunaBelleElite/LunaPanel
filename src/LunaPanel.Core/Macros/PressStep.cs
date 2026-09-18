namespace LunaPanel.Core.Macros;

/// <summary>
/// Blind step: <c>{ "press": "Action", "repeat": n, "holdMs": m }</c>.
/// Resolves <see cref="Action"/> through the same bound-action lookup slot
/// status uses (<c>LunaPanel.Core.Bindings.BindResolver.Resolve</c>), then
/// injects the resolved chord <see cref="Repeat"/> times, each press held
/// for <see cref="Hold"/> (or <see cref="MacroTimingDefaults.DefaultHoldDuration"/>
/// when not overridden) with <see cref="MacroTimingDefaults.InterPressGap"/>
/// between consecutive presses.
/// </summary>
/// <param name="Action">The Frontier action name to resolve and press.</param>
/// <param name="Repeat">How many times to press the resolved chord. Always positive - validated at load.</param>
/// <param name="Hold">Per-step override for how long to hold the chord down, or <see langword="null"/> to use the default.</param>
public sealed record PressStep(string Action, int Repeat, TimeSpan? Hold) : MacroStep;
