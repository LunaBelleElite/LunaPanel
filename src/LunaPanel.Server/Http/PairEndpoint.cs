using System.Linq;
using LunaPanel.Core.Pairing;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>POST /api/pair</c>'s handler logic, kept free of any ASP.NET type so it
/// can be driven directly by a test without a real HTTP request. The token
/// this returns on success is minted fresh by <see cref="DeviceRegistry"/>
/// and is never itself put in the JSON response body - the caller
/// (<see cref="ServerHostBuilder"/>) is responsible for putting it only in an
/// HttpOnly cookie, never in a body a script could read.
/// </summary>
public static class PairEndpoint
{
    public sealed record Request(string? Code, string? DeviceName, string? DeviceClass);

    /// <param name="DeviceName">
    /// The name this device ended up with - typed by the commander, or
    /// assigned here (see <see cref="TryPair"/>). Reported back because when
    /// the server assigned it, the client has never seen it, and it is what
    /// the settings Devices pane will show for this device from now on.
    /// </param>
    /// <param name="Recovery">
    /// <see langword="null"/> when no layout on disk is unowned - a
    /// first-ever pairing is not interrupted by an empty chooser
    /// (<c>ref/docs/layout-import.md</c>). Otherwise either an automatic
    /// adoption to announce, or the list to choose from.
    /// </param>
    public sealed record SuccessResponse(
        bool Paired,
        string DeviceId,
        string DeviceName,
        LayoutImportEndpoint.RecoveryResponse? Recovery);

    public sealed record ErrorResponse(string Error);

    /// <summary>
    /// Pairs, having first decided what this device is called and what class
    /// it is - see <see cref="DeviceNaming"/> for both rules.
    ///
    /// <b>The name is settled here, on the server, not by the client.</b> A
    /// name the commander typed is used verbatim (trimmed, and stripped of
    /// the control characters and excess length that would make the two
    /// device lists unreadable); an empty one becomes the class label plus
    /// the next free ordinal for that class - "Phone", then "Phone 2". The
    /// ordinal cannot be computed anywhere else: <c>GET /api/devices</c> is
    /// behind device authentication precisely so that an unpaired client
    /// cannot enumerate the household, so a device that has not paired yet
    /// has no way to know how many phones are already registered. Exposing a
    /// "suggest me a name" route to unauthenticated callers would hand out
    /// exactly that count, which is why this is not done.
    ///
    /// <paramref name="deviceName"/> is reported back to the caller because
    /// layout recovery (<c>ref/docs/layout-import.md</c>) matches an orphan
    /// against the name this device just gave - and when the server assigned
    /// it, the client has never seen it.
    /// </summary>
    public static bool TryPair(
        DeviceRegistry registry,
        Request request,
        out string? token,
        out string? deviceId,
        out string deviceName)
    {
        var deviceClass = DeviceNaming.NormalizeClass(request.DeviceClass);
        var typedName = string.IsNullOrWhiteSpace(request.DeviceName)
            ? string.Empty
            : DeviceNaming.SanitizeTypedName(request.DeviceName!);

        deviceName = typedName.Length > 0
            ? typedName
            : DeviceNaming.NextDefaultName(deviceClass, registry.ListDevices());

        return registry.TryPair(NormalizeCode(request.Code), deviceName, deviceClass, out token, out deviceId);
    }

    /// <summary>
    /// Strips spaces and dashes from a submitted pairing code before it is
    /// compared.
    ///
    /// The tray's "Add a device" window shows the code grouped as "123 456"
    /// (<see cref="LunaPanel.Server.Tray.PairingCodeFormatter.GroupForDisplay"/>)
    /// and pre-selects it so it can be copied - which is the whole point of
    /// that window - while <see cref="DeviceRegistry.TryPair"/> compares
    /// against the raw six digits. So the one action the window invites,
    /// copy and paste, could never pair. Found live on 2026-09-08 after four
    /// consecutive failures in the log; the commander was doing exactly what
    /// the UI asked of them.
    ///
    /// Normalising happens HERE, at the HTTP boundary, rather than by
    /// loosening the registry's comparison: <c>TryPair</c> stays an exact
    /// constant-time match on a canonical value, and the leniency about how
    /// a human typed it stays a presentation concern. The code space is
    /// unchanged - separators carry no entropy - so this costs nothing an
    /// attacker could use.
    /// </summary>
    public static string? NormalizeCode(string? code) =>
        code is null
            ? null
            : new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
}
