namespace LunaPanel.Core.Macros;

/// <summary>
/// Raw-key step: <c>{ "pressKey": "Key_F5", "repeat": n, "holdMs": m }</c>.
///
/// Unlike <see cref="PressStep"/>, this names a PHYSICAL key
/// (<see cref="LunaPanel.Core.Input.Scancodes"/>'s own <c>Key_*</c> spelling)
/// rather than a Frontier action name, and is deliberately the opposite of
/// <see cref="MacroJson"/>'s "intent, never resolution" rule: this key is
/// injected exactly as chosen, forever, whether or not Elite currently binds
/// anything to it and regardless of any later rebind in the game. Built for
/// the commander who knows the physical key they want pressed and has
/// nothing (yet) bound to it - see
/// <see cref="LunaPanel.Core.Bindings.KeyBindingLookup"/>, which the builder
/// consults first to offer a real <see cref="PressStep"/> instead whenever
/// something already IS bound to that key.
///
/// <see cref="Key"/> is resolved directly through
/// <see cref="LunaPanel.Core.Input.Scancodes.TryGet"/> in <c>MacroRunner</c> -
/// no <c>BindResolver</c> involved, and no notion of "unbound": a raw key
/// press is never degraded (<c>MacroKnowledgeBuilder</c> never inspects this
/// step kind - there is nothing here that resolution could fail to find).
/// </summary>
/// <param name="Key">The physical key's <c>Key_*</c> name, exactly as <see cref="LunaPanel.Core.Input.Scancodes"/> spells it.</param>
/// <param name="Repeat">How many times to press the key. Always positive - validated at load.</param>
/// <param name="Hold">Per-step override for how long to hold the key down, or <see langword="null"/> to use the default.</param>
public sealed record PressKeyStep(string Key, int Repeat, TimeSpan? Hold) : MacroStep;
