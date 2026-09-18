namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pins over <c>TrayApplicationContext</c>'s shutdown path.
///
/// <b>Why scans and not behaviour.</b> There is no headless WinForms harness
/// in this suite (<c>ref/docs/hosting.md</c>'s "What is NOT tested"), and
/// the thing that needs guarding here is a control-flow guarantee inside a
/// method that ends the process - which is not something a test can drive
/// and survive. These are the same kind of pin
/// <c>TrayMacroBuilderMenuTests</c>/<c>TrayTransferMenuTests</c> already use
/// on the same file, and they carry the same weakness: they prove the source
/// still says a thing, not that the thing works.
///
/// <b>What they are defending.</b> On 2026-09-10 Quit stopped closing the
/// Status window. The tray icon disappeared and Kestrel stopped, because
/// those happen first, but <c>ExitThread()</c> never ran - so
/// <c>Application.Run</c> never returned, <c>Program.Main</c>'s
/// <c>using</c> never disposed the context, and the window stayed on screen
/// with no way to quit the program. The commander: <i>"when I hit quit on
/// the status panel, the project seems to stop, but the status window
/// doesn't close."</i> Two independent causes were in play - a discarded
/// task that swallowed any failure, and an unguarded call order where one
/// throw skipped the exit - and each pin below covers one of them.
/// </summary>
public class TrayShutdownSourceGuardTests
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

    private static string ReadTraySource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", "TrayApplicationContext.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The whole fix in one line. <c>ExitThread()</c> is the single call that
    /// ends <c>Application.Run</c>; anywhere but a <c>finally</c>, a host
    /// that throws on stop leaves the process alive with its window up.
    /// </summary>
    [Fact]
    public void ExitThread_IsCalledFromAFinally_SoAFailedShutdownStillEndsTheProcess()
    {
        var source = ReadTraySource();

        Assert.Contains(
            """
                    finally
                    {
                        ExitThread();
                    }
            """.TrimEnd(),
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half. A bare <c>_ = QuitAsync()</c> discards the task and
    /// with it any exception, which is what turned a loud failure into a
    /// silent one when the menu's <c>async</c> handler was folded into the
    /// shared dispatch.
    /// </summary>
    [Fact]
    public void Quit_IsNotDispatchedAsABareDiscardedTask()
    {
        var source = ReadTraySource();

        Assert.DoesNotContain("_ = QuitAsync();", source, StringComparison.Ordinal);
        Assert.Contains("BeginQuit();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Shutdown is bounded. Without a budget, a host that never finishes
    /// stopping is indistinguishable to the commander from the original bug.
    /// </summary>
    [Fact]
    public void StoppingTheHost_IsBounded_RatherThanWaitedOnForever()
    {
        var source = ReadTraySource();

        Assert.Contains("ShutdownBudget", source, StringComparison.Ordinal);
        Assert.Contains("await _app.StopAsync(budget.Token);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await _app.StopAsync();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The window is hidden when Quit is pressed, not when shutdown finishes
    /// - stopping Kestrel with a live channel open can take seconds, and a
    /// window that sits there through it reads as "nothing happened", which
    /// is the report itself.
    /// </summary>
    [Fact]
    public void TheStatusWindow_IsHidden_BeforeTheHostIsStopped()
    {
        var source = ReadTraySource();

        var hideAt = source.IndexOf("_statusForm.Hide();", StringComparison.Ordinal);
        var stopAt = source.IndexOf("await _app.StopAsync(", StringComparison.Ordinal);

        Assert.True(hideAt > 0, "Expected QuitAsync to hide the Status window.");
        Assert.True(stopAt > 0, "Expected QuitAsync to stop the host.");
        Assert.True(hideAt < stopAt, "Expected the window to be hidden before the host is stopped, not after.");
    }
}
