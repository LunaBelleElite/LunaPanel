using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Pairing;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/devices</c>, <c>POST /api/devices/forget</c> and
/// <c>POST /api/devices/open-pairing</c>'s handler logic, kept free of any
/// ASP.NET type - same discipline as <see cref="PairEndpoint"/>/
/// <see cref="ThemeEndpoint"/>. Backs the settings gear's Devices pane
/// (<c>ref/docs/pairing-and-devices.md</c>): list every paired device, forget
/// one, and open the pairing window from an already-paired device.
/// </summary>
public static class DevicesEndpoint
{
    public sealed record DeviceResponse(string DeviceId, string Name, string DeviceClass, DateTimeOffset PairedAt, DateTimeOffset LastSeenAt);

    /// <param name="SelfDeviceId">
    /// The id of the device making this request, so the client can hide the
    /// forget control on its own row - "a device never forgets itself" is
    /// enforced server-side by <see cref="TryForget"/> regardless, but the
    /// client still needs to know which row is its own to not offer a button
    /// that would always be refused.
    /// </param>
    public sealed record ListResponse(IReadOnlyList<DeviceResponse> Devices, string SelfDeviceId);

    public sealed record ForgetRequest(string? DeviceId);

    public sealed record OpenPairingResponse(string Code);

    public static ListResponse BuildListResponse(IReadOnlyList<DeviceSummary> devices, string selfDeviceId) =>
        new(
            devices.Select(d => new DeviceResponse(d.DeviceId, d.Name, d.DeviceClass, d.PairedAt, d.LastSeenAt)).ToArray(),
            selfDeviceId);

    /// <summary>
    /// Forgets <paramref name="request"/>'s device, refusing outright if it
    /// names the caller's own device - "a device never forgets itself"
    /// (decided 2026-09-07, <c>ref/docs/pairing-and-devices.md</c>) is
    /// enforced here, not merely by hiding the control in the client, since a
    /// direct request could otherwise still remove the device holding the
    /// session that issued it. This is what makes "at least one device
    /// always survives any sequence of forgets issued from a device" true by
    /// construction rather than by a separate "don't forget the last one"
    /// guard, which the spec explicitly says not to build.
    /// </summary>
    public static bool TryForget(
        DeviceRegistry registry,
        OrphanMarkerStore markers,
        string callerDeviceId,
        ForgetRequest? request,
        out string error)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DeviceId))
        {
            error = "deviceId is required.";
            return false;
        }

        if (string.Equals(request.DeviceId, callerDeviceId, StringComparison.Ordinal))
        {
            error = "A device cannot forget itself.";
            return false;
        }

        // Goes through DeviceForget, not registry.Forget directly, so the
        // orphan marker that keeps the layout left behind describable is
        // written on this path as well as the tray's.
        if (!DeviceForget.Forget(registry, markers, request.DeviceId))
        {
            error = "No such device.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Opens pairing (<see cref="DeviceRegistry.OpenPairingWindow"/>) and
    /// returns the fresh code to show on the calling device's own screen -
    /// the "add another device" route from <c>ref/docs/pairing-and-devices.md</c>.
    /// The caller (<see cref="ServerHostBuilder"/>) only reaches this behind
    /// device authentication - opening pairing for a new device is an
    /// escalation only an already-paired device may perform.
    /// </summary>
    public static OpenPairingResponse OpenPairing(DeviceRegistry registry)
    {
        registry.OpenPairingWindow();
        return new OpenPairingResponse(registry.CurrentCode);
    }
}
