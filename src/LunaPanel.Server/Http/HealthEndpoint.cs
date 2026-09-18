using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/health</c> - unauthenticated liveness plus a coarse readiness
/// summary. Deliberately carries no paths or other environment-derived
/// strings: every field is a bare bool, which is what makes "no paths in the
/// response" true by construction rather than by discipline.
/// </summary>
public static class HealthEndpoint
{
    public sealed record Response(bool Live, bool BindsFound, bool EliteInstallFound);

    public static Response BuildResponse(PathDiscoveryResult discovery) => new(
        Live: true,
        BindsFound: discovery.Bindings.LatestBindsFilePath is not null,
        EliteInstallFound: discovery.EliteInstallations.Count > 0);
}
