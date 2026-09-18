using LunaPanel.Core.Pairing;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Pins <see cref="DeviceAuthMiddlewareExtensions.IsExemptPath"/> directly,
/// with no real HTTP pipeline needed - the exact match discipline (not a
/// prefix check) is the property most likely to be "simplified" into a
/// security hole later, so it gets its own near-miss cases.
/// </summary>
public class DeviceAuthMiddlewareExtensionsTests
{
    [Theory]
    [InlineData(ApiPaths.Health)]
    [InlineData(ApiPaths.Pair)]
    [InlineData(ApiPaths.App)]
    public void IsExemptPath_HealthAndPairAndApp_ReturnsTrue(string path)
    {
        Assert.True(DeviceAuthMiddlewareExtensions.IsExemptPath(path));
    }

    [Fact]
    public void IsExemptPath_IsCaseInsensitive()
    {
        Assert.True(DeviceAuthMiddlewareExtensions.IsExemptPath("/API/HEALTH"));
    }

    [Fact]
    public void IsExemptPath_Diagnostics_ReturnsFalse()
    {
        Assert.False(DeviceAuthMiddlewareExtensions.IsExemptPath(ApiPaths.Diagnostics));
    }

    /// <summary>
    /// [2026-09-17] O14, closed: the four `/probe*` calibration routes used to
    /// be exempt (requiring pairing before a device could be measured was
    /// judged circular) but that left an unauthenticated, uncovered surface
    /// in every build. They now require the same device cookie as everything
    /// else - this pins the removal directly, so a future "it's just a
    /// throwaway calibration page" instinct doesn't quietly re-add them.
    /// </summary>
    [Theory]
    [InlineData(ApiPaths.Probe)]
    [InlineData(ApiPaths.ProbeEstimate)]
    [InlineData(ApiPaths.ProbeLabels)]
    [InlineData(ApiPaths.ProbeServiceWorker)]
    public void IsExemptPath_ProbeRoutes_ReturnFalse(string path)
    {
        Assert.False(DeviceAuthMiddlewareExtensions.IsExemptPath(path));
    }

    [Theory]
    [InlineData("/api/pairing")]
    [InlineData("/api/health/")]
    [InlineData("/api/healthcheck")]
    [InlineData("/api")]
    [InlineData("/index.html")]
    [InlineData(null)]
    public void IsExemptPath_NearMissesAndNull_ReturnFalse(string? path)
    {
        Assert.False(DeviceAuthMiddlewareExtensions.IsExemptPath(path));
    }

    // ---------------------------------------------------------------------
    // ResolveHostDeviceId: editing a specific paired device's real layout
    // live, from the PC. Pure, so every branch is pinned directly here with
    // no real HTTP pipeline needed - same discipline as IsExemptPath above.
    // The genuinely security-critical half of this feature (a cookie-
    // authenticated request can never reach this method at all) is proved
    // through a real Kestrel round trip in ServerHostBuilderTests instead,
    // since that is a claim about UseDeviceAuthentication's wiring, not
    // about this pure function.
    // ---------------------------------------------------------------------

    private static readonly DeviceSummary Tablet = new("AAAAAAAAAAAAAAAA", "Kitchen tablet", "tablet", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static readonly DeviceSummary Phone = new("BBBBBBBBBBBBBBBB", "Phone", "phone", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveHostDeviceId_NoAsDeviceParam_ResolvesToTheHostItself(string? asDeviceParam)
    {
        var resolved = DeviceAuthMiddlewareExtensions.ResolveHostDeviceId(asDeviceParam, new[] { Tablet, Phone });

        Assert.Equal(HostRequest.HostDeviceId, resolved);
    }

    [Fact]
    public void ResolveHostDeviceId_AsDeviceNamesARealPairedDevice_ResolvesToThatDevice()
    {
        var resolved = DeviceAuthMiddlewareExtensions.ResolveHostDeviceId(Tablet.DeviceId, new[] { Tablet, Phone });

        Assert.Equal(Tablet.DeviceId, resolved);
    }

    /// <summary>
    /// The safety-critical refusal: a typo, an orphaned id, or plain
    /// garbage must never silently resolve to the host's own identity - that
    /// would be a commander believing they are editing a tablet while
    /// actually editing their own PC's private layout, which is exactly the
    /// failure this task's brief calls out by name.
    /// </summary>
    [Fact]
    public void ResolveHostDeviceId_AsDeviceNamesNoLiveDevice_ReturnsNull_NeverFallsBackToHost()
    {
        var resolved = DeviceAuthMiddlewareExtensions.ResolveHostDeviceId("NOT-A-REAL-DEVICE", new[] { Tablet, Phone });

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveHostDeviceId_NoLiveDevicesAtAll_AsDeviceStillRefusedRatherThanFallingBackToHost()
    {
        var resolved = DeviceAuthMiddlewareExtensions.ResolveHostDeviceId(Tablet.DeviceId, Array.Empty<DeviceSummary>());

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveHostDeviceId_AsDeviceComparisonIsOrdinal_NotCaseInsensitive()
    {
        // Device ids are hex, minted upper-case (DeviceRegistry.ComputeDeviceId).
        // A lower-cased match must not be treated as the same device - this is
        // the same "no case-folding on an identity" discipline the token
        // comparison itself already applies, and the mutation that would
        // silently switch this to OrdinalIgnoreCase would still pass every
        // other test in this class.
        var resolved = DeviceAuthMiddlewareExtensions.ResolveHostDeviceId(Tablet.DeviceId.ToLowerInvariant(), new[] { Tablet });

        Assert.Null(resolved);
    }
}
