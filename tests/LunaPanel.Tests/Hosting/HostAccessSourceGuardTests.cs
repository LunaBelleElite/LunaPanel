namespace LunaPanel.Tests.Hosting;

/// <summary>
/// Source-scan pins for the three claims about host access that this suite
/// cannot drive, each for a stated reason rather than for convenience.
///
/// <b>1. The host listener is bound to loopback and nothing else.</b> The
/// only way to drive that would be a test host bound to a real LAN address,
/// which would mean binding a machine-dependent interface in a suite that
/// runs offline - so the end-to-end tests in
/// <c>ServerHostBuilderTests</c> prove the <em>port</em> half of
/// <c>HostRequest.IsFromHost</c> honestly (their two listeners are on two
/// ports) and are blind to the <em>address</em> half (both are loopback).
/// Swapping <c>IPAddress.Loopback</c> for <c>options.BindAddress</c> in that
/// <c>Listen</c> call would pass every one of them and would put the
/// authoring surface on the LAN. This is the pin that sees it.
///
/// <b>2. No forwarded-headers middleware.</b> ASP.NET Core only rewrites
/// <c>Connection.RemoteIpAddress</c> from <c>X-Forwarded-For</c> when
/// <c>UseForwardedHeaders</c> is in the pipeline. It is not, and
/// <c>SpoofedHeaders_DoNotGrantHostStatus_AndDoNotTakeItAwayEither</c>
/// drives what adding one would do - but that test names the failure, not
/// the cause, and the cause is one line somebody could add tomorrow to fix
/// an unrelated reverse-proxy problem.
///
/// <b>3. Production actually configures a host port.</b>
/// <c>RealServerEnvironment</c> is deliberately never constructed by this
/// suite (see its own remarks), so nothing else can notice if the wiring is
/// dropped and every commander is left with a pairing screen on their own
/// PC.
/// </summary>
public class HostAccessSourceGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LunaPanel.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing LunaPanel.sln) above {AppContext.BaseDirectory}.");
    }

    private static string ReadServerSource(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { FindRepoRoot(), "src", "LunaPanel.Server" }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheHostListener_IsBoundToLoopbackOnly()
    {
        var content = ReadServerSource("Hosting", "ServerHostBuilder.cs");

        Assert.Contains("kestrel.Listen(IPAddress.Loopback, options.HostAccessPort)", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// The device listener is the one that takes the configured address, and
    /// it is a different call. Pinned alongside so a future edit that
    /// collapses the two <c>Listen</c> calls into one loop has to decide,
    /// visibly, which address each is getting.
    /// </summary>
    [Fact]
    public void TheDeviceListener_IsStillTheOneThatTakesTheConfiguredAddress()
    {
        var content = ReadServerSource("Hosting", "ServerHostBuilder.cs");

        Assert.Contains("kestrel.Listen(options.BindAddress, options.Port)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void NoForwardedHeadersMiddleware_AnywhereInThePipeline()
    {
        Assert.DoesNotContain("UseForwardedHeaders", ReadServerSource("Hosting", "ServerHostBuilder.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("UseForwardedHeaders", ReadServerSource("Http", "DeviceAuthMiddlewareExtensions.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The evidence is the connection. Pinned as the three exact expressions
    /// handed to <c>HostRequest.IsFromHost</c>, because the failure this
    /// guards against is not the call disappearing - the end-to-end tests
    /// would catch that - but one argument being swapped for something a
    /// caller writes.
    /// </summary>
    [Fact]
    public void TheHostDecision_ReadsTheConnection_NotTheRequest()
    {
        var content = ReadServerSource("Http", "DeviceAuthMiddlewareExtensions.cs");

        Assert.Contains("context.Connection.RemoteIpAddress", content, StringComparison.Ordinal);
        Assert.Contains("context.Connection.LocalIpAddress", content, StringComparison.Ordinal);
        Assert.Contains("context.Connection.LocalPort", content, StringComparison.Ordinal);
    }

    [Fact]
    public void RealServerEnvironment_ConfiguresAHostAccessPort()
    {
        var content = ReadServerSource("Hosting", "RealServerEnvironment.cs");

        Assert.Contains("HostRequest.ResolveAccessPort(", content, StringComparison.Ordinal);
        Assert.Contains("LUNAPANEL_HOST_PORT", content, StringComparison.Ordinal);
        Assert.Contains("HostAccessPort: hostAccessPort", content, StringComparison.Ordinal);
    }
}
