using LunaPanel.Server.Http;

namespace LunaPanel.Tests.Http;

/// <summary>
/// The DNS-rebinding guard, driven directly: which <c>Host</c> header values
/// LunaPanel answers to (<c>ref/docs/hosting.md</c>, "DNS rebinding and the
/// Host header"). The end-to-end half - a real two-listener Kestrel host
/// refusing a foreign Host on both listeners, auth-exempt routes included -
/// is in <c>ServerHostBuilderTests</c>.
///
/// <b>The rule is an allowlist of shapes, not a blocklist of names.</b> An IP
/// literal, <c>localhost</c>, or this machine's own name (bare or
/// <c>.local</c>), on the port the request actually arrived on. Every refusal
/// row below is a shape a rebinding page could put in front of a browser;
/// the allow rows are the shapes a real tablet, phone or the PC's own
/// browser sends.
///
/// The machine name here is made up (<see cref="Machine"/>); production
/// passes <c>Environment.MachineName</c>.
/// </summary>
public class HostHeaderPolicyTests
{
    private const string Machine = "TESTRIG-01";
    private const int Port = 51823;

    private static readonly IReadOnlyCollection<string> Trusted = HostHeaderPolicy.TrustedNamesFor(Machine);

    private static bool Allowed(string? host, int localPort = Port) =>
        HostHeaderPolicy.IsAllowed(host, localPort, Trusted);

    [Theory]
    // IPv4 literals: loopback, and the LAN shapes a tablet actually uses.
    [InlineData("127.0.0.1:51823")]
    [InlineData("192.168.1.12:51823")]
    [InlineData("10.0.0.5:51823")]
    [InlineData("0.0.0.0:51823")]
    [InlineData("255.255.255.255:51823")]
    // IPv6 literals, bracketed.
    [InlineData("[::1]:51823")]
    [InlineData("[fe80::1]:51823")]
    [InlineData("[2001:db8::1]:51823")]
    [InlineData("[::ffff:127.0.0.1]:51823")]
    // localhost, any case.
    [InlineData("localhost:51823")]
    [InlineData("LOCALHOST:51823")]
    [InlineData("LocalHost:51823")]
    // This machine's own name and its mDNS form, any case.
    [InlineData("TESTRIG-01:51823")]
    [InlineData("testrig-01:51823")]
    [InlineData("TestRig-01:51823")]
    [InlineData("TESTRIG-01.local:51823")]
    [InlineData("testrig-01.LOCAL:51823")]
    public void OurOwnNames_OnTheArrivalPort_AreAllowed(string host)
    {
        Assert.True(Allowed(host));
    }

    /// <summary>
    /// An absent port means 80, so a listener on 80 accepts a bare name and
    /// any other listener refuses it - a browser omits the port only when it
    /// is the default.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1", 80, true)]
    [InlineData("localhost", 80, true)]
    [InlineData("[::1]", 80, true)]
    [InlineData("TESTRIG-01", 80, true)]
    [InlineData("127.0.0.1", Port, false)]
    [InlineData("localhost", Port, false)]
    [InlineData("[::1]", Port, false)]
    [InlineData("TESTRIG-01", Port, false)]
    [InlineData("127.0.0.1:80", 80, true)]
    public void AnAbsentPort_MeansEighty(string host, int localPort, bool expected)
    {
        Assert.Equal(expected, Allowed(host, localPort));
    }

    [Theory]
    [InlineData("127.0.0.1:51824")]
    [InlineData("localhost:51824")]
    [InlineData("TESTRIG-01:80")]
    [InlineData("[::1]:51822")]
    public void APortThatIsNotTheArrivalPort_IsRefused(string host)
    {
        Assert.False(Allowed(host));
    }

