namespace LunaPanel.Core.Bindings;

/// <summary>
/// One bindable action as found in a player's <c>.binds</c> file - a direct
/// child of <c>&lt;Root&gt;</c> that has at least one of
/// <c>&lt;Primary&gt;</c>/<c>&lt;Secondary&gt;</c>. Either slot may be
/// <see langword="null"/> if the element didn't carry that child at all
/// (distinct from a slot that is present but names <c>{NoDevice}</c>).
/// </summary>
/// <param name="Name">The element's own tag name - Frontier's canonical action identifier, e.g. <c>UI_Up</c>.</param>
/// <param name="Primary">The <c>&lt;Primary&gt;</c> slot, or <see langword="null"/> if absent.</param>
/// <param name="Secondary">The <c>&lt;Secondary&gt;</c> slot, or <see langword="null"/> if absent.</param>
public sealed record BindingElement(string Name, BindingSlot? Primary, BindingSlot? Secondary);
