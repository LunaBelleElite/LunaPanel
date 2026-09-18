using System.Net;
using System.Net.NetworkInformation;
using LunaPanel.Server.Hosting;

namespace LunaPanel.Tests.Hosting;

/// <summary>
/// Drives <see cref="LanAddressResolver.Resolve"/> against hand-built
/// candidate lists - never the real machine's network configuration, which
/// <see cref="LanAddressResolver.GetCandidateAddresses"/> is deliberately
/// left untested against (see its own remarks - the same split
/// <c>ref/docs/discovery.md</c> describes for path discovery).
///
/// O12 (<c>tests/notes/open-items.md</c>): <c>Resolve</c> used to return
/// whatever candidate the OS enumerated first, with no preference for a
/// physical LAN adapter over a VPN tunnel - confirmed to actually fire in
/// production, see <c>tests/notes/live-checks.md</c> LC11. The ranking
/// tests below are the fix: deliberate, ordered selection by interface type
/// and gateway reachability only, never by interface name/vendor.
/// </summary>
public class LanAddressResolverTests
{
    private static readonly LanCandidate CandidateA =
        new(IPAddress.Parse("192.168.1.50"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "eth0");

    private static readonly LanCandidate CandidateB =
        new(IPAddress.Parse("10.0.0.7"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "eth1");

    [Fact]
    public void Resolve_ValidOverride_UsesOverride_IgnoringCandidates()
    {
        var result = LanAddressResolver.Resolve(new[] { CandidateA, CandidateB }, configuredOverride: "172.16.5.9");

        Assert.Equal(IPAddress.Parse("172.16.5.9"), result);
    }

    [Fact]
    public void Resolve_EmptyStringOverride_TreatedAsNoOverride_RanksCandidates()
    {
        var result = LanAddressResolver.Resolve(new[] { CandidateA, CandidateB }, configuredOverride: "   ");

        Assert.Equal(CandidateA.Address, result);
    }

    [Fact]
    public void Resolve_NoOverrideAndNoCandidates_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LanAddressResolver.Resolve(Array.Empty<LanCandidate>(), configuredOverride: null));
    }

    [Fact]
    public void Resolve_OverrideIsNotAValidIpAddress_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LanAddressResolver.Resolve(new[] { CandidateA }, configuredOverride: "not-an-ip"));
    }

    [Fact]
    public void Resolve_OverrideIsAllInterfacesIPv4_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LanAddressResolver.Resolve(new[] { CandidateA }, configuredOverride: "0.0.0.0"));
    }

    [Fact]
    public void Resolve_OverrideIsAllInterfacesIPv6_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => LanAddressResolver.Resolve(new[] { CandidateA }, configuredOverride: "::"));
    }

    /// <summary>
    /// This test used to be named <c>Resolve_NoOverride_ReturnsFirstCandidate</c>
    /// and pinned the O12 defect directly: "no override means take whatever
    /// is first, full stop", which was true only because nothing about the
    /// old signature could express a reason to prefer one candidate over
    /// another. It is rewritten here as a **deliberate behaviour change**,
    /// not a pin loosened to pass: both candidates below rank identically
    /// (private range, real gateway, non-tunnel), so "first wins" now means
    /// "first wins the tie-break between equally-good candidates", which is
    /// a sound fallback - not the defect. See
    /// <see cref="Resolve_NoOverride_TunnelEnumeratesFirst_LanAddressStillChosen"/>
    /// for the test that actually exercises discrimination between
    /// differently-ranked candidates, which is O12's real exit condition.
    /// </summary>
    [Fact]
    public void Resolve_NoOverride_EquallyRankedCandidates_FirstWinsAsTiebreak()
    {
        var result = LanAddressResolver.Resolve(new[] { CandidateA, CandidateB }, configuredOverride: null);

        Assert.Equal(CandidateA.Address, result);
    }

    /// <summary>
    /// O12's exit condition, verbatim: a test drives a candidate list in
    /// which the tunnel enumerates first and the LAN address is still
    /// chosen. Modelled directly on the real shape observed in LC11/LC7 - a
    /// WireGuard-style tunnel address is itself in RFC1918 private range
    /// (<c>10.5.0.2</c>), so this also proves the tunnel-type deprioritisation
    /// is doing real work and not merely riding along on the private-range
    /// or gateway checks: both candidates here are private-range with a real
    /// gateway, so only <see cref="NetworkInterfaceType"/> differs.
    /// </summary>
    [Fact]
    public void Resolve_NoOverride_TunnelEnumeratesFirst_LanAddressStillChosen()
    {
        var tunnel = new LanCandidate(IPAddress.Parse("10.5.0.2"), NetworkInterfaceType.Tunnel, HasGateway: true, InterfaceName: "vpn0");
        var lan = new LanCandidate(IPAddress.Parse("192.168.1.37"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "Ethernet");

        var result = LanAddressResolver.Resolve(new[] { tunnel, lan }, configuredOverride: null);

        Assert.Equal(lan.Address, result);
    }

    /// <summary>Same shape as the Tunnel case, for the other named tunnel-type: Ppp.</summary>
    [Fact]
    public void Resolve_NoOverride_PppEnumeratesFirst_LanAddressStillChosen()
    {
        var ppp = new LanCandidate(IPAddress.Parse("10.8.0.4"), NetworkInterfaceType.Ppp, HasGateway: true, InterfaceName: "ppp0");
        var lan = new LanCandidate(IPAddress.Parse("192.168.1.37"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "Ethernet");

        var result = LanAddressResolver.Resolve(new[] { ppp, lan }, configuredOverride: null);

        Assert.Equal(lan.Address, result);
    }

    /// <summary>
    /// Proves the private-range preference is a real, separate tier above
    /// "any address with a real gateway" - not merely a side effect of the
    /// tunnel-type check. The non-private candidate enumerates first and has
    /// a gateway too; it still loses to the private, gatewayed one.
    /// </summary>
    [Fact]
    public void Resolve_NoOverride_PrivateWithGatewayOutranksNonPrivateWithGateway()
    {
        var nonPrivate = new LanCandidate(IPAddress.Parse("8.8.8.8"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "eth-public");
        var privateWithGateway = new LanCandidate(IPAddress.Parse("192.168.1.10"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "eth-private");

        var result = LanAddressResolver.Resolve(new[] { nonPrivate, privateWithGateway }, configuredOverride: null);

        Assert.Equal(privateWithGateway.Address, result);
    }

    /// <summary>
    /// Proves gateway presence is its own tier, independent of the private
    /// range check: a private address with no gateway loses to a non-private
    /// address that does have one.
    /// </summary>
    [Fact]
    public void Resolve_NoOverride_GatewayPresenceOutranksPrivateRangeAlone()
    {
        var privateNoGateway = new LanCandidate(IPAddress.Parse("192.168.1.20"), NetworkInterfaceType.Ethernet, HasGateway: false, InterfaceName: "eth-nogw");
        var publicWithGateway = new LanCandidate(IPAddress.Parse("8.8.4.4"), NetworkInterfaceType.Ethernet, HasGateway: true, InterfaceName: "eth-gw");

        var result = LanAddressResolver.Resolve(new[] { privateNoGateway, publicWithGateway }, configuredOverride: null);

        Assert.Equal(publicWithGateway.Address, result);
    }
}
