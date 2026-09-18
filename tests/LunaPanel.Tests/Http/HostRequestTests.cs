using System.Net;
using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// The one question <see cref="HostRequest"/> answers - "did this request
/// come from the machine LunaPanel is running on?" - driven directly, over
/// the values Kestrel reports on a connection. The end-to-end half (a real
/// two-listener host, a real refusal, a real spoofed header) is in
/// <c>ServerHostBuilderTests</c>; this class is where the shape of the
/// decision itself is pinned.
///
/// <b>Every case here is a real connection shape, not a mood.</b> The
/// arguments are exactly what <c>HttpContext.Connection</c> carries, so a
/// test asserting "a header does not grant it" cannot even be written here:
/// there is no parameter for one. That is the point of the signature.
/// </summary>
public class HostRequestTests
{
    private const int HostPort = 51824;
    private const int DevicePort = 51823;

    /// <summary>
    /// The real production shape: a browser on the PC opening the loopback
    /// URL the tray hands it.
    /// </summary>
    [Fact]
    public void LoopbackPeerOnTheHostListener_IsTheHost()
    {
        Assert.True(HostRequest.IsFromHost(IPAddress.Loopback, IPAddress.Loopback, HostPort, HostPort));
    }

    /// <summary>
    /// The other real production shape, and the one that matters: a paired
    /// tablet reaching the LAN listener. Its own address is not loopback and
    /// the port it arrived on is not the host's - two independent reasons,
    /// either of which is enough.
    /// </summary>
    [Fact]
    public void LanPeerOnTheDeviceListener_IsNotTheHost()
    {
        Assert.False(HostRequest.IsFromHost(
            IPAddress.Parse("192.168.1.50"), IPAddress.Parse("192.168.1.37"), DevicePort, HostPort));
    }

    /// <summary>
    /// The commander's own PC, but reached through the LAN address rather
    /// than the tray's link: <b>not</b> the host. Both endpoints carry the
    /// machine's own LAN address here, which is exactly what a spoofed
    /// source address from elsewhere on the LAN would also look like - so
    /// this shape is deliberately worth nothing. The loopback listener
    /// exists precisely so there is a shape that is worth something.
    /// </summary>
    [Fact]
    public void HostsOwnLanAddressReachingTheLanListener_IsNotTheHost()
    {
        Assert.False(HostRequest.IsFromHost(
            IPAddress.Parse("192.168.1.37"), IPAddress.Parse("192.168.1.37"), DevicePort, HostPort));
    }

    /// <summary>
    /// A loopback peer that did not arrive on the host listener is refused.
    /// Cannot happen with the two listeners this host binds - it is the
    /// belt: were a third, non-loopback-only listener ever added on a port
    /// loopback traffic could also reach, host status would not follow it.
    /// </summary>
    [Fact]
    public void LoopbackPeer_OnADifferentPort_IsNotTheHost()
    {
        Assert.False(HostRequest.IsFromHost(IPAddress.Loopback, IPAddress.Loopback, DevicePort, HostPort));
    }

    /// <summary>
    /// The host port matching is not on its own sufficient either: a peer
    /// that is not loopback never counts, whatever port it reached.
    /// </summary>
    [Fact]
    public void NonLoopbackPeer_OnTheHostPort_IsNotTheHost()
    {
        Assert.False(HostRequest.IsFromHost(
            IPAddress.Parse("192.168.1.50"), IPAddress.Loopback, HostPort, HostPort));
    }

    /// <summary>
    /// And neither is a loopback peer on a local address that is not
    /// loopback - the third of the three conditions, pinned on its own so
    /// none of them can be dropped as "covered by the others".
    /// </summary>
    [Fact]
    public void LoopbackPeer_OnANonLoopbackLocalAddress_IsNotTheHost()
    {
        Assert.False(HostRequest.IsFromHost(
            IPAddress.Loopback, IPAddress.Parse("192.168.1.37"), HostPort, HostPort));
    }

    /// <summary>
    /// No host listener bound at all (<c>HostAccessPort</c> left at its
    /// default zero) means nothing is ever the host, even a genuine loopback
    /// pair on port zero. The deny direction for an unconfigured
    /// <c>ServerHostOptions</c>.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NoHostListenerConfigured_NothingIsTheHost(int hostAccessPort)
    {
        Assert.False(HostRequest.IsFromHost(IPAddress.Loopback, IPAddress.Loopback, hostAccessPort, hostAccessPort));
    }

    /// <summary>
    /// Kestrel reports null endpoints for some transports. Null is not
    /// loopback - the safe direction, and the one an <c>is null</c> slip
    /// would get backwards.
    /// </summary>
    [Fact]
    public void MissingEndpointAddresses_AreNotTheHost()
    {
        Assert.False(HostRequest.IsFromHost(null, IPAddress.Loopback, HostPort, HostPort));
        Assert.False(HostRequest.IsFromHost(IPAddress.Loopback, null, HostPort, HostPort));
    }

    /// <summary>
    /// IPv6 loopback and the IPv4-mapped form of IPv4 loopback both count.
    /// This host binds plain IPv4, so neither is observed today; the pin is
    /// here because a future dual-mode bind would otherwise answer "not the
    /// host" for every request on the machine and the symptom would be a
    /// pairing screen on the PC with nothing to explain it.
    /// </summary>
    [Theory]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("127.0.0.5")]
    public void EveryLoopbackSpelling_Counts(string address)
    {
        var ip = IPAddress.Parse(address);

        Assert.True(HostRequest.IsFromHost(ip, ip, HostPort, HostPort));
    }

    /// <summary>
    /// The id a host request is authorised as can never be one
    /// <c>DeviceRegistry</c> mints, so no paired device can ever be handed
    /// the host's layout, theme or panel settings. Driven against the real
    /// registry's shape (16 uppercase hex characters - see
    /// <c>DeviceRegistry.ComputeDeviceId</c>), not against a remembered one.
    /// </summary>
    [Fact]
    public void HostDeviceId_CannotBeAMintedDeviceId()
    {
        Assert.Equal("host", HostRequest.HostDeviceId);
        Assert.NotEqual(16, HostRequest.HostDeviceId.Length);
        Assert.Contains(HostRequest.HostDeviceId, c => !Uri.IsHexDigit(c));
    }

    [Fact]
    public void LoopbackUrl_IsTheLiteralLoopbackAddress_NeverAName()
    {
        Assert.Equal("http://127.0.0.1:51824/", HostRequest.LoopbackUrl(51824));
        Assert.Equal("http://127.0.0.1:51824/#macros", HostRequest.LoopbackUrl(51824, "#macros"));
    }

    [Fact]
    public void LoopbackUrl_IsNullWhenNoHostListenerWasBound()
    {
        Assert.Null(HostRequest.LoopbackUrl(0));
    }

    /// <summary>
    /// The port default and its override, pinned because
    /// <see cref="LunaPanel.Server.Hosting.RealServerEnvironment"/> - this
    /// function's only production call site - is deliberately never
    /// constructed by this suite.
    /// </summary>
    [Theory]
    [InlineData(null, 51823, 51824)]
    [InlineData("", 51823, 51824)]
    [InlineData("not a port", 51823, 51824)]
    [InlineData("0", 51823, 51824)]
    [InlineData("-5", 51823, 51824)]
    [InlineData("9000", 51823, 9000)]
    public void ResolveAccessPort_DefaultsToOneAboveTheDevicePort_AndTakesAnOverride(string? configured, int devicePort, int expected)
    {
        Assert.Equal(expected, HostRequest.ResolveAccessPort(configured, devicePort));
    }
}
