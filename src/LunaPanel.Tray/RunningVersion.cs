using System.Diagnostics;
using System.Windows.Forms;

namespace LunaPanel.Tray;

/// <summary>
/// The version this copy of LunaPanel is actually running, read from the
/// assembly's own <c>InformationalVersion</c> - stamped at build time from
/// <c>CHANGELOG.md</c>'s topmost <c>## ver-...</c> header
/// (<c>LunaPanel.Tray.csproj</c>), never a second hand-typed copy that could
/// drift from the file the commander already keeps current.
///
/// <b>One reader, not two.</b> <see cref="AboutForm"/> showed this first and
/// the update check needs the exact same string to compare a published
/// release against - two copies of the same <c>FileVersionInfo</c> call
/// would be two things that could disagree about what "running" means, which
/// on the update path decides whether an install happens.
/// </summary>
internal static class RunningVersion
{
    /// <summary>
    /// The literal version string (e.g. <c>ver-0.53.0.2-dev</c>), or
    /// <c>"(unknown)"</c> when it cannot be read at all. Never throws - both
    /// callers are UI, and neither has anywhere useful to put an exception.
    ///
    /// <c>"(unknown)"</c> is deliberately not a version: it does not parse
    /// (<c>LunaPanel.Core.Updates.VersionComparer</c>), so an unreadable
    /// version can only ever mean "offer nothing", never "everything is
    /// newer than this".
    /// </summary>
    public static string Current()
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(Application.ExecutablePath);
            return string.IsNullOrEmpty(info.ProductVersion) ? "(unknown)" : info.ProductVersion;
        }
        catch
        {
            return "(unknown)";
        }
    }
}
