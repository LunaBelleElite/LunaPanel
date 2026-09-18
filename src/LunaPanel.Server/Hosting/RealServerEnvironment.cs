using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Discovery;
using LunaPanel.Core.Network;
using LunaPanel.Server.Discovery;
using LunaPanel.Server.Http;

namespace LunaPanel.Server.Hosting;

/// <summary>
/// The one place that reads LunaPanel's actual environment - real special
/// folders, real network interfaces, real environment-variable overrides -
/// and turns it into a <see cref="ServerHostOptions"/> that
/// <see cref="ServerHostBuilder"/> can build the app from. Nothing here is
/// unit-tested directly: every test instead builds a synthetic
/// <see cref="ServerHostOptions"/> by hand and hands it straight to
/// <see cref="ServerHostBuilder.Build"/> - the same split
/// <c>ref/docs/discovery.md</c> describes between the tested discovery
/// library and its one real, untested call site.
/// </summary>
public static class RealServerEnvironment
{
    public static ServerHostOptions Build()
    {
        // This project is Windows-only (Steam's registry-recorded install
        // path below, and the Win32 SendInput injector ServerHostBuilder.Build
        // constructs later) but deliberately targets net10.0, not
        // net10.0-windows (same reasoning as Win32KeyInjector's own
        // [SupportedOSPlatform] split - see ref/docs/injection.md). This
        // early-exit guard is what lets this method call
        // Win32SteamRegistryLookup below without a CA1416 warning.
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("LunaPanel.Server requires Windows (Steam/EDHM discovery, Win32 SendInput).");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // The ONE signal for which root LunaPanelDirectories.Resolve uses
        // (see DevBuildDetector's own remarks) - read once here and reused
        // for both the early port-settings read below and the
        // PathDiscoveryEnvironment this method returns, so both agree on
        // the same directories PathDiscoveryService.Discover will use later,
        // exactly as they did before this field existed.
        var isDevBuild = DevBuildDetector.IsDevBuild();

        // The persisted port a commander chose via the tray's Ports dialog
        // (PortSettingsForm, LunaPanel.Tray) - read with a no-op log, since
        // the real diagnostics pipeline is not wired up until after this
        // method returns a directory/redactor for it to be built from.
        var portSettingsDirectory = LunaPanelDirectories.Resolve(localAppData, isDevBuild).LayoutsDirectory;
        var persistedPortSettings = new PortSettingsStore(portSettingsDirectory, new NoOpDiagnosticLog()).Load();

        // A commander's manual fallback for Elite/EDHM discovery (the Tray
        // app's About window, PathOverrideStore) - same one-off
        // NoOpDiagnosticLog pattern as the port settings read just above,
        // since the real diagnostics pipeline doesn't exist yet at this
        // point in startup.
        var pathOverrides = new PathOverrideStore(portSettingsDirectory, new NoOpDiagnosticLog()).Load();

        var port = PortResolution.Resolve(Environment.GetEnvironmentVariable("LUNAPANEL_PORT"), persistedPortSettings);
        var bindAddressOverride = Environment.GetEnvironmentVariable("LUNAPANEL_BIND_ADDRESS");

        // The loopback-only listener the PC's own browser reaches, and the
        // only thing that grants host access (ref/docs/hosting.md). Its
        // default is the device port plus one; LUNAPANEL_HOST_PORT is the way
        // out if that one is occupied.
        var hostAccessPort = HostRequest.ResolveAccessPort(
            Environment.GetEnvironmentVariable("LUNAPANEL_HOST_PORT"), port);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var steamRoots = SteamInstallDiscovery.GetCandidateSteamRoots(
            programFilesX86,
            programFiles,
            Win32SteamRegistryLookup.ReadSteamPathFromCurrentUserRegistry,
            Win32SteamRegistryLookup.ReadInstallPathFromLocalMachineRegistry);
        var epicManifestsDirectory = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        var bindingsDirectory = Path.Combine(localAppData, "Frontier Developments", "Elite Dangerous", "Options", "Bindings");
        var edhmSettingsJsonPath = pathOverrides.EdhmSettingsJsonPath
            ?? Path.Combine(localAppData, "EDHM-UI-V3", "resources", "data", "Settings.json");

        // UNVERIFIED, same treatment as FrontierDefaultInstallRoot below: a
        // best-guess conventional path, not one confirmed against a real
        // install - see PathDiscoveryEnvironment's own remarks on this
        // field. Degrades to a clean "not found" if wrong.
        var graphicsConfigurationOverridePath = Path.Combine(
            localAppData, "Frontier Developments", "Elite Dangerous", "Options", "Graphics", "GraphicsConfigurationOverride.xml");

        // Elite's own live-state directory - a fixed, well-documented
        // location under the player's profile, distinct from anything under
        // %LOCALAPPDATA%. Absence just means the game has never been
        // launched yet - see StatusJsonDiscovery.
        var statusJsonDirectory = Path.Combine(userProfile, "Saved Games", "Frontier Developments", "Elite Dangerous");

        // UNVERIFIED (see ref/docs/discovery.md's FrontierInstallDiscovery
        // remarks): no standalone Frontier launcher install exists on any
        // machine this has been checked against. The registry-recorded path
        // is left null rather than guessed at - reading a specific registry
        // key this project has never confirmed exists would be inventing an
        // interface, not wiring one. FrontierDefaultInstallRoot below is a
        // best-guess conventional path; if it's wrong, Directory.Exists-based
        // discovery degrades it to a clean "not found", which is this
        // subsystem's designed-for behaviour either way.
        string? frontierRegistryInstallPath = null;
        var frontierDefaultInstallRoot = Path.Combine(programFiles, "Frontier", "EDLaunch");

        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = userProfile,
            ["LOCALAPPDATA"] = localAppData,
        };

