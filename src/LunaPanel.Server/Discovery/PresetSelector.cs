using System.Globalization;
using System.Text.RegularExpressions;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>Which of the two places (<c>ref/docs/bindings-source.md</c>) a resolved candidate file actually came from.</summary>
public enum PresetOrigin
{
    CommanderAuthored,
    Stock,
}

/// <summary>Which step of the rule actually produced the final selection.</summary>
public enum PresetSelectionMethod
{
    /// <summary>Step 3: the first <c>StartPreset</c> name that resolved to a commander-authored file.</summary>
    CommanderAuthored,

    /// <summary>Step 3: no name resolved to a commander-authored file, so the first that resolved to a stock file won.</summary>
    Stock,

    /// <summary>Step 4: <c>StartPreset</c> is missing, empty, or named nothing that resolved to any file - today's highest-version-in-the-Options-folder behaviour, flagged rather than silent.</summary>
    Fallback,
}

/// <summary>
/// The outcome of applying <c>ref/docs/bindings-source.md</c>'s "The rule"
/// (steps 1-5). <see cref="SelectedFilePath"/> is <see langword="null"/> only
/// when nothing resolved at all, even by fallback - the "commander who has
/// never rebound anything and has no stock scheme file anywhere" case, which
/// stays an honest "not found" rather than inventing a path.
/// </summary>
/// <param name="SelectedFilePath">The chosen file, or <see langword="null"/> if nothing resolved.</param>
/// <param name="SelectedPresetName">The <c>StartPreset</c> line that drove the selection (steps 1-3), or <see langword="null"/> on <see cref="PresetSelectionMethod.Fallback"/>, which is not driven by any such name.</param>
/// <param name="Method">Which step of the rule produced this result.</param>
/// <param name="Origin">Whether the chosen file is the commander's own or shipped with the game - <see langword="null"/> only when <see cref="SelectedFilePath"/> is itself <see langword="null"/>.</param>
/// <param name="Version">The commander file's <c>major.minor</c>, or <see langword="null"/> for a stock file (which carries no version at all) or when nothing was found.</param>
/// <param name="PresetNameMismatch">Step 5: <see langword="true"/> when the chosen file's own <c>PresetName</c> attribute does not match <see cref="SelectedPresetName"/>. Always <see langword="false"/> on <see cref="PresetSelectionMethod.Fallback"/>, since no name drove that choice to compare against.</param>
/// <param name="ActualPresetNameInFile">The chosen file's own parsed <c>PresetName</c>, read once during selection for the step-5 check - <see langword="null"/> when nothing was found, verification wasn't attempted (Fallback), or the file couldn't be read/parsed.</param>
public sealed record PresetSelectionResult(
    string? SelectedFilePath,
    string? SelectedPresetName,
    PresetSelectionMethod Method,
    PresetOrigin? Origin,
    BindsVersion? Version,
    bool PresetNameMismatch,
    string? ActualPresetNameInFile);

/// <summary>
/// Implements <c>ref/docs/bindings-source.md</c>'s "The rule" end to end.
/// Fixes the three defects the day's measurement found in the old "highest
/// version in the Options folder" rule: a commander who never rebinds (no
/// file in Options at all) is now matched against the install's shipped
/// <c>ControlSchemes</c> file instead of going dead; two same-version
/// commander presets no longer tie on enumeration order, since
/// <c>StartPreset</c>'s own line order breaks the tie; and the preset name
/// <c>StartPreset</c> actually names is read and verified against the file
/// chosen, rather than being captured by a regex group and discarded.
///
/// All file I/O here stays in <c>LunaPanel.Server</c> - the needle-guard test
/// forbidding <c>LunaPanel.Core</c> from discovering a filesystem path (see
/// <c>diagnostics.md</c>) applies to this class the same as every other type
/// under <c>Discovery/</c>. Step 5's verification hands already-read XML text
/// to <see cref="BindingsFile.Parse"/> (a pure parser, no discovery of its
/// own) purely to read the chosen file's own <c>PresetName</c> attribute -
/// exactly the same split <c>LiveBindingsReader</c> already uses for the full
/// parse.
/// </summary>
public static class PresetSelector
{
    // Same shape as BindingsDiscovery's own regex (kept independent rather
    // than shared, since this one also needs a per-NAME lookup, not just a
    // single overall newest-file scan).
    private static readonly Regex BindsFileRegex =
        new(@"^(?<preset>[^.]+)\.(?<major>\d+)\.(?<minor>\d+)\.binds$", RegexOptions.Compiled);

    private static readonly Regex StartPresetFileRegex =
        new(@"^StartPreset\.(?<n>\d+)\.start$", RegexOptions.Compiled);

    private sealed record ResolvedCandidate(string FilePath, PresetOrigin Origin, BindsVersion? Version);

