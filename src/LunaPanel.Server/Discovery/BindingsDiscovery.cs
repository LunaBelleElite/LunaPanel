using System.Globalization;
using System.Text.RegularExpressions;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>One <c>Custom.&lt;major&gt;.&lt;minor&gt;.binds</c> file's parsed version.</summary>
public sealed record BindsVersion(int Major, int Minor)
{
    public override string ToString() => $"{Major}.{Minor}";
}

public sealed record BindingsDiscoveryResult(
    string? LatestBindsFilePath,
    BindsVersion? LatestVersion,
    IReadOnlyList<string> StartPresetFilePaths);

/// <summary>
/// Finds the player's active binds file under
/// <c>%LOCALAPPDATA%\Frontier Developments\Elite Dangerous\Options\Bindings\</c>.
/// Elite writes one <c>&lt;preset&gt;.&lt;major&gt;.&lt;minor&gt;.binds</c> file per
/// bindings version it has ever written under the current preset name, plus
/// a same-named <c>.&lt;epoch&gt;.backup</c> copy of the previous one on each
/// rewrite - the newest real file must be chosen by comparing
/// <c>(major, minor)</c> as integers, not by name or by mtime, since a
/// lexical string sort would rank <c>"4.9"</c> above <c>"4.10"</c>.
///
/// Also locates <c>StartPreset.&lt;n&gt;.start</c> files, but does not
/// interpret their contents - <see cref="PresetSelector"/> is what actually
/// reads them (see <c>ref/docs/bindings-source.md</c>'s "The rule"), and is
/// what the panel now reads a bindings file's path through
/// (<c>PathDiscoveryResult.BindingsSelection</c>). This type's own
/// <see cref="BindingsDiscoveryResult.LatestBindsFilePath"/>/<see cref="BindingsDiscoveryResult.LatestVersion"/>
/// - "just take the highest version in the folder", with no regard for
/// which preset is actually live - now serves only as <see cref="PresetSelector"/>'s
/// step-4 fallback, not as the panel's primary source. What exactly each
/// line of a <c>StartPreset</c> file selects (it appears to name one preset
/// per input category - keyboard/HOTAS/gamepad - rather than a single active
/// preset) remains an open question the rule deliberately does not depend on
/// - see <c>ref/docs/bindings-source.md</c>'s "Unproven".
/// </summary>
public static class BindingsDiscovery
{
    // Anchored at both ends deliberately: "Custom.4.3.binds.1574007660.backup"
    // must NOT match, since a backup file has trailing characters after
    // ".binds" that this pattern's closing "$" refuses to allow.
    private static readonly Regex BindsFileRegex =
        new(@"^(?<preset>[^.]+)\.(?<major>\d+)\.(?<minor>\d+)\.binds$", RegexOptions.Compiled);

    private static readonly Regex StartPresetFileRegex =
        new(@"^StartPreset\.\d+\.start$", RegexOptions.Compiled);

    public static BindingsDiscoveryResult Discover(string bindingsDirectory, IDiagnosticLog log)
    {
        if (!Directory.Exists(bindingsDirectory))
        {
            log.Info("Discovery", $"Bindings folder: {bindingsDirectory} -> not found");
            return new BindingsDiscoveryResult(null, null, Array.Empty<string>());
        }

        log.Info("Discovery", $"Bindings folder: {bindingsDirectory} -> found");

        string? latestPath = null;
        BindsVersion? latestVersion = null;
        var startPresetFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(bindingsDirectory))
        {
            var name = Path.GetFileName(file);

            var bindsMatch = BindsFileRegex.Match(name);
            if (bindsMatch.Success)
            {
                var major = int.Parse(bindsMatch.Groups["major"].Value, CultureInfo.InvariantCulture);
                var minor = int.Parse(bindsMatch.Groups["minor"].Value, CultureInfo.InvariantCulture);
                var version = new BindsVersion(major, minor);
                log.Info("Discovery", $"Binds file: {file} -> version {version}");

                var isNewer = latestVersion is null
                    || version.Major > latestVersion.Major
                    || (version.Major == latestVersion.Major && version.Minor > latestVersion.Minor);

                if (isNewer)
                {
                    latestVersion = version;
                    latestPath = file;
                }

                continue;
            }

            if (StartPresetFileRegex.IsMatch(name))
            {
                log.Info("Discovery", $"Start preset file: {file} -> found");
                startPresetFiles.Add(file);
                continue;
            }

            log.Debug("Discovery", $"Bindings folder entry: {file} -> ignored (not a recognized binds or start-preset file)");
        }

        if (latestPath is not null)
        {
            log.Info("Discovery", $"Bindings folder: newest binds file selected -> {latestPath} (version {latestVersion})");
        }
        else
        {
            log.Info("Discovery", $"Bindings folder: {bindingsDirectory} -> no .binds files found");
        }

        return new BindingsDiscoveryResult(latestPath, latestVersion, startPresetFiles);
    }
}
