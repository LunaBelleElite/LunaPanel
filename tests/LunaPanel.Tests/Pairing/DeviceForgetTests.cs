using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Pairing;

namespace LunaPanel.Tests.Pairing;

/// <summary>
/// Drives <see cref="DeviceForget"/> against a real
/// <see cref="DeviceRegistry"/> and a real <see cref="OrphanMarkerStore"/>,
/// both over temp directories - the marker written here is a real sidecar
/// file that a real import list could read back.
///
/// This is the shared half of the two forget paths
/// (<c>POST /api/devices/forget</c> and the tray's Devices window); that
/// both of them actually go through it is pinned separately by
/// <see cref="ForgetPathSourceGuardTests"/>, because a caller that quietly
/// went back to <c>registry.Forget</c> would leave every test here green.
/// </summary>
public class DeviceForgetTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private sealed record Harness(DeviceRegistry Registry, OrphanMarkerStore Markers, string LayoutsDirectory);

    private static Harness NewHarness([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-temp", "device-forget", testName, Guid.NewGuid().ToString("N"));
        var layouts = Path.Combine(root, "layouts");
        Directory.CreateDirectory(root);
        var log = new CapturingDiagnosticLog();
        return new Harness(
            new DeviceRegistry(Path.Combine(root, "device-registry-state.json"), log, TimeProvider.System),
            new OrphanMarkerStore(layouts, log),
            layouts);
    }

    [Fact]
    public void Forget_LeavesAMarkerCarryingTheNameClassAndLastSeen()
    {
        var harness = NewHarness();
        Assert.True(harness.Registry.TryPair(harness.Registry.CurrentCode, "Kitchen tablet", "tablet", out _, out var deviceId));
        var lastSeen = Assert.Single(harness.Registry.ListDevices()).LastSeenAt;

        Assert.True(DeviceForget.Forget(harness.Registry, harness.Markers, deviceId!));

        var marker = Assert.Single(harness.Markers.List());
        Assert.Equal(deviceId, marker.DeviceId);
        Assert.Equal("Kitchen tablet", marker.Name);
        Assert.Equal("tablet", marker.DeviceClass);
        Assert.Equal(lastSeen, marker.LastSeenAt);
    }

    /// <summary>
    /// The end-to-end "never the token" pin. Unlike
    /// <c>OrphanMarkerStoreTests.Write_MarkerFileNamesNoTokenBearingField</c>,
    /// a real 64-hex-character token is actually in play here - minted by a
    /// real pair - so the needle is one a single edit could genuinely make
    /// appear, and every file left in the layouts directory is checked, not
    /// only the marker.
    /// </summary>
    [Fact]
    public void Forget_WritesAnOrphanMarker_AndNeverTheToken()
    {
        var harness = NewHarness();
        Assert.True(harness.Registry.TryPair(harness.Registry.CurrentCode, "Phone", "phone", out var token, out var deviceId));
        Assert.Equal(64, token!.Length);

        DeviceForget.Forget(harness.Registry, harness.Markers, deviceId!);

        var files = Directory.GetFiles(harness.LayoutsDirectory);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.DoesNotContain(token, File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Forget_AlsoActuallyRevokesTheDevice()
    {
        var harness = NewHarness();
        Assert.True(harness.Registry.TryPair(harness.Registry.CurrentCode, "Phone", "phone", out var token, out var deviceId));

        DeviceForget.Forget(harness.Registry, harness.Markers, deviceId!);

        Assert.False(harness.Registry.TryAuthorize(token, out _));
        Assert.Empty(harness.Registry.ListDevices());
    }

    /// <summary>
    /// An id that is not registered leaves nothing behind. A marker for a
    /// device that never existed would put a row on the import list pointing
    /// at no layout at all.
    /// </summary>
    [Fact]
    public void Forget_UnknownDeviceId_ReturnsFalse_AndWritesNoMarker()
    {
        var harness = NewHarness();
        Assert.True(harness.Registry.TryPair(harness.Registry.CurrentCode, "Phone", "phone", out _, out _));

        Assert.False(DeviceForget.Forget(harness.Registry, harness.Markers, "NOTAREALDEVICE0"));

        Assert.Empty(harness.Markers.List());
    }

    /// <summary>
    /// Two devices sharing a name is explicitly allowed, so forgetting one
    /// of them has to leave exactly one marker, for exactly the id that was
    /// forgotten - a name-keyed marker store would collapse the two.
    /// </summary>
    [Fact]
    public void Forget_OneOfTwoDevicesSharingAName_LeavesAMarkerForThatIdOnly()
    {
        var harness = NewHarness();
        Assert.True(harness.Registry.TryPair(harness.Registry.CurrentCode, "Phone", "phone", out _, out var firstId));
        harness.Registry.OpenPairingWindow();
        Assert.True(harness.Registry.TryPair(harness.Registry.CurrentCode, "Phone", "phone", out var secondToken, out var secondId));

        DeviceForget.Forget(harness.Registry, harness.Markers, firstId!);

        var marker = Assert.Single(harness.Markers.List());
        Assert.Equal(firstId, marker.DeviceId);
        Assert.NotEqual(secondId, marker.DeviceId);
        Assert.True(harness.Registry.TryAuthorize(secondToken, out _));
    }
}
