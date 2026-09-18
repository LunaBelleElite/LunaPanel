namespace LunaPanel.Core.Bindings;

/// <summary>
/// Why <see cref="BindResolver"/> could not produce a keyboard chord for an
/// action - specific enough to show a player directly, since this is what
/// explains "why is my button dead" in a bug report.
/// </summary>
public enum UnboundReason
{
    /// <summary>No element with that action name exists in the parsed file at all.</summary>
    NotPresent,

    /// <summary>Both Primary and Secondary are <c>{NoDevice}</c> (or absent) - nothing is bound to anything.</summary>
    NoDevice,

    /// <summary>A real binding exists, but only on a non-keyboard device (mouse, joystick) - nothing we can synthesise.</summary>
    NonKeyboardOnly,

    /// <summary>A keyboard binding exists, but names a key (main or modifier) that <see cref="LunaPanel.Core.Input.Scancodes.TryGet"/> can't map.</summary>
    UnknownKey
}
