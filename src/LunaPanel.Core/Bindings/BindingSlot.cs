namespace LunaPanel.Core.Bindings;

/// <summary>
/// One <c>&lt;Primary&gt;</c> or <c>&lt;Secondary&gt;</c> element: its own
/// device+key, plus any <c>&lt;Modifier&gt;</c> children nested inside it
/// (empty when there are none - most bindings have no modifier at all).
/// </summary>
/// <param name="Device">The raw <c>Device</c> attribute value.</param>
/// <param name="Key">The raw <c>Key</c> attribute value.</param>
/// <param name="Modifiers">Nested <c>&lt;Modifier&gt;</c> elements, in file order.</param>
public sealed record BindingSlot(string Device, string Key, IReadOnlyList<BoundKey> Modifiers);