    [Theory]
    // Foreign names - the rebinding page's own domain.
    [InlineData("evil.example:51823")]
    [InlineData("evil.example")]
    [InlineData("localhost.evil.example:51823")]
    [InlineData("TESTRIG-01.evil.example:51823")]
    [InlineData("TESTRIG-01.lan:51823")]
    [InlineData("OTHERRIG-02:51823")]
    // Wildcard DNS that resolves to loopback: not an IP literal, so not ours.
    [InlineData("127.0.0.1.nip.io:51823")]
    [InlineData("localtest.me:51823")]
    [InlineData("1.0.0.127.in-addr.arpa:51823")]
    // Userinfo-looking and path-looking shapes.
    [InlineData("evil.example/@127.0.0.1:51823")]
    [InlineData("127.0.0.1@evil.example:51823")]
    [InlineData("user@127.0.0.1:51823")]
    [InlineData("127.0.0.1/:51823")]
    [InlineData("127.0.0.1\\:51823")]
    // Trailing-dot FQDN forms. Rejected rather than normalised: no browser
    // needs one to reach LunaPanel, and accepting "localhost." would mean
    // trusting a resolver's handling of an absolute name we never chose.
    [InlineData("localhost.:51823")]
    [InlineData("127.0.0.1.:51823")]
    [InlineData("TESTRIG-01.:51823")]
    [InlineData("TESTRIG-01.local.:51823")]
    public void ForeignOrMalformedNames_AreRefused(string host)
    {
        Assert.False(Allowed(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" 127.0.0.1:51823")]
    [InlineData("127.0.0.1:51823 ")]
    [InlineData("127.0.0.1 :51823")]
    [InlineData("127.0.0.1:51823\r\n")]
    [InlineData("127.0.0.1\0:51823")]
    [InlineData("local host:51823")]
    public void EmptyWhitespaceOrControlCharacters_AreRefused(string? host)
    {
        Assert.False(Allowed(host));
    }

    [Fact]
    public void AVeryLongHost_IsRefused_EvenIfItWouldOtherwiseMatch()
    {
        // A trusted name long enough to cross the cap, so the length rule is
        // what refuses it rather than the name failing to match.
        var longName = new string('a', 300);
        var trusted = HostHeaderPolicy.TrustedNamesFor(longName);

        Assert.False(HostHeaderPolicy.IsAllowed($"{longName}:{Port}", Port, trusted));
        Assert.False(HostHeaderPolicy.IsAllowed(new string('1', 300), Port, Trusted));
    }

    [Theory]
    [InlineData("127.0.0.1:")]
    [InlineData("127.0.0.1:abc")]
    [InlineData("127.0.0.1:5182x")]
    [InlineData("127.0.0.1:+51823")]
    [InlineData("127.0.0.1:-1")]
    [InlineData("127.0.0.1:0")]
    [InlineData("127.0.0.1:65536")]
    [InlineData("127.0.0.1:123456")]
    [InlineData("127.0.0.1:051823")]
    [InlineData("127.0.0.1:51823:51823")]
    [InlineData("[::1]:")]
    [InlineData("[::1]51823")]
    [InlineData("[::1]:abc")]
    public void APortThatIsNotAPlainNumberInRange_IsRefused(string host)
    {
        Assert.False(Allowed(host));
    }

    /// <summary>
    /// Port 0 and 65536 are refused even when the "arrival port" says the
    /// same - no real connection arrives on either, so this is the range
    /// check, not the equality check.
    /// </summary>
    [Fact]
    public void PortsOutsideTheRealRange_AreRefused_EvenWhenTheyEqualTheArrivalPort()
    {
        Assert.False(Allowed("127.0.0.1:0", 0));
        Assert.False(Allowed("127.0.0.1:65536", 65536));
    }

    /// <summary>
    /// Non-canonical IPv4 spellings. Every one of these resolves to a real
    /// address in some parser (<c>2130706433</c> and <c>0x7f000001</c> are
    /// both 127.0.0.1 to <c>inet_aton</c>), and none is what a browser shows
    /// for an address it was given as four octets. Refused on purpose: one
    /// spelling per address is the simplest rule a reader can check.
    /// </summary>
    [Theory]
    [InlineData("2130706433:51823")]
    [InlineData("0x7f000001:51823")]
    [InlineData("0x7f.0.0.1:51823")]
    [InlineData("0177.0.0.1:51823")]
    [InlineData("127.1:51823")]
    [InlineData("127.0.1:51823")]
    [InlineData("127.000.000.001:51823")]
    [InlineData("127.0.0.01:51823")]
    [InlineData("256.0.0.1:51823")]
    [InlineData("127.0.0.1.1:51823")]
    [InlineData("127..0.1:51823")]
    [InlineData(".127.0.0.1:51823")]
    public void NonCanonicalIPv4Spellings_AreRefused(string host)
    {
        Assert.False(Allowed(host));
    }

    [Theory]
    // Bracket-less IPv6 - the port cannot be told apart from the address.
    [InlineData("::1")]
    [InlineData("::1:51823")]
    [InlineData("fe80::1:51823")]
    // Zone ids, raw and percent-encoded.
    [InlineData("[fe80::1%eth0]:51823")]
    [InlineData("[fe80::1%25eth0]:51823")]
    // Broken brackets, and a bracketed thing that is not IPv6.
    [InlineData("[::1:51823")]
    [InlineData("::1]:51823")]
    [InlineData("[127.0.0.1]:51823")]
    [InlineData("[localhost]:51823")]
    [InlineData("[]:51823")]
    [InlineData("[::1]x:51823")]
    [InlineData("[[::1]]:51823")]
    [InlineData("[:::1]:51823")]
    public void MalformedIPv6_IsRefused(string host)
    {
        Assert.False(Allowed(host));
    }

    [Fact]
    public void TrustedNames_AreTheMachineNameAndItsLocalForm()
    {
        Assert.Equal(new[] { "TESTRIG-01", "TESTRIG-01.local" }, HostHeaderPolicy.TrustedNamesFor("TESTRIG-01"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankMachineName_TrustsNoNameAtAll(string? machine)
    {
        Assert.Empty(HostHeaderPolicy.TrustedNamesFor(machine));
        Assert.False(HostHeaderPolicy.IsAllowed(".local:51823", Port, HostHeaderPolicy.TrustedNamesFor(machine)));
    }

    /// <summary>
    /// Without a trusted-name list, a machine name is a foreign name like any
    /// other - the name is ours only because production says so.
    /// </summary>
    [Fact]
    public void TheMachineName_IsOnlyAllowedBecauseItIsInTheTrustedList()
    {
        Assert.False(HostHeaderPolicy.IsAllowed("TESTRIG-01:51823", Port, Array.Empty<string>()));
        Assert.True(HostHeaderPolicy.IsAllowed("127.0.0.1:51823", Port, Array.Empty<string>()));
    }

    [Fact]
    public void TheRefusalMessage_NamesWhatLunaPanelAnswersTo()
    {
        Assert.Equal("LunaPanel only answers to its IP address or this computer's name.", HostHeaderPolicy.RefusalMessage);
    }
}
