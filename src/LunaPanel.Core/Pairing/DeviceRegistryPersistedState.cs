namespace LunaPanel.Core.Pairing;

/// <summary>
/// The on-disk shape of <see cref="DeviceRegistry"/>'s persisted state,
/// round-tripped through JSON at an injected file path. This type never reads
/// or writes the file itself - <see cref="DeviceRegistry"/> owns that.
/// <see cref="Code"/> is the current shared 6-digit pairing code (one
/// household, one code shown on the PC console); <see cref="Devices"/> is
/// every device that has ever successfully paired.
///
/// This is the successor to the single-token shape a pre-registry build of
/// this project may still hold on disk (<c>{"TokenHex","Code","ConsecutiveFailures"}</c>,
/// no <c>Devices</c> array). <see cref="DeviceRegistry"/> recognizes that
/// older shape by the absence of a <c>Devices</c> property and migrates it
/// into a one-device registry rather than treating it as corrupt.
/// </summary>
internal sealed record DeviceRegistryPersistedState(string Code, int ConsecutiveFailures, List<DeviceRecord> Devices);
