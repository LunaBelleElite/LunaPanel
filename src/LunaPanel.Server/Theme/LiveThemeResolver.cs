using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Theme;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Server.Theme;

/// <summary>
/// Reads whatever theme-related files <see cref="PathDiscoveryService"/>
/// already discovered and hands their content to
/// <see cref="HudThemeResolver.Resolve"/> - the one place in
/// <c>LunaPanel.Server</c> that assembles the resolver's five inputs from
/// real files. Re-reads every call (no caching): a player who tweaks their
/// EDHM theme while LunaPanel is running sees it reflected on their next
/// panel request, no restart required - same freshness discipline as
/// <see cref="Bindings.LiveBindingsReader"/>.
///
/// <b>Elite's <c>GraphicsConfiguration.xml</c> default matrix (resolution
/// step 4) is not wired here</b> - no confirmed real-world path exists for
/// it anywhere in this project (see <c>ref/docs/theme.md</c>), and
/// inventing one would be guessing at an interface, not wiring a known one.
/// Skipping it changes nothing observable when the player hasn't touched
/// their in-game HUD colours: that file's own default content is the
/// identity matrix, which resolves to the exact same stock orange
/// <see cref="HudThemeResolver.Resolve"/> already falls back to when no
/// step is usable at all. It only matters for a player who both (a) runs no
/// EDHM and (b) customised Elite's own default (non-override) graphics
/// config - a narrower case than the override path above, which the docs
/// explicitly record as a best-guess-but-untested path is wired.
/// </summary>
public sealed class LiveThemeResolver
{
    private readonly PathDiscoveryEnvironment _environment;
    private readonly PathDiscoveryResult _discovery;
    private readonly IDiagnosticLog _log;

    public LiveThemeResolver(PathDiscoveryEnvironment environment, PathDiscoveryResult discovery, IDiagnosticLog log)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public HudTheme Resolve()
    {
        var inputs = ReadInputs();
        return HudThemeResolver.Resolve(inputs.ThemeSettingsJson, inputs.IniFilesByName, inputs.XmlProfileIni, inputs.OverrideXml, graphicsConfigurationXml: null, _log);
    }

    /// <summary>
    /// The distinct colours discovered in the commander's live EDHM theme
    /// (<see cref="HudThemeResolver.ExtractDiscoveredColours"/>) - feeds the
    /// settings gear's "From your HUD" picker group
    /// (<c>ref/docs/web-client.md</c>). Re-reads the same files as
    /// <see cref="Resolve"/> on every call (no caching, same freshness
    /// discipline). Empty, never <see langword="null"/>, when there is no
    /// usable EDHM theme at all.
    /// </summary>
    public IReadOnlyList<EdhmDiscoveredColour> GetDiscoveredColours()
    {
        var inputs = ReadInputs();
        return HudThemeResolver.ExtractDiscoveredColours(inputs.ThemeSettingsJson, inputs.IniFilesByName);
    }

    private (string? ThemeSettingsJson, Dictionary<string, string> IniFilesByName, string? XmlProfileIni, string? OverrideXml) ReadInputs()
    {
        var edition = _discovery.Edhm.Editions.FirstOrDefault(e =>
            string.Equals(e.EditionFolderName, _discovery.Edhm.ActiveInstance, StringComparison.OrdinalIgnoreCase));
        edition ??= _discovery.Edhm.Editions.FirstOrDefault();

        var themeSettingsJson = edition?.ThemeSettingsJsonPath is { } themeSettingsPath ? TryRead(themeSettingsPath) : null;
        var xmlProfileIni = edition?.XmlProfileIniPath is { } xmlProfilePath ? TryRead(xmlProfilePath) : null;

        var iniFilesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (edition?.IniDirectory is { } iniDirectory && Directory.Exists(iniDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(iniDirectory, "*.ini"))
            {
                if (TryRead(file) is { } content)
                {
                    iniFilesByName[Path.GetFileNameWithoutExtension(file)] = content;
                }
            }
        }

        var overrideXml = _environment.GraphicsConfigurationOverridePath is { } overridePath ? TryRead(overridePath) : null;

        return (themeSettingsJson, iniFilesByName, xmlProfileIni, overrideXml);
    }

    private string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Theme", "A theme-related file could not be read for a live panel request", ex.GetType().Name);
            return null;
        }
    }
}
