using System.Diagnostics;

namespace LunaPanel.Server.Discovery;

/// <summary>
/// Whether the currently running copy of LunaPanel is a dev build - the one
/// signal <see cref="LunaPanelDirectories.Resolve"/> uses to decide whether
/// to root under <c>LunaPanel-Dev</c> instead of <c>LunaPanel</c>, so a dev
/// launch used to test changes can never collide with a real install's own
/// Logs/Layouts/Pairing state again.
///
/// Deliberately the SAME signal <c>LunaPanel.Tray.RunningVersion.Current()</c>
/// already reads (the running process's own <c>ProductVersion</c>, stamped
/// from <c>CHANGELOG.md</c>'s header at build time) - not a second,
/// independent "am I a dev build" concept (build configuration, an
/// environment variable) that could drift out of sync with the version
/// string every dev commit already carries a <c>-dev</c> suffix on.
/// </summary>
public static class DevBuildDetector
{
    /// <summary>
    /// True when the running process's own <c>ProductVersion</c> ends with
    /// <c>-dev</c> (case-insensitive). Never throws - this runs at startup,
    /// before the diagnostic log pipeline even exists, so there is nowhere
    /// useful to put an exception; any failure to read the version (a null
    /// or empty <see cref="Environment.ProcessPath"/>, or a version that
    /// can't be read at all) degrades to <see langword="false"/>, the safe
    /// direction - a real install must never be mistaken for a dev build.
    /// </summary>
    public static bool IsDevBuild()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                return false;
            }

            var info = FileVersionInfo.GetVersionInfo(processPath);
            return IsDevVersion(info.ProductVersion);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The pure string check <see cref="IsDevBuild"/> wraps, split out so it
    /// can be pinned directly - <see cref="IsDevBuild"/> itself always reads
    /// the real running test-host process's own version, which is not a
    /// meaningful thing to assert on in a unit test.
    ///
    /// <c>public</c>, not <c>internal</c>, on purpose: <c>LunaPanel.Server</c>
    /// has no <c>[InternalsVisibleTo("LunaPanel.Tests")]</c> grant (see
    /// <c>ref/docs/bindings-source.md</c>'s remarks on <c>BindsFileWatcher</c>'s
    /// own test-support members for the same choice made the same way) -
    /// making one small, pure member public is the accepted trade over
    /// opening the whole assembly's internal surface for its sake.
    /// </summary>
    public static bool IsDevVersion(string? productVersion) =>
        !string.IsNullOrEmpty(productVersion)
        && productVersion.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);
}
