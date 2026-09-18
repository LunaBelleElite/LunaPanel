using LunaPanel.Core.Network;

namespace LunaPanel.Server.Hosting;

/// <summary>
/// The device listener's effective port, pulled out of
/// <see cref="RealServerEnvironment"/> so it is unit-testable - the same
/// split <see cref="Http.HostRequest.ResolveAccessPort"/> already uses, since
/// <see cref="RealServerEnvironment"/> itself is deliberately never
/// constructed by this suite.
///
/// Precedence: the <c>LUNAPANEL_PORT</c> environment variable, when it parses
/// as a valid integer, wins outright - this keeps that variable working
/// exactly as it does today for power users and testing. Otherwise the
/// persisted <see cref="PortSettings"/> a commander chose via the Ports
/// dialog wins. Otherwise <see cref="PortSettings.DefaultPort"/>.
///
/// Deliberately does NOT range-check the environment variable's value
/// against <see cref="PortSettings.MinPort"/>/<see cref="PortSettings.MaxPort"/> -
/// same treatment <see cref="Http.HostRequest.ResolveAccessPort"/> gives
/// <c>LUNAPANEL_HOST_PORT</c>: an out-of-range or already-occupied port
/// fails loudly when Kestrel refuses to bind, which is the existing failure
/// mode for that variable and not one this function changes.
/// </summary>
public static class PortResolution
{
    public static int Resolve(string? envVarValue, PortSettings persisted) =>
        int.TryParse(envVarValue, out var configuredPort)
            ? configuredPort
            : (persisted ?? PortSettings.Default).Port;
}
