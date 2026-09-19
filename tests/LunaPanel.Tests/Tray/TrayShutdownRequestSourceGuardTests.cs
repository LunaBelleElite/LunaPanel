namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pins over <c>LunaPanel.Tray.ShutdownRequestWindow</c> and the
/// way <c>Program.Run</c> wires it - the app's side of the installer
/// sequencing fix of 2026-09-18 (<c>ref/docs/updates.md</c>, "Closing the
/// running copy: the app now answers").
///
/// <b>Why scans.</b> Same reason as <see cref="TrayStartupRetrySourceGuardTests"/>:
/// the test project does not reference the WinForms tray project, and what
/// is being guarded is that a real window exists, hears two specific
/// messages, and ends the app - a shape, not a runtime behaviour this suite
/// could drive. The behaviour was measured live: <c>RmShutdown</c> against
/// the running tray took 30,173 ms and failed (<c>ERROR_FAIL_SHUTDOWN</c>)
/// before this change, and 99 ms with a clean exit after it; the installer's
/// close action went from a 10 s timeout-then-terminate to ~100 ms.
///
/// <b>What a regression looks like.</b> Remove the window, make it
/// message-only, stop answering <c>WM_ENDSESSION</c>, or take the host stop
/// back out of <c>Program.Run</c>, and the tray silently goes back to
/// ignoring "please exit": Restart Manager waits its full 30 s on every
/// self-update and the installer terminates the process instead of letting
/// it stop cleanly. Nothing else in <c>dotnet test</c> can see any of that.
/// </summary>
public class TrayShutdownRequestSourceGuardTests
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

    private static string WindowSource() => Read("src", "LunaPanel.Tray", "ShutdownRequestWindow.cs");

    private static string ProgramSource() => Read("src", "LunaPanel.Tray", "Program.cs");

    /// <summary>
    /// A real top-level window, created with a handle. A message-only
    /// window (<c>HWND_MESSAGE</c> parent) is never enumerated and would
    /// receive neither message - measured with a throwaway probe on
    /// 2026-09-18: Restart Manager and the WiX close action both send to
    /// every enumerable top-level window of the process, hidden or not.
    /// </summary>
    [Fact]
    public void ShutdownRequestWindow_IsARealTopLevelWindow_NotMessageOnly()
    {
        var source = WindowSource();

        Assert.Contains(": NativeWindow", source, StringComparison.Ordinal);
        Assert.Contains("CreateHandle(new CreateParams", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HWND_MESSAGE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Parent = new IntPtr(-3)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two messages, by their real values, and the protocol: say yes to
    /// the query, act only on a true <c>WM_ENDSESSION</c> (a false one means
    /// the shutdown was cancelled).
    /// </summary>
    [Fact]
    public void ShutdownRequestWindow_AnswersTheQueryAndActsOnlyOnATrueEndSession()
    {
        var source = WindowSource();

        Assert.Contains("WmQueryEndSession = 0x0011", source, StringComparison.Ordinal);
        Assert.Contains("WmEndSession = 0x0016", source, StringComparison.Ordinal);
        Assert.Contains("case WmQueryEndSession:", source, StringComparison.Ordinal);
        Assert.Contains("m.Result = 1;", source, StringComparison.Ordinal);
        Assert.Contains("case WmEndSession:", source, StringComparison.Ordinal);
        Assert.Contains("if (m.WParam != 0)", source, StringComparison.Ordinal);
        Assert.Contains("_onShutdownRequested();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Program.Run</c> creates the window for the life of the message loop,
    /// ends the loop on the request, and stops the host afterwards - the
    /// same icon-then-host-then-loop ordering <c>TrayApplicationContext</c>'s
    /// own Quit documents. Without the host stop, the process would exit
    /// with Kestrel never told to stop.
    /// </summary>
    [Fact]
    public void ProgramRun_WiresTheWindow_EndsTheLoop_ThenStopsTheHost()
    {
        var source = ProgramSource();

        Assert.Contains("using (new ShutdownRequestWindow(() =>", source, StringComparison.Ordinal);
        Assert.Contains("Application.Exit();", source, StringComparison.Ordinal);
        Assert.Contains("Application.Run(context);", source, StringComparison.Ordinal);
        Assert.Contains("StopHostAfterShutdownRequest(app, log);", source, StringComparison.Ordinal);
        Assert.Contains("await app.StopAsync(budget.Token);", source, StringComparison.Ordinal);
        Assert.Contains("await app.DisposeAsync();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host stop is bounded twice (its own budget and the outer wait)
    /// and runs off the UI thread - a continuation posted to a loop that has
    /// already ended would never run, and an installer is waiting on this
    /// process to be gone.
    /// </summary>
    [Fact]
    public void HostStopAfterShutdownRequest_IsBounded_AndOffTheUiThread()
    {
        var source = ProgramSource();

        Assert.Contains("Task.Run(async () =>", source, StringComparison.Ordinal);
        Assert.Contains("new CancellationTokenSource(TimeSpan.FromSeconds(5))", source, StringComparison.Ordinal);
        Assert.Contains("stop.Wait(TimeSpan.FromSeconds(8))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The request is logged, so the next investigation can tell a
    /// system-initiated exit from a crash or a manual Quit in the diagnostic
    /// log - which is exactly how 2026-09-18's cycles were read.
    /// </summary>
    [Fact]
    public void ShutdownRequest_IsLogged_BeforeAnythingStops()
    {
        Assert.Contains("\"Shutdown requested by the system\"", ProgramSource(), StringComparison.Ordinal);
    }
}
