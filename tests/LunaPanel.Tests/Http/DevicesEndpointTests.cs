using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="DevicesEndpoint"/>'s handler logic against a real
/// <see cref="DeviceRegistry"/> over a temp state file, same discipline as
/// <see cref="PairEndpointTests"/> - only this endpoint's own thin layer
/// (self-forget refusal, response shaping) is pinned here; the registry's own
/// pairing/forget/window behaviour is already covered in
/// <c>DeviceRegistryTests</c>.
/// </summary>
public class DevicesEndpointTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewStatePath([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "devices-endpoint", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "device-registry-state.json");
    }

    private static DeviceRegistry NewRegistry(string path) =>
        new(path, new CapturingDiagnosticLog(), TimeProvider.System);

    /// <summary>
    /// A marker store on its own scratch directory, beside the registry's
    /// state file - the real type, never a stand-in, so a forget here writes
    /// a real sidecar a real import list could read back.
    /// </summary>
    private static OrphanMarkerStore NewMarkerStore([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "devices-endpoint-layouts", testName, Guid.NewGuid().ToString("N"));
        return new OrphanMarkerStore(dir, new CapturingDiagnosticLog());
    }

    [Fact]
    public void BuildListResponse_IncludesEveryDevice_AndTheCallersOwnId_NeverAToken()
    {
        var registry = NewRegistry(NewStatePath());
        Assert.True(registry.TryPair(registry.CurrentCode, "Kitchen tablet", "tablet", out var token1, out var id1));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out var token2, out var id2));

        var response = DevicesEndpoint.BuildListResponse(registry.ListDevices(), id1!);

        Assert.Equal(id1, response.SelfDeviceId);
        Assert.Equal(2, response.Devices.Count);
        Assert.Contains(response.Devices, d => d.DeviceId == id1 && d.Name == "Kitchen tablet" && d.DeviceClass == "tablet");
        Assert.Contains(response.Devices, d => d.DeviceId == id2 && d.Name == "Phone" && d.DeviceClass == "phone");

        var serialized = System.Text.Json.JsonSerializer.Serialize(response);
        Assert.DoesNotContain(token1!, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(token2!, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void TryForget_AnotherDevice_Succeeds_AndItStopsAuthorizing()
    {
        var registry = NewRegistry(NewStatePath());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var selfToken, out var selfId));
        registry.OpenPairingWindow();
        Assert.True(registry.TryPair(registry.CurrentCode, "Phone", "phone", out var otherToken, out var otherId));

        var result = DevicesEndpoint.TryForget(registry, NewMarkerStore(), selfId!, new DevicesEndpoint.ForgetRequest(otherId), out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
        Assert.False(registry.TryAuthorize(otherToken, out _));
        Assert.True(registry.TryAuthorize(selfToken, out _));
    }

    /// <summary>
    /// The server-side half of "a device never forgets itself"
    /// (ref/docs/pairing-and-devices.md, decided 2026-09-07) - refused even
    /// though the caller is a real, currently-authorized device asking to
    /// forget its own, real, currently-registered id. Hiding the button in
    /// the client is not enough on its own; this is the enforcement that
    /// makes it actually impossible via a direct request too.
    /// </summary>
    [Fact]
    public void TryForget_OwnDeviceId_IsRefused_AndTheDeviceStaysRegistered()
    {
        var registry = NewRegistry(NewStatePath());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out var selfToken, out var selfId));

        var result = DevicesEndpoint.TryForget(registry, NewMarkerStore(), selfId!, new DevicesEndpoint.ForgetRequest(selfId), out var error);

        Assert.False(result);
        Assert.NotEqual(string.Empty, error);
        Assert.True(registry.TryAuthorize(selfToken, out var stillAuthorizedId));
        Assert.Equal(selfId, stillAuthorizedId);
    }

    [Fact]
    public void TryForget_UnknownDeviceId_IsRefused()
    {
        var registry = NewRegistry(NewStatePath());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out var selfId));

        var result = DevicesEndpoint.TryForget(registry, NewMarkerStore(), selfId!, new DevicesEndpoint.ForgetRequest("NOTAREALDEVICE0"), out var error);

        Assert.False(result);
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void TryForget_MissingDeviceId_IsRefused()
    {
        var registry = NewRegistry(NewStatePath());
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out var selfId));

        var result = DevicesEndpoint.TryForget(registry, NewMarkerStore(), selfId!, new DevicesEndpoint.ForgetRequest(null), out var error);

        Assert.False(result);
        Assert.NotEqual(string.Empty, error);
    }

    [Fact]
    public void OpenPairing_OpensTheWindow_AndReturnsTheFreshCode()
    {
        var path = NewStatePath();
        var registry = NewRegistry(path);
        Assert.True(registry.TryPair(registry.CurrentCode, "Tablet", "tablet", out _, out _));

        var reloaded = NewRegistry(path);
        Assert.False(reloaded.IsPairingWindowOpen);

        var response = DevicesEndpoint.OpenPairing(reloaded);

        Assert.True(reloaded.IsPairingWindowOpen);
        Assert.Equal(reloaded.CurrentCode, response.Code);
        Assert.True(reloaded.TryPair(response.Code, "Phone", "phone", out var token, out var deviceId));
        Assert.NotNull(token);
        Assert.NotNull(deviceId);
    }
}
