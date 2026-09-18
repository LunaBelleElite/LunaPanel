using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Updates;

namespace LunaPanel.Tray;

/// <summary>
/// The whole "Check for updates" flow, start to finish: ask what the latest
/// published release is, compare it to what is running, offer it, download
/// it, and hand it to <c>msiexec</c>.
///
/// Self-contained in its own class rather than four more methods on
/// <see cref="TrayApplicationContext"/>, mirroring <see cref="AboutForm"/>'s
/// shape - the tray context's job is to own the icon, the menu and the
/// host's lifetime, and this is a conversation with the commander that
/// happens to start from one of its menu items.
///
/// <b>This class never quits LunaPanel, and must not start.</b> The
/// installer owns both ends of that: <c>util:CloseApplication</c> in
/// <c>installer/Package.wxs</c> terminates <c>LunaPanel.Tray.exe</c> before
/// replacing any file, and a launch-after-install custom action starts it
/// again once the install finishes. An app-side quit here would be a second,
/// independent piece of timing doing the same job - and the one that runs
/// first decides whether the relaunch has anything to relaunch.
///
/// <b>Nothing installs without an explicit yes.</b> One confirmation, and
/// after it the flow is completely silent by design (no installer UI, no
/// progress, no second prompt) - which is exactly why
/// <see cref="LastLaunchedVersionStore"/> exists to say so on the next
/// launch.
/// </summary>
internal sealed class UpdateCheckFlow
{
    private const string LogCategory = "Updates";
    private const string DialogTitle = "LunaPanel";

    private readonly IReleaseChecker _releaseChecker;
    private readonly IDiagnosticLog _log;
    private readonly string _currentVersion;

    public UpdateCheckFlow(IReleaseChecker releaseChecker, IDiagnosticLog log, string currentVersion)
    {
        _releaseChecker = releaseChecker ?? throw new ArgumentNullException(nameof(releaseChecker));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    /// <summary>
    /// Runs the flow. Awaited from the WinForms message loop, so every
    /// <c>await</c> here resumes on the UI thread and the
    /// <see cref="MessageBox"/> calls need no marshalling of their own.
    /// </summary>
    public async Task RunAsync()
    {
        _log.Info(LogCategory, "Checking for updates", $"running={_currentVersion}");

        var latest = await _releaseChecker.GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(true);

        if (latest is null)
        {
            // GetLatestReleaseAsync already logged why. The commander does
            // not need the reason - every one of them ("no internet",
            // "rate limited", "GitHub is down") has the same answer.
            Show(
                "LunaPanel couldn't check for updates just now. Worth trying again a bit later.",
                MessageBoxIcon.Information);
            return;
        }

        if (!VersionComparer.IsNewer(latest.Version, _currentVersion))
        {
            _log.Info(LogCategory, "No update available", $"running={_currentVersion}, latest={latest.Version}");
            Show(
                $"You're up to date — you're running {_currentVersion}.",
                MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Version {latest.Version} is available (you have {_currentVersion}). Update now?",
            DialogTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            _log.Info(LogCategory, "Update declined", $"offered={latest.Version}");
            return;
        }

        var installerPath = await DownloadAsync(latest).ConfigureAwait(true);
        if (installerPath is null)
        {
            Show(
                "LunaPanel couldn't download that update, so nothing has been changed. Worth trying again a bit later.",
                MessageBoxIcon.Warning);
            return;
        }

        StartInstaller(installerPath);
    }

    /// <summary>
    /// Downloads the release's MSI to a temp file, streamed rather than
    /// buffered whole, and never on the UI thread's back - a self-contained
    /// LunaPanel build is not small, and a blocking download would look
    /// exactly like a hung tray icon.
    ///
    /// Returns null on any failure, having already logged it and cleaned up
    /// whatever partial file it left behind. A half-downloaded MSI handed to
    /// <c>msiexec</c> is the one outcome here that could actually damage a
    /// working install.
    /// </summary>
    private async Task<string?> DownloadAsync(ReleaseInfo release)
    {
        var targetPath = TempInstallerPath(release.Version);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using var client = new HttpClient { Timeout = DownloadTimeout };
            using var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warn(LogCategory, "The update download was refused", $"status={(int)response.StatusCode} {response.StatusCode}");
                return null;
            }

            await using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target).ConfigureAwait(false);
            }

            _log.Info(LogCategory, "Downloaded the update", $"version={release.Version}");
            return targetPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Error(LogCategory, "Could not download the update", $"{ex.GetType().Name}: {ex.Message}");
            TryDelete(targetPath);
            return null;
        }
    }

    /// <summary>
    /// Hands the downloaded MSI to Windows Installer and returns
    /// immediately.
    ///
    /// <c>/quiet</c> is the confirmed design (no installer UI at all),
    /// <c>/norestart</c> because nothing this installs can need a reboot and
    /// a silent install is the last place a machine should decide to restart
    /// itself. <c>msiexec</c> is a separate process, so it survives this one
    /// being terminated moments later by the installer's own
    /// <c>util:CloseApplication</c> - which is the whole reason this method
    /// can simply return.
    /// </summary>
    private void StartInstaller(string installerPath)
    {
        try
        {
            _log.Info(LogCategory, "Starting the silent update install", installerPath);
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{installerPath}\" /quiet /norestart")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Error(LogCategory, "Could not start the update install", $"{ex.GetType().Name}: {ex.Message}");
            Show(
                $"LunaPanel downloaded the update but couldn't start the installer, so nothing has been changed. You can run it yourself from:{Environment.NewLine}{Environment.NewLine}{installerPath}",
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// How long the download is allowed to take. Generous - this is a
    /// self-contained runtime, over whatever connection the commander has -
    /// but not unbounded, because a stalled download with no progress UI is
    /// indistinguishable from a broken one.
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Where the downloaded MSI lands. Under the temp folder in a folder of
    /// LunaPanel's own, and named from the version rather than from anything
    /// the response said - a file name taken from a remote response is a
    /// path, and a path is something that can point somewhere else.
    /// </summary>
    private static string TempInstallerPath(string version)
    {
        var safeVersion = string.Concat(version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Path.GetTempPath(), "LunaPanel-updates", $"LunaPanel-{safeVersion}.msi");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best-effort: a leftover partial file in the temp folder is not
            // worth failing the flow over, and Windows clears it eventually.
        }
    }

    private static void Show(string text, MessageBoxIcon icon) =>
        MessageBox.Show(text, DialogTitle, MessageBoxButtons.OK, icon);
}
