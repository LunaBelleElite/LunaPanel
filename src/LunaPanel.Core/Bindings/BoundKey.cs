namespace LunaPanel.Core.Bindings;

/// <summary>
/// One device+key pair as written in a player's <c>.binds</c> file - used
/// both for a <see cref="BindingSlot"/>'s own binding and for each
/// <c>&lt;Modifier&gt;</c> nested inside it. <paramref name="Device"/> is
/// whatever the file wrote (<c>Keyboard</c>, <c>Mouse</c>,
/// <c>{NoDevice}</c>, or a joystick device name) - this type does not judge
/// it, only <see cref="BindResolver"/> does.
/// </summary>
/// <param name="Device">The raw <c>Device</c> attribute value.</param>
/// <param name="Key">The raw <c>Key</c> attribute value (Frontier's <c>Key_*</c> spelling, or empty).</param>
public sealed record BoundKey(string Device, string Key);
