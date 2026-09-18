using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Finds Elite Dangerous installed via Frontier's own standalone launcher.
///
/// UNVERIFIED: there is no standalone Frontier launcher install on the
/// machine this was authored on, so neither the default install location
/// nor the registry key Frontier's launcher may record itself under have
/// been checked against reality.
///
/// The registry value is deliberately taken as an already-read
/// <c>string?</c> parameter rather than read here with
/// <c>Microsoft.Win32.Registry</c> directly, for two reasons: it keeps this
/// method's own logic testable without a real registry (a test supplies
/// <c>null</c> or a synthetic path), and it sidesteps the platform-specific
/// registry APIs entirely for now, since this project's target framework
/// (<c>net10.0</c>, not <c>net10.0-windows</c>) reports every
/// <c>Microsoft.Win32.Registry</c> call site as CA1416 platform-compatibility
/// warnings, which would fail this project's zero-warnings build. Whatever
/// eventually reads the real registry value (a later wiring task) can decide
/// how to satisfy or suppress that warning at the one call site that needs
/// it, rather than this method needing to.
/// </summary>
public static class FrontierInstallDiscovery
{
    public static IReadOnlyList<EliteInstallation> Discover(
        string? registryInstallPath,
        string defaultInstallRoot,
        IDiagnosticLog log)
    {
        var results = new List<EliteInstallation>();

        if (!string.IsNullOrEmpty(registryInstallPath))
        {
            log.Info("Discovery", $"Frontier launcher (registry-recorded path): {registryInstallPath} -> probing");
            results.AddRange(EliteProductScanner.Scan(registryInstallPath, EliteSource.Frontier, log));
        }
        else
        {
            log.Info("Discovery", "Frontier launcher: no registry-recorded install path, falling back to default location");
        }

        log.Info("Discovery", $"Frontier launcher (default install location): {defaultInstallRoot} -> probing");
        results.AddRange(EliteProductScanner.Scan(defaultInstallRoot, EliteSource.Frontier, log));

        return results;
    }
}
