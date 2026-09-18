using LunaPanel.Core.Pairing;
using LunaPanel.Server.Tray;

namespace LunaPanel.Tests.Tray;

/// <summary>
/// Pins <see cref="DeviceListFormatter"/>'s row shape for the tray's
/// "Devices" window - id, name, class, and both timestamps formatted as
/// fixed, UTC, unambiguous strings, and never the token (which
/// <see cref="DeviceSummary"/> has no field for in the first place, so this
/// is a shape guarantee rather than a redaction step).
/// </summary>
public class DeviceListFormatterTests
{
    [Fact]
    public void BuildRows_OneDevice_FormatsAllFieldsAndBothTimestampsAsUtc()
    {
        var pairedAt = new DateTimeOffset(2026, 9, 7, 14, 30, 0, TimeSpan.Zero);
        var lastSeenAt = new DateTimeOffset(2026, 9, 7, 15, 45, 0, TimeSpan.Zero);
        var devices = new[] { new DeviceSummary("abc12345", "Kitchen tablet", "tablet", pairedAt, lastSeenAt) };

        var rows = DeviceListFormatter.BuildRows(devices);

        var row = Assert.Single(rows);
        Assert.Equal("abc12345", row.DeviceId);
        Assert.Equal("Kitchen tablet", row.Name);
        Assert.Equal("tablet", row.DeviceClass);
        Assert.Equal("2026-09-07 14:30 UTC", row.PairedAtDisplay);
        Assert.Equal("2026-09-07 15:45 UTC", row.LastSeenDisplay);
    }

    [Fact]
    public void BuildRows_NonUtcOffset_IsConvertedToUtcBeforeFormatting()
    {
        // 2026-09-07 10:00 -04:00 is 2026-09-07 14:00 UTC - this is the
        // specific claim that distinguishes "formats UtcDateTime" from
        // "formats DateTime verbatim, ignoring the offset".
        var pairedAt = new DateTimeOffset(2026, 9, 7, 10, 0, 0, TimeSpan.FromHours(-4));
        var devices = new[] { new DeviceSummary("id1", "Phone", "phone", pairedAt, pairedAt) };

        var rows = DeviceListFormatter.BuildRows(devices);

        Assert.Equal("2026-09-07 14:00 UTC", rows[0].PairedAtDisplay);
    }

    [Fact]
    public void BuildRows_NoDevices_ReturnsEmpty()
    {
        var rows = DeviceListFormatter.BuildRows(Array.Empty<DeviceSummary>());

        Assert.Empty(rows);
    }

    [Fact]
    public void BuildRows_MultipleDevices_PreservesOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var devices = new[]
        {
            new DeviceSummary("first", "First device", "tablet", now, now),
            new DeviceSummary("second", "Second device", "phone", now, now),
        };

        var rows = DeviceListFormatter.BuildRows(devices);

        Assert.Equal(new[] { "first", "second" }, rows.Select(r => r.DeviceId));
    }
}
