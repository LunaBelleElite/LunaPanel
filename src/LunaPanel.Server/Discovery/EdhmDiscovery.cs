using System.Text.Json;
using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>One edition's EDHM-applied data folder, if present.</summary>
public sealed record EdhmEditionData(
    string EditionFolderName,
    string IniDirectory,
    string? ThemeSettingsJsonPath,
    string? XmlProfileIniPath);

public sealed record EdhmDiscoveryResult(
    bool SettingsFound,
    string? UserDataFolder,
    string? ActiveInstance,
    IReadOnlyList<EdhmEditionData> Editions)
{
    /// <summary>
    /// Whether a theme is actually resolvable - distinct from
    /// <see cref="SettingsFound"/>, which only means EDHM's fixed
    /// install-time bootstrap <c>Settings.json</c> exists and parses. That
    /// can be true with <see cref="UserDataFolder"/> unresolved, both
    /// edition folders missing, or every edition present but empty - none of
    /// which give the panel anything to actually read a HUD colour from.
    /// This is the property both user-facing "found" indicators
    /// (<c>TrayStatusModel</c>'s "EDHM theme found" row and
    /// <c>AboutForm.CurrentEdhmLabel</c>) should report, so "found" only
    /// ever means a theme could actually be resolved from it.
    /// </summary>
    public bool HasResolvableTheme => Editions.Any(edition => edition.ThemeSettingsJsonPath is not null || edition.XmlProfileIniPath is not null);

    /// <summary>
    /// The one edition <see cref="Theme.LiveThemeResolver"/> reads and
    /// <see cref="Theme.ThemeFileWatcher"/> watches - a single shared
    /// selection so the two can never disagree about which folder is "the
    /// active" one. Prefers the edition named by <see cref="ActiveInstance"/>;
    /// falls back to the first discovered edition (there are only ever two,
    /// <c>ODYSS</c>/<c>HORIZ</c>) when <see cref="ActiveInstance"/> names
    /// none of them, or names none at all.
    /// </summary>
    public EdhmEditionData? SelectActiveEdition() =>
        Editions.FirstOrDefault(e => string.Equals(e.EditionFolderName, ActiveInstance, StringComparison.OrdinalIgnoreCase))
        ?? Editions.FirstOrDefault();
}

/// <summary>
/// Finds EDHM-UI-V3's own data, if the mod is installed at all - absent is a
/// normal outcome (most players don't run it), not an error.
///
/// EDHM-UI-V3's own settings file lives at a fixed, version-stable location
/// (<c>%LOCALAPPDATA%\EDHM-UI-V3\resources\data\Settings.json</c>, confirmed
/// on the authoring machine) and records where the mod actually keeps its
/// live per-edition data, via a <c>UserDataFolder</c> field - which on the
/// authoring machine was a real, non-default path
/// (<c>C:\Users\&lt;user&gt;\EDHM_UI</c>, not anywhere under
/// <c>%LOCALAPPDATA%</c>), confirming this must be read, never assumed. That
/// same field can also be found holding an unexpanded
/// <c>%USERPROFILE%\EDHM_UI</c>-style placeholder (the shape shipped in the
/// mod's own factory-default settings template) - see
/// <see cref="PlaceholderExpander"/>.
///
/// Under the resolved <c>UserDataFolder</c>, data splits by edition into an
/// <c>ODYSS</c> and a <c>HORIZ</c> folder (both confirmed real folder names
/// on the authoring machine, though only <c>ODYSS</c> was actually
/// populated there - the player only plays Odyssey). Each, when present,
/// holds an <c>EDHM\EDHM-Ini\</c> folder with the active theme's
/// <c>ThemeSettings.json</c> and <c>XML-Profile.ini</c> among other INI
/// files - both confirmed present under the authoring machine's populated
/// <c>ODYSS</c> folder.
///
/// <b>The fixed install-time file is only ever the bootstrap pointer, never
/// the last word (O17, closed 2026-09-17).</b> EDHM never rewrites it after
/// install, so it can go on naming a <c>UserDataFolder</c> the commander has
/// since moved away from. The file EDHM actually keeps current lives under
/// that folder itself, at <c>&lt;UserDataFolder&gt;\Settings.json</c> - once
/// that exists, its own <c>UserDataFolder</c>/<c>ActiveInstance</c> are
/// re-read and take over, so a moved folder is followed rather than missed.
/// This still cannot recover a folder moved so completely that no trace is
/// reachable from the install-time pointer at all - that residual limit is
/// accepted, not solved, since nothing short of EDHM itself changing where
/// it writes could close it further.
/// </summary>
public static class EdhmDiscovery
{
    private static readonly string[] EditionFolderNames = { "ODYSS", "HORIZ" };

