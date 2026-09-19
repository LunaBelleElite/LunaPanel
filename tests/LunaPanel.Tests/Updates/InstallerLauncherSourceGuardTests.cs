namespace LunaPanel.Tests.Updates;

/// <summary>
/// Source-scan pins over the installer's own helper
/// (<c>installer/launcher/LunaPanel.Launcher.cs</c>) and the way
/// <c>installer/Package.wxs</c> and <c>scripts/build-installer.sh</c> use it -
/// the installer's side of the sequencing fix of 2026-09-18
/// (<c>ref/docs/updates.md</c>, "The launcher"). Same idiom and the same
/// honesty as <see cref="UpdateFlowSourceGuardTests"/>: these prove the
/// wiring says the right thing. The helper is not part of the solution
/// (it is compiled by Windows' own C# 5 compiler at installer build time),
/// so nothing here executes it; both failure modes it exists for were forced
/// and observed live on a real machine instead - see this task's report and
/// the doc page.
/// </summary>
public class InstallerLauncherSourceGuardTests
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

    private static string LauncherSource() => Read("installer", "launcher", "LunaPanel.Launcher.cs");

    private static string PackageWxsSource() => Read("installer", "Package.wxs");

    private static string BuildScript() => Read("scripts", "build-installer.sh");

    private static string VerifyScript() => Read("scripts", "verify-msi-file-versions.ps1");

    /// <summary>
    /// The version is fixed by hand, never derived from <c>CHANGELOG.md</c>:
    /// Windows Installer only overwrites a versioned file with a higher one,
    /// so a fixed version is what keeps the installed helper from being one
    /// of the freshly-written files it waits on. Both attributes, matching.
    /// </summary>
    [Fact]
    public void Launcher_CarriesAFixedVersion_NotTheProductVersion()
    {
        var source = LauncherSource();

        var assemblyVersion = System.Text.RegularExpressions.Regex.Match(source, @"AssemblyVersion\(""(\d+\.\d+\.\d+\.\d+)""\)").Groups[1].Value;
        var fileVersion = System.Text.RegularExpressions.Regex.Match(source, @"AssemblyFileVersion\(""(\d+\.\d+\.\d+\.\d+)""\)").Groups[1].Value;

        Assert.False(string.IsNullOrEmpty(assemblyVersion), "Expected an explicit AssemblyVersion attribute.");
        Assert.Equal(assemblyVersion, fileVersion);

        // Scoped to CODE lines only (same idiom as Launcher_StaysWithinCSharp5,
        // just below) - not the whole file. The header comment explaining WHY
        // the version is fixed deliberately says "never wire it to
        // CHANGELOG.md's version", and that explanatory mention is correct
        // documentation, not the thing this guard exists to catch. What must
        // never happen is the version attributes themselves being DERIVED from
        // CHANGELOG.md in code - e.g. reading it at build time the way
        // scripts/build-installer.sh does for the rest of the product.
        var codeLines = source
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("///", StringComparison.Ordinal));

        Assert.DoesNotContain(codeLines, l => l.Contains("CHANGELOG", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// C# 5 only - Windows' inbox <c>csc.exe</c> is what compiles this file
    /// on any build machine, and it rejects everything newer. The needles are
    /// the newer-syntax habits most likely to creep in from the rest of this
    /// repo.
    /// </summary>
    [Theory]
    [InlineData("$\"")]
    [InlineData("?.")]
    [InlineData("nameof(")]
    [InlineData("=> ")]
    [InlineData(" is not ")]
    [InlineData("using var ")]
    public void Launcher_StaysWithinCSharp5(string newerSyntax)
    {
        // Comments may describe newer syntax; only code lines are checked.
        var codeLines = LauncherSource()
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("///", StringComparison.Ordinal));

        Assert.DoesNotContain(codeLines, l => l.Contains(newerSyntax, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every loop bounded, both modes present, the probe opened the way the
    /// loader opens an image. A wait that never ends is a hung install.
    /// </summary>
    [Fact]
    public void Launcher_HasBothModes_AndEveryWaitIsBounded()
    {
        var source = LauncherSource();

        Assert.Contains("if (mode == \"wait\")", source, StringComparison.Ordinal);
        Assert.Contains("if (mode == \"launch\")", source, StringComparison.Ordinal);
        Assert.Contains("private const int WaitForFilesSeconds = 10;", source, StringComparison.Ordinal);
        Assert.Contains("private const int LaunchAttempts = 3;", source, StringComparison.Ordinal);
        Assert.Contains("if (stopwatch.Elapsed >= budget)", source, StringComparison.Ordinal);
        Assert.Contains("FileShare.Read | FileShare.Delete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("while (true) { }", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The watch has to outlast Windows Error Reporting (a tray whose entry
    /// assembly is held faults within 130 ms but does not EXIT until WER is
    /// done with it, ~1.3-1.9 s measured), and its verdict must be
    /// structural: exit code, or a top-level window. Two drafts that trusted
    /// <c>WaitForInputIdle</c> both declared a WER-frozen crash "idle",
    /// retried nothing, and left no tray running when the hold was forced on
    /// 2026-09-18; the third, which pins here, recovered it.
    /// </summary>
    [Fact]
    public void Launcher_WatchesLongEnoughForWer_AndJudgesByExitCodeOrAWindow_NeverByInputIdle()
    {
        var source = LauncherSource();

        Assert.Contains("private const int StartupWatchMilliseconds = 5000;", source, StringComparison.Ordinal);
        Assert.Contains("if (process.WaitForExit(PollMilliseconds))", source, StringComparison.Ordinal);
        Assert.Contains("return process.ExitCode == 0;", source, StringComparison.Ordinal);
        Assert.Contains("if (HasTopLevelWindow(pid))", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitForInputIdle(", source.Replace("WaitForInputIdle and", string.Empty).Replace("WaitForInputIdle reports", string.Empty).Replace("WaitForInputIdle, which", string.Empty), StringComparison.Ordinal);
    }

    /// <summary>
    /// The close that removes the dead time runs before <c>InstallValidate</c>
    /// (sequence 1399 in the built MSI, read back), asks every window of every
    /// running copy the way Windows does, waits, and terminates only as a
    /// last resort.
    /// </summary>
    [Fact]
    public void Launcher_CloseMode_AsksEveryWindow_WaitsBounded_ThenTerminates()
    {
        var source = LauncherSource();
        var wxs = PackageWxsSource();

        Assert.Contains("if (mode == \"close\")", source, StringComparison.Ordinal);
        Assert.Contains("SendMessageTimeout(hWnd, WmQueryEndSession,", source, StringComparison.Ordinal);
        Assert.Contains("SendMessageTimeout(hWnd, WmEndSession, (IntPtr)1,", source, StringComparison.Ordinal);
        Assert.Contains("if (!p.WaitForExit(GracefulExitMilliseconds))", source, StringComparison.Ordinal);
        Assert.Contains("p.Kill();", source, StringComparison.Ordinal);
        Assert.Contains("ExeCommand=\"close &quot;[INSTALLFOLDER]LunaPanel.Tray.exe&quot;\"", wxs, StringComparison.Ordinal);
        Assert.Contains("<Custom Action=\"CloseLunaPanelBeforeValidate\" Before=\"InstallValidate\" />", wxs, StringComparison.Ordinal);
    }

    /// <summary>
    /// Started the way <c>Wix4ShellExec</c> started it - through the shell.
    /// </summary>
    [Fact]
    public void Launcher_StartsTheTrayThroughTheShell_LikeTheActionItReplaced()
    {
        Assert.Contains("start.UseShellExecute = true;", LauncherSource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Package.wxs</c>: the helper is both an installed file (the launch
    /// action runs the never-rewritten installed copy) and a Binary-table
    /// entry (the wait action runs before <c>InstallFiles</c>, when the file
    /// may not exist yet). Both actions ignore their return - nothing here
    /// may fail an install - and the launch action still never runs on an
    /// uninstall.
    /// </summary>
    [Fact]
    public void PackageWxs_ShipsTheLauncherTwice_AndNeitherActionCanFailTheInstall()
    {
        var source = PackageWxsSource();

        Assert.Contains("<File Id=\"LunaPanelLauncherExe\"", source, StringComparison.Ordinal);
        Assert.Contains("<Binary Id=\"LunaPanelLauncherBinary\"", source, StringComparison.Ordinal);
        Assert.Contains("BinaryRef=\"LunaPanelLauncherBinary\"", source, StringComparison.Ordinal);
        Assert.Contains("FileRef=\"LunaPanelLauncherExe\"", source, StringComparison.Ordinal);
        Assert.Contains("ExeCommand=\"wait &quot;[INSTALLFOLDER]LunaPanel.Tray.exe&quot;\"", source, StringComparison.Ordinal);
        Assert.Contains("ExeCommand=\"launch &quot;[INSTALLFOLDER]LunaPanel.Tray.exe&quot;\"", source, StringComparison.Ordinal);

        // Counted with XML comments stripped first, not over the raw file:
        // the three CustomAction elements (wait/close/launch) each explain
        // themselves in a preceding <!-- --> comment that quotes
        // Return="ignore" verbatim in prose, which would otherwise double
        // (then triple) this count every time a comment is reworded, with
        // nothing to do with whether an action can actually fail the install.
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(source, "<!--.*?-->", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(withoutComments, "Return=\"ignore\"").Count);
        Assert.Contains("<Custom Action=\"LaunchLunaPanelAfterInstall\" After=\"InstallFinalize\" Condition=\"NOT REMOVE\" />", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wait sits between the close and <c>InstallFiles</c> - confirmed
    /// by reading the built MSI's <c>InstallExecuteSequence</c> back out
    /// (close 3998, wait 3999, InstallFiles 4000), which no test here can
    /// do; this needle is the only thing standing between the wait and
    /// silently landing somewhere useless.
    /// </summary>
    [Fact]
    public void PackageWxs_SchedulesTheWait_RightAfterTheCloseAction()
    {
        Assert.Contains(
            "<Custom Action=\"WaitForLunaPanelFilesToBeFree\" After=\"Wix4CloseApplications_$(sys.BUILDARCHSHORT)\" />",
            PackageWxsSource(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Wix4ShellExec</c> and its property plumbing are gone - a bare
    /// ShellExecute with <c>Return="check"</c> is exactly what this replaces,
    /// and leaving it beside the launcher would start the tray twice.
    /// </summary>
    [Fact]
    public void PackageWxs_NoLongerUsesWixShellExec()
    {
        var source = PackageWxsSource();

        Assert.DoesNotContain("CustomActionRef Id=\"Wix4ShellExec", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Property Id=\"WixShellExecTarget\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Custom Action=\"Wix4ShellExec", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The build compiles the helper with Windows' own compiler and hands the
    /// result to WiX; the version check knows the helper is the one
    /// deliberate exception to "every LunaPanel.*.exe carries the product
    /// version" and still insists it is present and versioned.
    /// </summary>
    [Fact]
    public void BuildAndVerifyScripts_CompileAndAccountForTheLauncher()
    {
        var build = BuildScript();
        var verify = VerifyScript();

        Assert.Contains("Microsoft.NET\\\\Framework64\\\\v4.0.30319\\\\csc.exe", build, StringComparison.Ordinal);
        Assert.Contains("-target:winexe", build, StringComparison.Ordinal);
        Assert.Contains("-d \"LauncherExe=$LAUNCHER_EXE_ABS\"", build, StringComparison.Ordinal);
        Assert.Contains("if ($longName -eq 'LunaPanel.Launcher.exe')", verify, StringComparison.Ordinal);
        Assert.Contains("if (-not $launcherSeen)", verify, StringComparison.Ordinal);
    }
}
