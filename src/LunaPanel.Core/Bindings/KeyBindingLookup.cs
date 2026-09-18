using LunaPanel.Core.Input;

namespace LunaPanel.Core.Bindings;

/// <summary>
/// Reverse lookup for the "press a key" macro step (<c>ref/docs/macros.md</c>):
/// given a physical key a commander picked, is any Elite control already
/// bound to exactly that key? Offered so the builder can suggest "use that
/// control instead" - a real <c>press</c> step, resolved by action name,
/// heals itself on a later rebind, which a raw <c>pressKey</c> step
/// deliberately never does (see <see cref="LunaPanel.Core.Macros.PressKeyStep"/>'s
/// own remarks).
///
/// <b>Scope decision, 2026-09-12:</b> a match requires BOTH sides to carry no
/// modifier - the commander's own pick is always a bare key (this feature's
/// first version offers no chord-building UI at all), and a bound control is
/// only reported as equivalent when it, too, resolves with zero modifiers.
/// A control bound to <c>Ctrl+F5</c> is not "the same key" as a bare
/// <c>F5</c> press: substituting that action name into a <c>press</c> step
/// would send a completely different chord than the raw key the commander
/// actually picked. Requiring both sides bare is what keeps an offered match
/// genuinely equivalent, never merely close.
/// </summary>
public static class KeyBindingLookup
{
    /// <summary>
    /// The name of the first element in <paramref name="file"/> whose
    /// resolved chord is exactly <paramref name="key"/> with no modifiers, or
    /// <see langword="null"/> when none matches. Goes through the same
    /// <see cref="BindResolver.Resolve(BindingElement)"/> call every other
    /// bound-state check in this project uses, so a reported match is one
    /// <see cref="LunaPanel.Core.Macros.MacroRunner"/> would genuinely press
    /// today for that action - not a second opinion computed here.
    /// </summary>
    public static string? FindActionBoundTo(ScancodeInfo key, BindingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        foreach (var element in file.Elements)
        {
            var resolution = BindResolver.Resolve(element);
            if (resolution.IsBound &&
                resolution.Chord!.ModifierKeys.Count == 0 &&
                resolution.Chord.MainKey == key)
            {
                return element.Name;
            }
        }

        return null;
    }
}
