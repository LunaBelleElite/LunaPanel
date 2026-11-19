using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Asks Windows where the "Saved Games" known folder really is. A user can
/// relocate it (folder Properties > Location), and Elite writes
/// <c>Status.json</c> and the journal under the relocated folder, so the
/// conventional <c>&lt;profile&gt;\Saved Games</c> guess is wrong on such a
/// machine. Not unit-tested for a specific location (a thin wrapper over
/// live OS state, same treatment as <see cref="Win32SteamRegistryLookup"/>);
/// <see cref="EliteSavedGamesLocator"/> carries the decision logic and is
/// driven by an injected <c>Func&lt;string?&gt;</c> standing in for this.
/// A smoke test proves the P/Invoke marshals.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Win32KnownFolderLookup
{
    // FOLDERID_SavedGames
    private static readonly Guid SavedGamesFolderId = new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");

    // KF_FLAG_DONT_VERIFY: return the configured path even if the folder has
    // not been created yet, matching "absence just means the game never ran".
    private const uint KfFlagDontVerify = 0x00004000;

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    /// <summary>
    /// The Saved Games folder Windows reports for the current user, or
    /// <c>null</c> on a failure HRESULT, a null pointer, or an empty string.
    /// </summary>
    public static string? GetSavedGamesPath()
    {
        var hr = SHGetKnownFolderPath(SavedGamesFolderId, KfFlagDontVerify, IntPtr.Zero, out var pathPointer);
        try
        {
            if (hr < 0 || pathPointer == IntPtr.Zero)
            {
                return null;
            }

            var path = Marshal.PtrToStringUni(pathPointer);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        finally
        {
            // Documented: the caller frees the string whether or not the call
            // succeeded, so free whenever a pointer came back.
            if (pathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }
    }
}
