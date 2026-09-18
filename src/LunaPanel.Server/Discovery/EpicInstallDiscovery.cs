using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Finds Elite Dangerous installed via the Epic Games launcher, by reading
/// its per-app manifest <c>.item</c> files (JSON) under
/// <c>%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\</c>.
///
/// UNVERIFIED: there is no Epic install of Elite Dangerous on the machine
/// this was authored on, so neither the manifest folder location, the
/// <c>.item</c> field names below, nor the assumption that Epic's install
/// also nests a <c>Products/</c> folder under <c>InstallLocation</c> (the
/// way Steam's layout does) have been checked against a real file. Written
/// defensively: any shape mismatch degrades to "not found" rather than
/// throwing.
/// </summary>
public static class EpicInstallDiscovery
{
    public static IReadOnlyList<EliteInstallation> Discover(string manifestsDirectory, IDiagnosticLog log)
    {
        if (!Directory.Exists(manifestsDirectory))
        {
            log.Info("Discovery", $"Epic manifests folder: {manifestsDirectory} -> not found");
            return Array.Empty<EliteInstallation>();
        }

        log.Info("Discovery", $"Epic manifests folder: {manifestsDirectory} -> found");

        var results = new List<EliteInstallation>();
        foreach (var itemFile in Directory.EnumerateFiles(manifestsDirectory, "*.item"))
        {
            string? displayName;
            string? installLocation;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(itemFile));
                displayName = doc.RootElement.TryGetProperty("DisplayName", out var displayNameElement)
                    ? displayNameElement.GetString()
                    : null;
                installLocation = doc.RootElement.TryGetProperty("InstallLocation", out var installLocationElement)
                    ? installLocationElement.GetString()
                    : null;
            }
            catch (JsonException ex)
            {
                log.Warn("Discovery", $"Epic manifest: {itemFile} -> parse failed", ex.Message);
                continue;
            }

            if (displayName is null || !displayName.Contains("Elite Dangerous", StringComparison.OrdinalIgnoreCase))
            {
                log.Info("Discovery", $"Epic manifest: {itemFile} -> not Elite Dangerous ({displayName ?? "no DisplayName"})");
                continue;
            }

            if (string.IsNullOrEmpty(installLocation))
            {
                log.Info("Discovery", $"Epic manifest: {itemFile} -> matched Elite Dangerous but has no InstallLocation");
                continue;
            }

            log.Info("Discovery", $"Epic manifest: {itemFile} -> matched Elite Dangerous, InstallLocation={installLocation}");
            results.AddRange(EliteProductScanner.Scan(installLocation, EliteSource.Epic, log));
        }

        return results;
    }
}
