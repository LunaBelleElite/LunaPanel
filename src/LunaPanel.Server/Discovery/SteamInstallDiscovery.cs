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
    ///
    /// The two Program-Files guesses cover a default install only - Steam
    /// itself always records its real install location in the registry, so
    /// a Steam install anywhere else (a custom drive, a renamed folder) is
    /// otherwise invisible to this whole probe, no matter which library the
    /// game itself ended up in. <paramref name="readSteamPathFromCurrentUserRegistry"/>
    /// and <paramref name="readInstallPathFromLocalMachineRegistry"/> are the
    /// two registry-backed sources for that, taken as callbacks rather than
    /// read here directly - same reasoning as <c>FrontierInstallDiscovery</c>'s
    /// own registry value (see its remarks): it keeps this method testable
    /// without a real registry, and keeps this project's <c>net10.0</c>
    /// (non-Windows) target framework from reporting a CA1416
    /// platform-compatibility build error. Both default to
    /// <see langword="null"/> (meaning "don't check"), so every existing
    /// caller keeps compiling. Each is called at most once, wrapped so a
    /// missing key/value or any exception it throws degrades to "no
    /// additional candidate" rather than failing discovery.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateSteamRoots(
        string? programFilesX86,
        string? programFiles,
        Func<string?>? readSteamPathFromCurrentUserRegistry = null,
        Func<string?>? readInstallPathFromLocalMachineRegistry = null,
        IDiagnosticLog? log = null)
    {
        var candidates = new List<string>();
        var effectiveLog = log ?? new NoOpDiagnosticLog();

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam"));
        }

        if (!string.IsNullOrEmpty(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Steam"));
        }

        TryAddRegistryCandidate(
            candidates,
            "HKCU Software\\Valve\\Steam SteamPath",
            readSteamPathFromCurrentUserRegistry,
            effectiveLog);
        TryAddRegistryCandidate(
            candidates,
            "HKLM SOFTWARE\\WOW6432Node\\Valve\\Steam InstallPath",
            readInstallPathFromLocalMachineRegistry,
            effectiveLog);

        return candidates;
    }

    private static void TryAddRegistryCandidate(
        List<string> candidates,
        string description,
        Func<string?>? reader,
        IDiagnosticLog log)
    {
        if (reader is null)
        {
            return;
        }

        string? value;
        try
        {
            value = reader();
        }
        catch (Exception ex)
        {
            log.Warn("Discovery", $"Steam registry ({description}): read failed", ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            log.Info("Discovery", $"Steam registry ({description}): not found");
            return;
        }

        // Steam writes SteamPath (and, less commonly, InstallPath) with
        // forward slashes - normalize before this joins paths built with
        // Path.Combine elsewhere in this method/namespace.
        var normalized = value.Replace('/', Path.DirectorySeparatorChar);

        if (candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            log.Info("Discovery", $"Steam registry ({description}): {normalized} -> already a candidate, skipped");
            return;
        }

        log.Info("Discovery", $"Steam registry ({description}): {normalized} -> added as candidate");
        candidates.Add(normalized);
    }

    /// <summary>
    /// Discards every event. Used only when <see cref="GetCandidateSteamRoots"/>
    /// is called without a real log (every existing caller before the
    /// registry lookups above existed, and every test that doesn't care
    /// about log output) - same one-off pattern as
    /// <see cref="Hosting.RealServerEnvironment"/>'s own private no-op log.
    /// </summary>
    private sealed class NoOpDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticEvent diagnosticEvent)
        {
        }
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
