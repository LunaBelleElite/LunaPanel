using System.Net;

namespace LunaPanel.Server.Http;

/// <summary>
/// "Did this request come from the machine LunaPanel is running on?" - the
/// one place that question is answered (<c>ref/docs/hosting.md</c>'s "The
/// host is a second listener, not a claim").
///
/// <b>The evidence is the connection, never anything the caller wrote.</b>
/// <see cref="ServerHostBuilder"/> binds Kestrel twice: once to the single
/// LAN address every paired device reaches (<see cref="LanAddressResolver"/>),
/// and once to <see cref="IPAddress.Loopback"/> on
/// <c>ServerHostOptions.HostAccessPort</c> - a socket nothing off this
/// machine can open at all, because an IP packet arriving at a physical
/// interface carrying a <c>127.0.0.0/8</c> source or destination is dropped
/// by the operating system before any listener sees it. A request that
/// arrived on <em>that</em> endpoint, from a loopback peer, therefore came
/// from a process on this machine, and that is the whole claim.
///
/// Nothing here reads a header. <c>X-Forwarded-For</c>, <c>Host</c>,
/// <c>Origin</c> and every other header a caller controls are irrelevant by
/// construction rather than by being filtered out - there is no code path
/// through which one could reach this decision. (ASP.NET Core only rewrites
/// <c>Connection.RemoteIpAddress</c> from <c>X-Forwarded-For</c> when
/// <c>UseForwardedHeaders</c> is added to the pipeline, and this host never
/// adds it. A test pins that spelling's absence, because adding it later
/// would silently turn a header into evidence.)
///
/// <b>What would defeat it</b>, stated so it is not mistaken for more than
/// it is: any process already running on this machine can open a loopback
/// socket, so "the host" means "something on this PC", never "the commander".
/// That is the accepted trade - anything with code execution here can read
/// the layouts, the pairing registry and the macro files directly anyway, so
/// a browser on the same box is not the weak link. A router forwarding an
/// external port to the LAN listener does not grant it (the listener is the
/// wrong one). Malware on the PC, or a browser the commander has been talked
/// into pointing at a loopback URL by another page, would - the latter
/// limited to what a cross-origin request can do without reading the
/// response.
/// </summary>
public static class HostRequest
{
    /// <summary>
    /// The device id every host request is authorised as. Deliberately not a
    /// registry id: <c>DeviceRegistry.ComputeDeviceId</c> mints 16 uppercase
    /// hex characters, so no paired device can ever be handed this one, and
    /// a test pins that. Its own per-device stores (layout, theme override,
    /// panel settings, latches) key off it exactly as a paired device's do,
    /// which is what makes the PC's panel its own arrangement rather than a
    /// borrowed copy of somebody's tablet.
    /// </summary>
    public const string HostDeviceId = "host";

    /// <summary>
    /// <see langword="true"/> only when the connection both arrived on the
    /// loopback-only host listener (<paramref name="localPort"/> matching
    /// <paramref name="hostAccessPort"/>, on a loopback local address) and
    /// has a loopback peer. Either half alone is close to sufficient; both
    /// are required so a defect in one does not open the door.
    ///
    /// <paramref name="hostAccessPort"/> of zero or less means no host
    /// listener was bound at all, and every request is then an ordinary
    /// device request - the deny direction, which is what an unconfigured
    /// <c>ServerHostOptions</c> gets.
    /// </summary>
    public static bool IsFromHost(IPAddress? remoteIp, IPAddress? localIp, int localPort, int hostAccessPort)
    {
        if (hostAccessPort <= 0 || localPort != hostAccessPort)
        {
            return false;
        }

        return IsLoopback(remoteIp) && IsLoopback(localIp);
    }

    /// <summary>
    /// The URL the tray's "Build a macro" item opens
    /// (<c>ref/docs/hosting.md</c>), and the only address that grants host
    /// access. <see langword="null"/> when no host listener was bound, so a
    /// caller has to decide what to do about that rather than being handed a
    /// URL that answers nothing.
    ///
    /// A literal <c>127.0.0.1</c>, never <c>localhost</c>: the name can
    /// resolve to <c>::1</c> first, and only IPv4 loopback is bound.
    /// </summary>
    public static string? LoopbackUrl(int hostAccessPort, string? fragment = null) =>
        hostAccessPort <= 0 ? null : $"http://127.0.0.1:{hostAccessPort}/{fragment}";

    /// <summary>
    /// The host listener's port: <c>LUNAPANEL_HOST_PORT</c> when it is set to
    /// a usable number, otherwise the device port plus one. Pure, so the one
    /// real call site (<see cref="RealServerEnvironment"/>, which this suite
    /// deliberately never constructs) carries no arithmetic of its own.
    ///
    /// A port already in use fails the same way an occupied device port
    /// already does - Kestrel refuses to start and says so - which is an
    /// existing, loud failure mode rather than a new silent one, and the
    /// environment variable is the way out of it.
    /// </summary>
    public static int ResolveAccessPort(string? configuredOverride, int devicePort) =>
        int.TryParse(configuredOverride, out var configured) && configured > 0
            ? configured
            : devicePort + 1;

    /// <summary>
    /// Treats an IPv4-mapped IPv6 address (<c>::ffff:127.0.0.1</c>) as the
    /// IPv4 address it carries. Kestrel reports plain IPv4 for the endpoints
    /// this host binds, so this is a guard against a future dual-mode bind
    /// rather than an observed case - but it is the guard whose absence
    /// would silently answer "not the host" for every request.
    /// </summary>
    private static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }
}