        var discoveryEnvironment = new PathDiscoveryEnvironment(
            SteamRoots: steamRoots,
            EpicManifestsDirectory: epicManifestsDirectory,
            FrontierRegistryInstallPath: frontierRegistryInstallPath,
            FrontierDefaultInstallRoot: frontierDefaultInstallRoot,
            BindingsDirectory: bindingsDirectory,
            EdhmSettingsJsonPath: edhmSettingsJsonPath,
            EnvironmentVariables: environmentVariables,
            LocalAppData: localAppData,
            IsDevBuild: isDevBuild,
            GraphicsConfigurationOverridePath: graphicsConfigurationOverridePath,
            StatusJsonDirectory: statusJsonDirectory,
            EliteInstallPathOverride: pathOverrides.EliteInstallPath);

        var redactor = new PathRedactor(new (string ActualRoot, string ReplacementToken)[]
        {
            (localAppData, "%LOCALAPPDATA%"),
            (userProfile, "%USERPROFILE%"),
        });

        var candidateAddresses = LanAddressResolver.GetCandidateAddresses();
        var bindAddress = LanAddressResolver.Resolve(candidateAddresses, bindAddressOverride);

        return new ServerHostOptions(
            BindAddress: bindAddress,
            Port: port,
            DiscoveryEnvironment: discoveryEnvironment,
            Redactor: redactor,
            Clock: TimeProvider.System,
            BindCandidates: candidateAddresses,
            HostAccessPort: hostAccessPort);
    }

    /// <summary>
    /// Discards every event. Used only for the one-off <see cref="PortSettingsStore"/>
    /// read this method needs before the real diagnostics pipeline exists -
    /// a corrupt or out-of-range port-settings file still falls back safely
    /// (<see cref="PortSettingsStore.Load"/>'s own guarantee), it just isn't
    /// reported anywhere at this point in startup.
    /// </summary>
    private sealed class NoOpDiagnosticLog : IDiagnosticLog
    {
        public void Write(DiagnosticEvent diagnosticEvent)
        {
        }
    }
}
