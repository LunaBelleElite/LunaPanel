namespace LunaPanel.Core.Pairing;

/// <summary>
/// One paired device, as handed back by <see cref="DeviceRegistry.ListDevices"/>
/// for a devices view. Deliberately omits <see cref="DeviceRecord.TokenHex"/> -
/// the token is never returned in any response, logged, or shown outside the
/// pairing exchange itself, per <c>ref/docs/pairing-and-devices.md</c>.
/// </summary>
public sealed record DeviceSummary(
    string DeviceId,
    string Name,
    string DeviceClass,
    DateTimeOffset PairedAt,
    DateTimeOffset LastSeenAt);
