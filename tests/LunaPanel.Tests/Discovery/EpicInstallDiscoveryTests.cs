using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// UNVERIFIED against a real Epic manifest - see
/// <see cref="EpicInstallDiscovery"/>'s own remarks. These tests only pin
/// this project's own defensive handling of the documented <c>.item</c>
/// JSON shape, not that the shape itself is correct.
/// </summary>
public class EpicInstallDiscoveryTests
{
    [Fact]
    public void Discover_ManifestMatchingEliteDangerous_ScansItsInstallLocation()
    {
        using var temp = TempDirectory.Create();
        var manifests = temp.CreateSubdirectory("Manifests");
        var installLocation = temp.CreateSubdirectory("EliteInstall");
        temp.CreateFile("EliteInstall/Products/p/EliteDangerous64.exe");

        temp.CreateFile(
            "Manifests/EliteDangerous.item",
            $$"""{ "DisplayName": "Elite Dangerous", "InstallLocation": "{{installLocation.Replace(@"\", @"\\")}}" }""");

        var log = new DiagnosticRingBuffer(200);
        var results = EpicInstallDiscovery.Discover(manifests, log);

        Assert.Single(results);
        Assert.Equal(EliteSource.Epic, results[0].Source);
        Assert.Equal(EliteEdition.Odyssey, results[0].Edition);
    }

    [Fact]
    public void Discover_ManifestForUnrelatedGame_IsIgnored()
    {
        using var temp = TempDirectory.Create();
        var manifests = temp.CreateSubdirectory("Manifests");
        temp.CreateFile(
            "Manifests/SomeOtherGame.item",
            """{ "DisplayName": "Some Other Game", "InstallLocation": "C:\\Games\\Other" }""");

        var log = new DiagnosticRingBuffer(200);
        var results = EpicInstallDiscovery.Discover(manifests, log);

        Assert.Empty(results);
    }

    [Fact]
    public void Discover_MalformedManifestJson_IsSkippedNotThrown()
    {
        using var temp = TempDirectory.Create();
        var manifests = temp.CreateSubdirectory("Manifests");
        temp.CreateFile("Manifests/Broken.item", "{ not valid json");

        var log = new DiagnosticRingBuffer(200);
        var results = EpicInstallDiscovery.Discover(manifests, log);

        Assert.Empty(results);
    }

    [Fact]
    public void Discover_NoManifestsDirectory_ReturnsEmptyCleanly()
    {
        using var temp = TempDirectory.Create();
        var manifests = temp.Combine("DoesNotExist");

        var log = new DiagnosticRingBuffer(200);
        var results = EpicInstallDiscovery.Discover(manifests, log);

        Assert.Empty(results);
    }
}
