using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Finds Elite Dangerous installs recorded by Steam. Steam's own library
/// list can span any number of drives, recorded in each Steam install's
/// <c>steamapps/libraryfolders.vdf</c> (see <see cref="SteamKeyValues"/>
/// for the file format, confirmed against a real file on the authoring
/// machine). A library entry pointing at a path that no longer exists (an
/// unplugged drive, a removed library) is skipped, not treated as a search
/// failure - Steam itself does not delete a library's ledger entry just
/// because the drive is temporarily absent.
/// </summary>
public static class SteamInstallDiscovery
{
    /// <summary>
    /// Candidate Steam install roots, built from already-resolved
    /// environment folder values (never discovered here - see this
    /// namespace's own README-equivalent, <c>ref/docs/discovery.md</c>, and
    /// <c>LunaPanel.Core</c>'s discipline this deliberately does NOT extend
    /// to <c>LunaPanel.Server</c>, which is the one place allowed to know
    /// these roots).
    /// </summary>
    public static IReadOnlyList<string> GetCandidateSteamRoots(string? programFilesX86, string? programFiles)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam"));
        }

        if (!string.IsNullOrEmpty(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Steam"));
        }

        return candidates;
    }

    public static IReadOnlyList<EliteInstallation> Discover(IEnumerable<string> candidateSteamRoots, IDiagnosticLog log)
    {
        var results = new List<EliteInstallation>();

        foreach (var steamRoot in candidateSteamRoots)
        {
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                log.Info("Discovery", $"Steam libraryfolders.vdf: {vdfPath} -> not found");
                continue;
            }

            log.Info("Discovery", $"Steam libraryfolders.vdf: {vdfPath} -> found");

            IReadOnlyList<string> libraryPaths;
            try
            {
                libraryPaths = SteamLibraryFoldersParser.ExtractLibraryPaths(File.ReadAllText(vdfPath));
            }
            catch (Exception ex)
            {
                log.Warn("Discovery", $"Steam libraryfolders.vdf: {vdfPath} -> parse failed", ex.Message);
                continue;
            }

            foreach (var libraryPath in libraryPaths)
            {
                if (!Directory.Exists(libraryPath))
                {
                    log.Info("Discovery", $"Steam library: {libraryPath} -> missing, skipped");
                    continue;
                }

                log.Info("Discovery", $"Steam library: {libraryPath} -> found");

                var eliteRoot = Path.Combine(libraryPath, "steamapps", "common", "Elite Dangerous");
                results.AddRange(EliteProductScanner.Scan(eliteRoot, EliteSource.Steam, log));
            }
        }

        return results;
    }
}
