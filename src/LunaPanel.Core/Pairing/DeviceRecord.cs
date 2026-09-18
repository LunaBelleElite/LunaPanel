namespace LunaPanel.Core.Pairing;

/// <summary>
/// One paired device, as held in memory and round-tripped through
/// <see cref="DeviceRegistryPersistedState"/>. <see cref="TokenHex"/> is that
/// device's own long-lived secret, hex-encoded - never logged, never shown
/// outside the token exchange itself. <see cref="DeviceId"/> is the first 8
/// bytes of SHA-256 over the raw token bytes, hex-encoded: derived from the
/// secret, safe to log, safe to use as a filename, and not reversible back to
/// the token. <see cref="Name"/> and <see cref="DeviceClass"/> are supplied by
/// the caller at pair time (e.g. "Kitchen tablet", "tablet"). <see cref="PairedAt"/>
/// and <see cref="LastSeenAt"/> are both stamped from <see cref="TimeProvider"/>,
/// never discovered by this type.
/// </summary>
internal sealed record DeviceRecord(
    string DeviceId,
    string TokenHex,
    string Name,
    string DeviceClass,
    DateTimeOffset PairedAt,
    DateTimeOffset LastSeenAt);