    /// <param name="bindingsDirectory">The commander's own <c>Options\Bindings</c> folder.</param>
    /// <param name="controlSchemesDirectory">The install's <c>Products\elite-dangerous-odyssey-64\ControlSchemes</c> folder, or <see langword="null"/> when no Odyssey install was found - stock resolution is then simply never possible, matching "not found is a first-class result" throughout this subsystem.</param>
    /// <param name="bindingsDiscovery">Already-computed today's-rule result, reused for step 4's fallback rather than re-scanning the directory a second time.</param>
    public static PresetSelectionResult Select(
        string bindingsDirectory,
        string? controlSchemesDirectory,
        BindingsDiscoveryResult bindingsDiscovery,
        IDiagnosticLog log)
    {
        var startPresetNames = ReadHighestStartPresetNames(bindingsDirectory, log);

        if (startPresetNames.Count > 0)
        {
            var candidates = startPresetNames
                .Select(name => (Name: name, Resolved: ResolveCandidate(name, bindingsDirectory, controlSchemesDirectory)))
                .ToList();

            var commander = candidates.FirstOrDefault(c => c.Resolved is { Origin: PresetOrigin.CommanderAuthored });
            if (commander.Resolved is not null)
            {
                return Verify(commander.Name, commander.Resolved, PresetSelectionMethod.CommanderAuthored, log);
            }

            var stock = candidates.FirstOrDefault(c => c.Resolved is { Origin: PresetOrigin.Stock });
            if (stock.Resolved is not null)
            {
                return Verify(stock.Name, stock.Resolved, PresetSelectionMethod.Stock, log);
            }

            log.Info(
                "Discovery",
                $"Preset selection: StartPreset named {startPresetNames.Count} preset(s), none resolved to a file -> falling back to highest version");
        }
        else
        {
            log.Info("Discovery", "Preset selection: no StartPreset file found, or it named nothing -> falling back to highest version");
        }

        // Step 4.
        if (bindingsDiscovery.LatestBindsFilePath is null)
        {
            log.Info("Discovery", "Preset selection: fallback found nothing either -> no bindings file usable at all");
            return new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null);
        }

        log.Info(
            "Discovery",
            $"Preset selection: fallback -> {bindingsDiscovery.LatestBindsFilePath} (version {bindingsDiscovery.LatestVersion})");
        return new PresetSelectionResult(
            bindingsDiscovery.LatestBindsFilePath,
            null,
            PresetSelectionMethod.Fallback,
            PresetOrigin.CommanderAuthored,
            bindingsDiscovery.LatestVersion,
            false,
            null);
    }

    /// <summary>Step 1: the highest-numbered <c>StartPreset.&lt;n&gt;.start</c>'s lines, distinct, first appearance preserved.</summary>
    private static List<string> ReadHighestStartPresetNames(string bindingsDirectory, IDiagnosticLog log)
    {
        if (!Directory.Exists(bindingsDirectory))
        {
            return new List<string>();
        }

        string? bestPath = null;
        var bestN = -1;

        foreach (var file in Directory.EnumerateFiles(bindingsDirectory))
        {
            var match = StartPresetFileRegex.Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            var n = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (n > bestN)
            {
                bestN = n;
                bestPath = file;
            }
        }

        if (bestPath is null)
        {
            return new List<string>();
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(bestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("Discovery", $"StartPreset file could not be read: {bestPath}", ex.GetType().Name);
            return new List<string>();
        }

        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (seen.Add(line))
            {
                distinct.Add(line);
            }
        }

        return distinct;
    }

    /// <summary>Step 2: resolve one candidate name to a file, commander's own preferred over stock.</summary>
    private static ResolvedCandidate? ResolveCandidate(string name, string bindingsDirectory, string? controlSchemesDirectory)
    {
        BindsVersion? bestVersion = null;
        string? bestPath = null;

        if (Directory.Exists(bindingsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(bindingsDirectory))
            {
                var match = BindsFileRegex.Match(Path.GetFileName(file));
                if (!match.Success || !string.Equals(match.Groups["preset"].Value, name, StringComparison.Ordinal))
                {
                    continue;
                }

                var version = new BindsVersion(
                    int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture));

                var isNewer = bestVersion is null
                    || version.Major > bestVersion.Major
                    || (version.Major == bestVersion.Major && version.Minor > bestVersion.Minor);

                if (isNewer)
                {
                    bestVersion = version;
                    bestPath = file;
                }
            }
        }

        if (bestPath is not null)
        {
            return new ResolvedCandidate(bestPath, PresetOrigin.CommanderAuthored, bestVersion);
        }

        if (controlSchemesDirectory is not null)
        {
            var stockPath = Path.Combine(controlSchemesDirectory, $"{name}.binds");
            if (File.Exists(stockPath))
            {
                return new ResolvedCandidate(stockPath, PresetOrigin.Stock, null);
            }
        }

        return null;
    }

    /// <summary>Step 5: read the chosen file once and compare its own <c>PresetName</c> against the name that selected it.</summary>
    private static PresetSelectionResult Verify(string selectingName, ResolvedCandidate resolved, PresetSelectionMethod method, IDiagnosticLog log)
    {
        string? actualPresetName = null;
        var mismatch = false;

        try
        {
            var xml = File.ReadAllText(resolved.FilePath);
            var parsed = BindingsFile.Parse(xml);
            if (parsed.Success)
            {
                actualPresetName = parsed.File!.PresetName;
                mismatch = !string.Equals(actualPresetName, selectingName, StringComparison.Ordinal);
            }
            else
            {
                log.Warn("Discovery", $"Preset selection: chosen file could not be parsed for PresetName verification: {resolved.FilePath}", parsed.Error);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("Discovery", $"Preset selection: chosen file could not be read for PresetName verification: {resolved.FilePath}", ex.GetType().Name);
        }

        if (mismatch)
        {
            log.Warn(
                "Discovery",
                $"Preset selection: chosen file's PresetName ('{actualPresetName}') does not match the selecting StartPreset name ('{selectingName}')",
                resolved.FilePath);
        }
        else
        {
            log.Info(
                "Discovery",
                $"Preset selection: '{selectingName}' -> {resolved.FilePath} ({method}, version {resolved.Version?.ToString() ?? "none"})");
        }

        return new PresetSelectionResult(resolved.FilePath, selectingName, method, resolved.Origin, resolved.Version, mismatch, actualPresetName);
    }
}
