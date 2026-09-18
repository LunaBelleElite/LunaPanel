using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>LunaPanel's own on-disk locations, all rooted under one folder.</summary>
public sealed record LunaPanelDirectoryLayout(
    string LogsDirectory,
    string LayoutsDirectory,
    string DeviceRegistryFilePath);

/// <summary>
/// Resolves and creates LunaPanel's own directories - never conditional on
/// anything being found elsewhere. These exist and are usable even with no
/// Elite install, no bindings, and no EDHM-UI present at all, since the
/// diagnostics view and pairing screen must work without a game (see this
/// task's brief).
/// </summary>
public static class LunaPanelDirectories
{
    /// <param name="isDevBuild">
    /// From <see cref="DevBuildDetector.IsDevBuild"/> (or a
    /// <see cref="PathDiscoveryEnvironment.IsDevBuild"/> already carrying that
    /// value) - the ONE signal for which root to use. When
    /// <see langword="true"/>, the root becomes <c>LunaPanel-Dev</c> instead
    /// of <c>LunaPanel</c>, so a dev launch used to test changes never
    /// collides with a real install's own Logs/Layouts/Pairing state.
    /// <see langword="false"/> reproduces this method's behaviour from
    /// before this parameter existed, byte-for-byte - the invariant a real
    /// install's build must never regress.
    /// </param>
    public static LunaPanelDirectoryLayout Resolve(string localAppData, bool isDevBuild)
    {
        var root = Path.Combine(localAppData, isDevBuild ? "LunaPanel-Dev" : "LunaPanel");
        return new LunaPanelDirectoryLayout(
            LogsDirectory: Path.Combine(root, "Logs"),
            LayoutsDirectory: Path.Combine(root, "Layouts"),
            DeviceRegistryFilePath: Path.Combine(root, "Pairing", "device-registry.json"));
    }

    public static void EnsureCreated(LunaPanelDirectoryLayout layout, IDiagnosticLog log)
    {
        Directory.CreateDirectory(layout.LogsDirectory);
        log.Info("Discovery", $"LunaPanel logs directory: {layout.LogsDirectory} -> ensured");

        Directory.CreateDirectory(layout.LayoutsDirectory);
        log.Info("Discovery", $"LunaPanel layouts directory: {layout.LayoutsDirectory} -> ensured");

        var registryDirectory = Path.GetDirectoryName(layout.DeviceRegistryFilePath);
        if (!string.IsNullOrEmpty(registryDirectory))
        {
            Directory.CreateDirectory(registryDirectory);
            log.Info("Discovery", $"LunaPanel device registry directory: {registryDirectory} -> ensured");
        }
    }
}
