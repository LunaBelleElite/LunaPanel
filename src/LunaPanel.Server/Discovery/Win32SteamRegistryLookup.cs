using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Reads Steam's own registry-recorded install location - the one source
/// that finds a Steam install anywhere other than the two Program-Files
/// default guesses <see cref="SteamInstallDiscovery.GetCandidateSteamRoots"/>
/// otherwise relies on. Not directly unit-tested (it is a thin wrapper over
/// live registry state, same treatment as <c>Win32ForegroundInspector</c> -
/// see <c>ref/docs/injection.md</c>'s "The [SupportedOSPlatform] split" for
/// why); <see cref="SteamInstallDiscovery.GetCandidateSteamRoots"/> is what
/// carries the actual test coverage, driven by an injected <c>Func&lt;string?&gt;</c>
/// standing in for these methods.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Win32SteamRegistryLookup
{
    public static string? ReadSteamPathFromCurrentUserRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    public static string? ReadInstallPathFromLocalMachineRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        return key?.GetValue("InstallPath") as string;
    }
}
