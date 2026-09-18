using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// UNVERIFIED against a real Frontier launcher install - see
/// <see cref="FrontierInstallDiscovery"/>'s own remarks.
/// </summary>
public class FrontierInstallDiscoveryTests
{
    [Fact]
    public void Discover_RegistryPathRecorded_ScansIt()
    {
        using var temp = TempDirectory.Create();
        var registryPath = temp.CreateSubdirectory("RegistryInstall");
        var defaultPath = temp.Combine("DoesNotExist-Default");
        temp.CreateFile("RegistryInstall/Products/p/EliteDangerous64.exe");

        var log = new DiagnosticRingBuffer(200);
        var results = FrontierInstallDiscovery.Discover(registryPath, defaultPath, log);

        Assert.Single(results);
        Assert.Equal(EliteSource.Frontier, results[0].Source);
    }

    [Fact]
    public void Discover_NoRegistryPath_FallsBackToDefaultLocation()
    {
        using var temp = TempDirectory.Create();
        var defaultPath = temp.CreateSubdirectory("DefaultInstall");
        temp.CreateFile("DefaultInstall/Products/p/EliteDangerous32.exe");

        var log = new DiagnosticRingBuffer(200);
        var results = FrontierInstallDiscovery.Discover(null, defaultPath, log);

        Assert.Single(results);
        Assert.Equal(EliteEdition.Horizons, results[0].Edition);
    }

    [Fact]
    public void Discover_NeitherLocationHasAnInstall_ReturnsEmptyCleanly()
    {
        using var temp = TempDirectory.Create();

        var log = new DiagnosticRingBuffer(200);
        var results = FrontierInstallDiscovery.Discover(
            temp.Combine("NoRegistryInstall"), temp.Combine("NoDefaultInstall"), log);

        Assert.Empty(results);
    }
}
