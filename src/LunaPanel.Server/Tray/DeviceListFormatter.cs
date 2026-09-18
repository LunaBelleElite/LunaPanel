using System.Globalization;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Server.Tray;

/// <summary>
/// Formats <see cref="DeviceRegistry.ListDevices"/>'s output for the tray's
/// "Devices" window (<c>ref/docs/pairing-and-devices.md</c>'s "What this
/// means for the tray"): id, name, class, paired-at, last-seen - and
/// <b>never the token</b>, which <see cref="DeviceSummary"/> already omits
/// by construction, so there is nothing here that could leak it.
/// </summary>
public static class DeviceListFormatter
{
    /// <summary>
    /// A fixed, sortable, unambiguous format - deliberately UTC rather than
    /// the tray machine's local time, so a formatted row is a pure function
    /// of the timestamp alone and this class needs no machine-dependent
    /// timezone input to be tested.
    /// </summary>
    private const string TimestampFormat = "yyyy-MM-dd HH:mm 'UTC'";

    public sealed record DeviceRow(
        string DeviceId,
        string Name,
        string DeviceClass,
        string PairedAtDisplay,
        string LastSeenDisplay);

    public static IReadOnlyList<DeviceRow> BuildRows(IReadOnlyList<DeviceSummary> devices) =>
        devices
            .Select(d => new DeviceRow(
                d.DeviceId,
                d.Name,
                d.DeviceClass,
                FormatTimestamp(d.PairedAt),
                FormatTimestamp(d.LastSeenAt)))
            .ToArray();

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
}
