using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LunaPanel.Server.Hosting;

/// <summary>
/// One IPv4 unicast address as a bind candidate, carrying just enough about
/// the interface it came from for <see cref="LanAddressResolver.Resolve"/>
/// to rank it deliberately instead of taking whatever the OS happened to
/// enumerate first (see <c>tests/notes/open-items.md</c> O12, closed by this
/// type's introduction - the bare <see cref="IPAddress"/> list this replaced
/// carried no interface type and no gateway, so no ranking could be
/// expressed in a pure function at all).
/// </summary>
/// <param name="Address">The candidate IPv4 unicast address.</param>
/// <param name="InterfaceType">
/// The owning interface's reported type. Used only to deprioritise
/// tunnel-type adapters (<see cref="NetworkInterfaceType.Tunnel"/>,
/// <see cref="NetworkInterfaceType.Ppp"/>) - never to identify a specific
/// vendor or product, which this project deliberately does not do (see
/// <c>tests/notes/live-checks.md</c> LC11).
/// </param>
/// <param name="HasGateway">
/// Whether the owning interface carries a real (non-zero, non-loopback)
/// default gateway - the strongest available signal that this interface is
/// actually routed onto a real network, rather than a tunnel endpoint with
/// no default route of its own.
/// </param>
/// <param name="InterfaceName">
/// Human-readable interface name, carried only for diagnostic logging so a
/// startup log line can name which candidates existed and which one was
/// chosen.
/// </param>
public sealed record LanCandidate(
    IPAddress Address,
    NetworkInterfaceType InterfaceType,
    bool HasGateway,
    string InterfaceName);

/// <summary>
/// Picks the single IPv4 address Kestrel binds to. LunaPanel is a LAN-facing
/// service and must bind to one concrete address a tablet can actually
/// reach - never <c>0.0.0.0</c>/<c>::</c> ("every interface"), which is both
/// broader than needed and the opposite of what a single bind address is
/// for.
///
/// <see cref="Resolve"/> is a pure function of its inputs, so it is testable
/// without depending on the real machine's network configuration: production
/// gathers real candidates via <see cref="GetCandidateAddresses"/> (the one
/// real, untested-by-design call site - see <c>ref/docs/discovery.md</c> for
/// the same split applied to path discovery), tests supply a synthetic list.
/// </summary>
public static class LanAddressResolver
{
    /// <summary>
    /// Interface types that genuinely denote a virtual/tunnel adapter, per
    /// the real <see cref="NetworkInterfaceType"/> enum - never a vendor or
    /// product name (see this type's own remarks, and O12/LC11). Checked
    /// directly against the enum rather than guessed at.
    /// </summary>
    private static readonly HashSet<NetworkInterfaceType> TunnelTypes = new()
    {
        NetworkInterfaceType.Tunnel,
        NetworkInterfaceType.Ppp,
    };

    /// <summary>
    /// Every "up", non-loopback IPv4 unicast address on this machine, in
    /// whatever order the OS reports its network interfaces, each carrying
    /// its interface's type, gateway presence and name. Not unit-tested
    /// itself - its result is entirely a function of the real machine's
    /// network configuration, which a test cannot control - <see cref="Resolve"/>
    /// is what carries the actual, testable selection logic.
    /// </summary>
    public static IReadOnlyList<LanCandidate> GetCandidateAddresses()
    {
        var candidates = new List<LanCandidate>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = nic.GetIPProperties();
            var hasGateway = properties.GatewayAddresses.Any(gateway => IsRealGatewayAddress(gateway.Address));

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    candidates.Add(new LanCandidate(unicast.Address, nic.NetworkInterfaceType, hasGateway, nic.Name));
                }
            }
        }

        return candidates;
    }

    private static bool IsRealGatewayAddress(IPAddress address) =>
        !IPAddress.Any.Equals(address)
        && !IPAddress.IPv6Any.Equals(address)
        && !IPAddress.IsLoopback(address);

    /// <summary>
    /// Resolves the bind address: <paramref name="configuredOverride"/> if
    /// it is set and parses as an IP address, otherwise the best-ranked of
    /// <paramref name="candidateAddresses"/> (see <see cref="RankKey"/>).
    /// Never returns <c>0.0.0.0</c> or <c>::</c> ("every interface") - even
    /// an explicit override asking for one of those is rejected, since
    /// binding to every interface is exactly what a single configured LAN
    /// address exists to avoid.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No override was given and <paramref name="candidateAddresses"/> is
    /// empty (no safe default exists - loopback would make the panel
    /// unreachable from any tablet), the override does not parse as an IP
    /// address, or the override resolves to an all-interfaces address.
    /// </exception>
    public static IPAddress Resolve(IReadOnlyList<LanCandidate> candidateAddresses, string? configuredOverride)
    {
        IPAddress resolved;

        if (!string.IsNullOrWhiteSpace(configuredOverride))
        {
            if (!IPAddress.TryParse(configuredOverride, out var overrideAddress))
            {
                throw new InvalidOperationException(
                    $"Configured bind address override '{configuredOverride}' is not a valid IP address.");
            }

            resolved = overrideAddress;
        }
        else
        {
            if (candidateAddresses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No non-loopback IPv4 network address was found to bind LunaPanel's server to.");
            }

            resolved = candidateAddresses.OrderBy(RankKey).First().Address;
        }

        if (IPAddress.Any.Equals(resolved) || IPAddress.IPv6Any.Equals(resolved))
        {
            throw new InvalidOperationException(
                $"Resolved bind address '{resolved}' is an all-interfaces address, which LunaPanel must never bind to.");
        }

        return resolved;
    }

    /// <summary>
    /// Lower is preferred. Two independent criteria, applied together: (1)
    /// a private-range (RFC1918) address on an interface with a real
    /// default gateway ranks above any address on a gatewayed interface,
    /// which in turn ranks above everything else; (2) within each of those
    /// three tiers, a tunnel-type interface (<see cref="TunnelTypes"/>)
    /// ranks below a non-tunnel one. This is deliberately by type and
    /// reachability only, never by interface name/description/vendor - see
    /// this file's O12/LC11 references.
    /// </summary>
    private static int RankKey(LanCandidate candidate)
    {
        var isTunnel = TunnelTypes.Contains(candidate.InterfaceType);
        var isPrivate = IsPrivateRfc1918(candidate.Address);

        if (isPrivate && candidate.HasGateway)
        {
            return isTunnel ? 1 : 0;
        }

        if (candidate.HasGateway)
        {
            return isTunnel ? 3 : 2;
        }

        return isTunnel ? 5 : 4;
    }

    private static bool IsPrivateRfc1918(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}
