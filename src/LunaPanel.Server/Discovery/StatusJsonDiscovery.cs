using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>Whether Elite's live-state directory was actually found, and where.</summary>
public sealed record StatusJsonDiscoveryResult(string? Directory);

/// <summary>
/// Confirms whether the directory Elite Dangerous writes its live-state file
/// to actually exists. Elite creates this directory itself the first time it
/// runs, so its absence on a machine that has never launched the game even
/// once is a normal "not found," not an error - the same treatment every
/// other optional discovery step in this subsystem gives a missing target
/// (see <see cref="BindingsDiscovery"/>/<see cref="EdhmDiscovery"/>).
///
/// Deliberately just an existence check on a single, already-resolved
/// directory - unlike <see cref="BindingsDiscovery"/> this never enumerates
/// the directory's contents (that is <c>StatusFileWatcher</c>'s job, over a
/// fixed filename, which is what keeps it safe from the conflict-copy hazard
/// this subsystem's other enumerations have to guard against explicitly).
/// </summary>
public static class StatusJsonDiscovery
{
    public static StatusJsonDiscoveryResult Discover(string? statusJsonDirectory, IDiagnosticLog log)
    {
        if (statusJsonDirectory is null || !Directory.Exists(statusJsonDirectory))
        {
            log.Info("Discovery", $"Status.json directory: {statusJsonDirectory ?? "(not configured)"} -> not found");
            return new StatusJsonDiscoveryResult(null);
        }

        log.Info("Discovery", $"Status.json directory: {statusJsonDirectory} -> found");
        return new StatusJsonDiscoveryResult(statusJsonDirectory);
    }
}