    public static EdhmDiscoveryResult Discover(
        string settingsJsonPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        IDiagnosticLog log)
    {
        if (!File.Exists(settingsJsonPath))
        {
            log.Info("Discovery", $"EDHM-UI settings: {settingsJsonPath} -> not found");
            return new EdhmDiscoveryResult(false, null, null, Array.Empty<EdhmEditionData>());
        }

        log.Info("Discovery", $"EDHM-UI settings: {settingsJsonPath} -> found");

        string? rawUserDataFolder;
        string? activeInstance;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsJsonPath));
            rawUserDataFolder = doc.RootElement.TryGetProperty("UserDataFolder", out var userDataFolderElement)
                ? userDataFolderElement.GetString()
                : null;
            activeInstance = doc.RootElement.TryGetProperty("ActiveInstance", out var activeInstanceElement)
                ? activeInstanceElement.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            log.Warn("Discovery", $"EDHM-UI settings: {settingsJsonPath} -> parse failed", ex.Message);
            return new EdhmDiscoveryResult(false, null, null, Array.Empty<EdhmEditionData>());
        }

        if (string.IsNullOrEmpty(rawUserDataFolder))
        {
            log.Info("Discovery", "EDHM-UI settings: UserDataFolder not recorded");
            return new EdhmDiscoveryResult(true, null, activeInstance, Array.Empty<EdhmEditionData>());
        }

        var userDataFolder = PlaceholderExpander.Expand(rawUserDataFolder, environmentVariables);
        log.Info("Discovery", $"EDHM-UI UserDataFolder: {rawUserDataFolder} -> resolved to {userDataFolder}");

        // [2026-09-17, O17 closed] settingsJsonPath is EDHM's own install-time
        // settings file, never rewritten after install - it can name a
        // UserDataFolder the commander has since moved away from. The file
        // EDHM actually writes to live under that folder itself
        // (<UserDataFolder>\Settings.json), so once it exists it is a more
        // current record than the one that pointed us here - re-read it and
        // let its own UserDataFolder/ActiveInstance win. Fall through to what
        // the install-time file already said when the live copy is absent,
        // unreadable, or empty - the shipped copy IS the live copy on a
        // fresh install where EDHM has never rewritten it at its own
        // location yet.
        var liveSettingsJsonPath = Path.Combine(userDataFolder, "Settings.json");
        if (!string.Equals(Path.GetFullPath(liveSettingsJsonPath), Path.GetFullPath(settingsJsonPath), StringComparison.OrdinalIgnoreCase)
            && File.Exists(liveSettingsJsonPath))
        {
            try
            {
                using var liveDoc = JsonDocument.Parse(File.ReadAllText(liveSettingsJsonPath));
                var liveRawUserDataFolder = liveDoc.RootElement.TryGetProperty("UserDataFolder", out var liveUserDataFolderElement)
                    ? liveUserDataFolderElement.GetString()
                    : null;

                if (!string.IsNullOrEmpty(liveRawUserDataFolder))
                {
                    activeInstance = liveDoc.RootElement.TryGetProperty("ActiveInstance", out var liveActiveInstanceElement)
                        ? liveActiveInstanceElement.GetString()
                        : activeInstance;
                    userDataFolder = PlaceholderExpander.Expand(liveRawUserDataFolder, environmentVariables);
                    log.Info("Discovery", $"EDHM-UI live settings at {liveSettingsJsonPath} -> UserDataFolder resolved to {userDataFolder}");
                }
            }
            catch (JsonException ex)
            {
                log.Warn("Discovery", $"EDHM-UI live settings: {liveSettingsJsonPath} -> parse failed, keeping the install-time settings' value", ex.Message);
            }
        }

        var editions = new List<EdhmEditionData>();
        foreach (var editionFolderName in EditionFolderNames)
        {
            var iniDirectory = Path.Combine(userDataFolder, editionFolderName, "EDHM", "EDHM-Ini");
            if (!Directory.Exists(iniDirectory))
            {
                log.Info("Discovery", $"EDHM-UI {editionFolderName} data: {iniDirectory} -> not found");
                continue;
            }

            log.Info("Discovery", $"EDHM-UI {editionFolderName} data: {iniDirectory} -> found");

            var themeSettingsPath = Path.Combine(iniDirectory, "ThemeSettings.json");
            var xmlProfilePath = Path.Combine(iniDirectory, "XML-Profile.ini");

            editions.Add(new EdhmEditionData(
                editionFolderName,
                iniDirectory,
                File.Exists(themeSettingsPath) ? themeSettingsPath : null,
                File.Exists(xmlProfilePath) ? xmlProfilePath : null));
        }

        return new EdhmDiscoveryResult(true, userDataFolder, activeInstance, editions);
    }
}
