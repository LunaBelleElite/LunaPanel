namespace LunaPanel.Tests.Updates;

/// <summary>
/// Source-scan pins over the two files this suite structurally cannot
/// execute - <c>LunaPanel.Tray</c> is <c>net10.0-windows</c> and is not
/// referenced here (same reason <c>TrayFormsPaletteSourceGuardTests</c>
/// exists, and the same repo-root-by-walking-up idiom), and
/// <c>installer/Package.wxs</c> is not code at all.
///
/// <b>What these are for.</b> Every claim pinned below is one where being
/// wrong means a commander's working install is damaged rather than a
/// feature merely not working: an app that quits itself mid-update, an
/// installer that does not close the running copy before replacing its
/// files, or one that never starts it again afterwards. None of them is
/// reachable by <c>dotnet test</c> any other way, and the alternative to a
/// source pin here is no pin at all.
///
/// <b>What they are NOT.</b> These prove the wiring says the right thing,
/// not that it does the right thing. The update actually installing and
/// relaunching is a live check on a real machine; see this task's own
/// report.
/// </summary>
public class UpdateFlowSourceGuardTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LunaPanel.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (a directory containing LunaPanel.sln) above {AppContext.BaseDirectory}.");
    }

    private static string Read(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    private static string UpdateCheckFlowSource() => Read("src", "LunaPanel.Tray", "UpdateCheckFlow.cs");

    private static string TrayContextSource() => Read("src", "LunaPanel.Tray", "TrayApplicationContext.cs");

    private static string PackageWxsSource() => Read("installer", "Package.wxs");

    /// <summary>
    /// Pins the whole argument string as one literal, not each switch
    /// separately.
    ///
    /// [2026-09-17] It was three separate <c>Contains</c> calls first
    /// ("msiexec.exe", "/quiet", "/norestart"), and deleting <c>/quiet</c>
    /// from the actual command line left all three green - the file's own
    /// doc comment explains why <c>/quiet</c> is there, so the needle went
    /// on existing with nothing using it. Measured, not reasoned about: that
    /// mutation predicted one red and produced zero. The needle is now
    /// scoped to the thing it guards.
    ///
    /// [2026-09-18] Switched to <c>/passive</c> (this task's own fix, closing
    /// the "silent install looks like a hung tray icon for 15-30s" gap).
    /// Same reasoning still applies with the new value: this guards against
    /// a future edit silently reverting to fully-invisible <c>/quiet</c>, and
    /// equally against silently going the other way to a full <c>/i "..."</c>
    /// UI-mode install, which would require user interaction and could block
    /// the install indefinitely. <c>/passive</c> is the one value that is
    /// both visible and cannot be dismissed or block anything.
    /// </summary>
    [Fact]
    public void UpdateCheckFlow_HandsTheDownloadToMsiexec_SilentlyAndWithoutRestarting()
    {
        var source = UpdateCheckFlowSource();

        Assert.Contains("msiexec.exe", source, StringComparison.Ordinal);
        Assert.Contains("""/i \"{installerPath}\" /passive /norestart""", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one thing this flow must NOT do. The installer owns both ends of
    /// the restart - <c>util:CloseApplication</c> terminates LunaPanel and a
    /// launch-after-install action starts it again - so an app-side quit
    /// here would be a second, independent piece of timing racing the first,
    /// and whichever won would decide whether the relaunch had anything left
    /// to relaunch.
    ///
    /// Each needle is one edit away from appearing (this file already calls
    /// <c>Process.Start</c> and lives beside a class whose whole job is
    /// quitting), so this guard can fail rather than merely looking like it
    /// could.
    /// </summary>
    [Theory]
    [InlineData("Application.Exit")]
    [InlineData("Environment.Exit")]
    [InlineData("ExitThread")]
    [InlineData("QuitAsync")]
    [InlineData("BeginQuit")]
    public void UpdateCheckFlow_NeverQuitsLunaPanelItself(string forbidden)
    {
        Assert.DoesNotContain(forbidden, UpdateCheckFlowSource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing installs without an explicit yes. A flow that downloaded and
    /// ran the installer off a single menu click would be an install nobody
    /// asked for.
    /// </summary>
    [Fact]
    public void UpdateCheckFlow_AsksBeforeInstallingAnything()
    {
        var source = UpdateCheckFlowSource();

        Assert.Contains("MessageBoxButtons.YesNo", source, StringComparison.Ordinal);
        Assert.Contains("DialogResult.Yes", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact wording the commander chose for the post-update message,
    /// pinned literally because it was decided rather than drafted - em dash
    /// and all.
    /// </summary>
    [Fact]
    public void TrayApplicationContext_UsesTheAgreedPostUpdateWording()
    {
        Assert.Contains(
            "You're all set — LunaPanel just updated itself to {version} and is back up and running.",
            TrayContextSource(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The startup check has to run through <c>RecordLaunch</c>, which is
    /// what makes a first install silent and a repeat launch silent too. A
    /// bare <c>Load()</c>/compare here without the write-back would announce
    /// the same update on every launch forever.
    /// </summary>
    [Fact]
    public void TrayApplicationContext_RecordsTheLaunchedVersionOnStartup()
    {
        var source = TrayContextSource();

        Assert.Contains("LastLaunchedVersionStore", source, StringComparison.Ordinal);
        Assert.Contains("RecordLaunch", source, StringComparison.Ordinal);
        Assert.Contains("ShowUpdatedMessageIfThisLaunchFollowedAnUpdate()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tray menu item exists and dispatches through the same
    /// <c>TrayAction</c> the Status window's button does - the "one action,
    /// one place" seam <c>TrayStatusWindowActions</c> exists to keep, which
    /// no test outside this file can see for the menu half.
    /// </summary>
    [Fact]
    public void TrayApplicationContext_OffersCheckForUpdates_OnTheMenuAndThroughDispatch()
    {
        var source = TrayContextSource();

        Assert.Contains("menu.Items.Add(\"Check for updates\"", source, StringComparison.Ordinal);
        Assert.Contains("case TrayAction.CheckForUpdates:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageWxs_ClosesTheRunningLunaPanelByProcessName_ByTerminatingIt()
    {
        var source = PackageWxsSource();

        Assert.Contains("util:CloseApplication", source, StringComparison.Ordinal);
        Assert.Contains("Target=\"LunaPanel.Tray.exe\"", source, StringComparison.Ordinal);
        Assert.Contains("TerminateProcess=\"0\"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same bargain: having terminated LunaPanel, the
    /// installer must start it again, on every path that is not an
    /// uninstall. Without this the silent self-update would leave the
    /// commander with nothing running and no sign anything had happened.
    /// </summary>
    [Fact]
    public void PackageWxs_LaunchesLunaPanelAfterInstallFinalize_OnEveryPathButUninstall()
    {
        var source = PackageWxsSource();

        Assert.Contains("Wix4ShellExec_$(sys.BUILDARCHSHORT)", source, StringComparison.Ordinal);
        Assert.Contains("WixShellExecTarget", source, StringComparison.Ordinal);
        Assert.Contains("After=\"InstallFinalize\"", source, StringComparison.Ordinal);
        Assert.Contains("Condition=\"NOT REMOVE\"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Util extension has to be declared for either of the two above to
    /// compile at all - pinned separately because losing the namespace and
    /// losing the elements are the same single edit, and a .wxs that no
    /// longer builds is caught by nothing in this suite.
    /// </summary>
    [Fact]
    public void PackageWxs_DeclaresTheUtilExtensionNamespace()
    {
        Assert.Contains(
            "xmlns:util=\"http://wixtoolset.org/schemas/v4/wxs/util\"",
            PackageWxsSource(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// O45, closed 2026-09-17: the default <c>MajorUpgrade</c> schedule
    /// (<c>afterInstallValidate</c>) puts <c>RemoveExistingProducts</c> at
    /// sequence 1401 - before <c>InstallInitialize</c> (1500) and well before
    /// <c>Wix4CloseApplications</c> (3999, right before <c>InstallFiles</c> at
    /// 4000) ever terminates a running <c>LunaPanel.Tray.exe</c>. On an
    /// upgrade that removes the OLD product's files while the exe still has
    /// them open, which can leave a delete-on-reboot flag on the newly
    /// installed copy - invisible until the next restart. This needle is the
    /// only thing in this suite that can see the fix at all: no test here can
    /// read a built MSI's actual `InstallExecuteSequence` (that was verified
    /// separately, live, by reading it back with the WindowsInstaller COM
    /// API - see `tests/notes/open-items.md`'s O45 closure), so losing this
    /// one line silently reintroduces the exact hazard the row was opened
    /// over with nothing in `dotnet test` able to notice.
    /// </summary>
    [Fact]
    public void PackageWxs_MajorUpgradeSchedulesAfterInstallExecute_SoTheOldProductIsRemovedAfterTheCloseAndTheNewFiles()
    {
        Assert.Contains(
            "<MajorUpgrade Schedule=\"afterInstallExecute\"",
            PackageWxsSource(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The build script and <c>LunaPanel.Tray.csproj</c> must read the same
    /// <c>CHANGELOG.md</c> header, or the version in the About window and
    /// the version on the MSI's file name can disagree - and the MSI's file
    /// name is how an update picks the right asset off a release.
    /// </summary>
    [Fact]
    public void BuildInstallerScript_DerivesTheVersionFromTheChangelog_NotAHandTypedCopy()
    {
        var script = Read("scripts", "build-installer.sh");

        Assert.Contains("CHANGELOG.md", script, StringComparison.Ordinal);
        Assert.Contains("ver-", script, StringComparison.Ordinal);
        Assert.Contains("LunaPanel-${VERSION_FULL}.msi", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without <c>-arch x64</c>, <c>wix build</c> defaults to x86 even though
    /// the harvested publish folder is a self-contained win-x64 build - found
    /// by the implementer as a pre-existing defect (the built MSI was
    /// mislabeled x86), fixed the same day. Confirmed separately, live, that
    /// the built MSI's `SummaryInformation` Template property now reads
    /// `x64;1033` rather than an x86 marker (see `tests/notes/open-items.md`'s
    /// O45 closure for the sibling live check this one sits beside) - no test
    /// here can read that property back out of a built MSI, so this needle is
    /// the only thing in `dotnet test` standing between this flag and a
    /// silently mislabeled installer again.
    /// </summary>
    [Fact]
    public void BuildInstallerScript_BuildsForX64_NotWhateverWixDefaultsTo()
    {
        var script = Read("scripts", "build-installer.sh");

        Assert.Contains("-arch x64", script, StringComparison.Ordinal);
    }
}
