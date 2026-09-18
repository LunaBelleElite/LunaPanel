using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

public class SteamInstallDiscoveryTests
{
    private static string BuildVdf(params string[] libraryPaths)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\"libraryfolders\"");
        sb.AppendLine("{");
        for (var i = 0; i < libraryPaths.Length; i++)
        {
            sb.AppendLine($"\t\"{i}\"");
            sb.AppendLine("\t{");
            sb.AppendLine($"\t\t\"path\"\t\t\"{libraryPaths[i].Replace(@"\", @"\\")}\"");
            sb.AppendLine("\t}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    [Fact]
    public void Discover_LibraryPathThatDoesNotExist_IsSkippedNotThrown()
    {
        using var temp = TempDirectory.Create();
        var steamRoot = temp.CreateSubdirectory("Steam");
        var missingLibrary = temp.Combine("DoesNotExist");
        var realLibrary = temp.CreateSubdirectory("RealLibrary");
        temp.CreateFile("RealLibrary/steamapps/common/Elite Dangerous/Products/p/EliteDangerous64.exe");

        temp.CreateFile("Steam/steamapps/libraryfolders.vdf", BuildVdf(missingLibrary, realLibrary));

        var log = new DiagnosticRingBuffer(200);
        var results = SteamInstallDiscovery.Discover(new[] { steamRoot }, log);

        // Predicted: exactly one installation, from the real library only -
        // the missing one must not throw and must not appear.
        Assert.Single(results);
        Assert.Equal(EliteEdition.Odyssey, results[0].Edition);
    }

    [Fact]
    public void Discover_LibraryPathThatDoesNotExist_LogsSkippedNotFound()
    {
        using var temp = TempDirectory.Create();
        var steamRoot = temp.CreateSubdirectory("Steam");
        var missingLibrary = temp.Combine("DoesNotExist");
        temp.CreateFile("Steam/steamapps/libraryfolders.vdf", BuildVdf(missingLibrary));

        var log = new DiagnosticRingBuffer(200);
        SteamInstallDiscovery.Discover(new[] { steamRoot }, log);

        Assert.Contains(log.Snapshot(), e => e.Message.Contains("missing, skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_MultipleLibrariesOnDifferentPaths_FindsInstallationsFromEach()
    {
        using var temp = TempDirectory.Create();
        var steamRoot = temp.CreateSubdirectory("Steam");
        var libraryA = temp.CreateSubdirectory("LibA");
        var libraryB = temp.CreateSubdirectory("LibB");
        temp.CreateFile("LibA/steamapps/common/Elite Dangerous/Products/odyssey/EliteDangerous64.exe");
        temp.CreateFile("LibB/steamapps/common/Elite Dangerous/Products/horizons/EliteDangerous32.exe");
        temp.CreateFile("Steam/steamapps/libraryfolders.vdf", BuildVdf(libraryA, libraryB));

        var log = new DiagnosticRingBuffer(200);
        var results = SteamInstallDiscovery.Discover(new[] { steamRoot }, log);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Edition == EliteEdition.Odyssey);
        Assert.Contains(results, r => r.Edition == EliteEdition.Horizons);
        Assert.All(results, r => Assert.Equal(EliteSource.Steam, r.Source));
    }

    [Fact]
    public void Discover_NoLibraryFoldersVdf_ReturnsEmptyCleanly()
    {
        using var temp = TempDirectory.Create();
        var steamRoot = temp.CreateSubdirectory("Steam");

        var log = new DiagnosticRingBuffer(200);
        var results = SteamInstallDiscovery.Discover(new[] { steamRoot }, log);

        Assert.Empty(results);
    }

    [Fact]
    public void Discover_NoCandidateRootsAtAll_ReturnsEmptyCleanly()
    {
        var log = new DiagnosticRingBuffer(200);
        var results = SteamInstallDiscovery.Discover(Array.Empty<string>(), log);

        Assert.Empty(results);
    }

    [Fact]
    public void GetCandidateSteamRoots_BothProgramFilesRootsSupplied_ReturnsBothSteamSubfolders()
    {
        var candidates = SteamInstallDiscovery.GetCandidateSteamRoots(@"C:\Program Files (x86)", @"C:\Program Files");

        Assert.Equal(
            new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" },
            candidates);
    }

    [Fact]
    public void GetCandidateSteamRoots_NullProgramFilesValues_ReturnsEmpty()
    {
        var candidates = SteamInstallDiscovery.GetCandidateSteamRoots(null, null);
        Assert.Empty(candidates);
    }
}
