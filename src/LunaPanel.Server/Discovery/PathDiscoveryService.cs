using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Every environment-dependent input the whole discovery pass needs, bundled
/// so a test can substitute a synthetic temp-directory tree for all of it at
/// once, with no dependence on the machine actually running the test. In
/// production, every field here is built from real, already-resolved
/// environment values (special folders, environment variables) - the
/// resolving happens at the call site, never inside <see cref="PathDiscoveryService"/>
/// itself, so this type and everything it calls stays a pure function of its
/// inputs.
/// </summary>
/// <param name="GraphicsConfigurationOverridePath">
/// Elite's player-set <c>GraphicsConfigurationOverride.xml</c> - the third
/// step of <c>HudThemeResolver</c>'s resolution chain (see
/// <c>ref/docs/theme.md</c>). A best-guess conventional path
/// (<c>Options\Graphics\GraphicsConfigurationOverride.xml</c>, alongside the
/// already-confirmed <c>Options\Bindings</c> convention <see cref="BindingsDirectory"/>
/// uses), not one this project has verified against a real install the way
/// <see cref="BindingsDirectory"/> and <see cref="EdhmSettingsJsonPath"/>
/// have been - same treatment as <see cref="FrontierDefaultInstallRoot"/>:
/// if it's wrong, reading it degrades to a clean "not found" and the theme
/// resolver simply falls through to its next step, which is this
/// subsystem's designed-for behaviour either way. Optional (defaults to
/// <see langword="null"/>) so every existing caller that builds this record
/// without it keeps compiling.
/// </param>
/// <param name="StatusJsonDirectory">
/// The directory Elite Dangerous writes its live-state file to - the
/// conventional <c>Saved Games\Frontier Developments\Elite Dangerous\</c>
/// location under the player's profile (see <see cref="StatusJsonDiscovery"/>).
/// Optional (defaults to <see langword="null"/>), same treatment as
/// <see cref="GraphicsConfigurationOverridePath"/>, so every existing caller
/// that builds this record without it keeps compiling.
/// </param>
/// <param name="EliteInstallPathOverride">
/// A commander-chosen install root (<c>PathOverrideStore</c>, set via the
/// Tray app's About window), used in place of the Steam/Epic/Frontier sweep
/// when set. A manual pick means the commander is telling LunaPanel exactly
/// where the install is - discovery does not also merge in whatever
/// auto-detect finds elsewhere. Optional (defaults to <see langword="null"/>),
/// same treatment as the two fields above, so every existing caller that
/// builds this record without it keeps compiling.
/// </param>
public sealed record PathDiscoveryEnvironment(
    IReadOnlyList<string> SteamRoots,
    string EpicManifestsDirectory,
    string? FrontierRegistryInstallPath,
    string FrontierDefaultInstallRoot,
    string BindingsDirectory,
    string EdhmSettingsJsonPath,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string LocalAppData,
    string? GraphicsConfigurationOverridePath = null,
    string? StatusJsonDirectory = null,
    string? EliteInstallPathOverride = null);

/// <param name="BindingsSelection">
/// Which single bindings file the panel should actually read, per
/// <c>ref/docs/bindings-source.md</c>'s "The rule" - distinct from
/// <see cref="Bindings"/>, which only ever reports the highest-versioned
/// file in the Options folder regardless of which preset is actually live.
/// <see cref="LunaPanel.Server.Bindings.LiveBindingsReader"/> reads this
/// field's <see cref="PresetSelectionResult.SelectedFilePath"/>, never
/// <see cref="Bindings"/>'s own path directly.
/// </param>
public sealed record PathDiscoveryResult(
    IReadOnlyList<EliteInstallation> EliteInstallations,
    BindingsDiscoveryResult Bindings,
    PresetSelectionResult BindingsSelection,
    EdhmDiscoveryResult Edhm,
    LunaPanelDirectoryLayout LunaPanelDirectories,
    StatusJsonDiscoveryResult StatusJson);

/// <summary>
/// The single entry point that runs the whole path-discovery sweep: Elite
/// Dangerous (Steam/Epic/Frontier, both editions), the bindings folder,
/// EDHM-UI, and LunaPanel's own directories. "Not found" is a first-class,
/// clean result at every stage here, never an exception - running this
/// against a completely empty environment (no game, no Steam, no EDHM)
/// returns a fully populated, all-empty/null result, not a thrown error.
/// </summary>
public static class PathDiscoveryService
{
    public static PathDiscoveryResult Discover(PathDiscoveryEnvironment environment, IDiagnosticLog log)
    {
        var installations = new List<EliteInstallation>();
        if (environment.EliteInstallPathOverride is not null)
        {
            // A manual pick replaces the whole storefront sweep rather than
            // adding to it - see PathDiscoveryEnvironment.EliteInstallPathOverride's
            // own remarks on why merging would second-guess the commander.
            installations.AddRange(EliteProductScanner.Scan(environment.EliteInstallPathOverride, EliteSource.Manual, log));
        }
        else
        {
            installations.AddRange(SteamInstallDiscovery.Discover(environment.SteamRoots, log));
            installations.AddRange(EpicInstallDiscovery.Discover(environment.EpicManifestsDirectory, log));
            installations.AddRange(FrontierInstallDiscovery.Discover(
                environment.FrontierRegistryInstallPath, environment.FrontierDefaultInstallRoot, log));
        }

        // The same real install can be reachable through more than one
        // probe (e.g. a Frontier-registry path and a Frontier-default path
        // that happen to coincide) - dedupe by product path so the caller
        // never sees the same install reported twice.
        var deduped = installations
            .GroupBy(installation => installation.ProductPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (deduped.Count == 0)
        {
            log.Info("Discovery", "Elite Dangerous install: not found via Steam, Epic, or Frontier");
        }

        var bindings = BindingsDiscovery.Discover(environment.BindingsDirectory, log);

        // The Odyssey install is the one named in ref/docs/bindings-source.md
        // ("Products\elite-dangerous-odyssey-64\ControlSchemes") - reusing
        // the install already found above rather than adding a second way to
        // locate the game. No Odyssey install found simply means stock
        // resolution is never possible; PresetSelector treats that the same
        // "not found is a first-class result" way every other probe here does.
        var odysseyInstall = deduped.FirstOrDefault(installation => installation.Edition == EliteEdition.Odyssey);
        var controlSchemesDirectory = odysseyInstall is not null
            ? Path.Combine(odysseyInstall.ProductPath, "ControlSchemes")
            : null;
        var bindingsSelection = PresetSelector.Select(environment.BindingsDirectory, controlSchemesDirectory, bindings, log);

        var edhm = EdhmDiscovery.Discover(environment.EdhmSettingsJsonPath, environment.EnvironmentVariables, log);

        var layout = LunaPanelDirectories.Resolve(environment.LocalAppData);
        LunaPanelDirectories.EnsureCreated(layout, log);

        var statusJson = StatusJsonDiscovery.Discover(environment.StatusJsonDirectory, log);

        return new PathDiscoveryResult(deduped, bindings, bindingsSelection, edhm, layout, statusJson);
    }
}
