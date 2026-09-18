using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Given an Elite Dangerous install root (the folder that directly contains
/// a <c>Products/</c> subfolder - true for the Steam layout, and assumed for
/// Epic/Frontier since both distribute the same Frontier-built client), scans
/// each product subfolder and identifies its edition by the executable it
/// actually contains. This is the one piece shared by all three storefront
/// discoverers, so the edition rule only needs stating once.
/// </summary>
public static class EliteProductScanner
{
    public static IReadOnlyList<EliteInstallation> Scan(string eliteInstallRoot, EliteSource source, IDiagnosticLog log)
    {
        var productsDirectory = Path.Combine(eliteInstallRoot, "Products");
        if (!Directory.Exists(productsDirectory))
        {
            log.Info("Discovery", $"Elite Products folder ({source}): {productsDirectory} -> not found");
            return Array.Empty<EliteInstallation>();
        }

        log.Info("Discovery", $"Elite Products folder ({source}): {productsDirectory} -> found");

        var results = new List<EliteInstallation>();
        foreach (var productDirectory in Directory.EnumerateDirectories(productsDirectory))
        {
            if (File.Exists(Path.Combine(productDirectory, "EliteDangerous64.exe")))
            {
                log.Info("Discovery", $"Elite product ({source}): {productDirectory} -> Odyssey");
                results.Add(new EliteInstallation(EliteEdition.Odyssey, productDirectory, source));
            }
            else if (File.Exists(Path.Combine(productDirectory, "EliteDangerous32.exe")))
            {
                log.Info("Discovery", $"Elite product ({source}): {productDirectory} -> Horizons");
                results.Add(new EliteInstallation(EliteEdition.Horizons, productDirectory, source));
            }
            else
            {
                log.Info(
                    "Discovery",
                    $"Elite product ({source}): {productDirectory} -> not recognized (no EliteDangerous64.exe or EliteDangerous32.exe)");
            }
        }

        return results;
    }
}
