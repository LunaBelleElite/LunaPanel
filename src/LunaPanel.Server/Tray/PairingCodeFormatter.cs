namespace LunaPanel.Server.Tray;

/// <summary>
/// Groups <see cref="LunaPanel.Core.Pairing.DeviceRegistry.CurrentCode"/>'s
/// raw 6-digit string into "123 456" for the tray's "Add a device" window -
/// the whole reason that window exists is to be the one obviously-correct
/// place to read this code from (<c>ref/docs/pairing-and-devices.md</c>), so
/// it is grouped the way a person reads a code aloud rather than shown as one
/// unbroken run of digits.
/// </summary>
public static class PairingCodeFormatter
{
    /// <summary>
    /// Returns <paramref name="code"/> unchanged if it is not exactly 6
    /// ASCII digits - <see cref="LunaPanel.Core.Pairing.DeviceRegistry"/>
    /// never produces anything else, but this display helper degrades to the
    /// raw value rather than throwing or guessing at a different grouping.
    /// </summary>
    public static string GroupForDisplay(string code) =>
        code.Length == 6 && code.All(char.IsAsciiDigit)
            ? $"{code[..3]} {code[3..]}"
            : code;
}
