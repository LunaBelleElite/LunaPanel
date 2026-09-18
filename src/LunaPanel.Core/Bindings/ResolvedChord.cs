using LunaPanel.Core.Input;

namespace LunaPanel.Core.Bindings;

/// <summary>
/// A keyboard chord resolved by <see cref="BindResolver"/>: everything
/// needed to inject it, plus a display string for the UI.
/// <see cref="DisplayText"/> is display-only and must never be fed back
/// into injection - it collapses left/right modifier variants
/// (<c>Key_LeftShift</c>/<c>Key_RightShift</c> both read "Shift") in a way
/// that would lose exactly the distinction <c>SendInput</c> needs.
/// </summary>
/// <param name="MainKey">The scan code for the chord's main key.</param>
/// <param name="ModifierKeys">Scan codes for each modifier, in file order.</param>
/// <param name="DisplayText">A human-readable chord, e.g. <c>"Shift+W"</c>, <c>"Ctrl+Alt+T"</c>, <c>"W"</c>.</param>
public sealed record ResolvedChord(ScancodeInfo MainKey, IReadOnlyList<ScancodeInfo> ModifierKeys, string DisplayText);
