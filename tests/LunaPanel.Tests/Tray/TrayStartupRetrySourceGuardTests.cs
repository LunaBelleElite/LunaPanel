namespace LunaPanel.Tests.Tray;

/// <summary>
/// Source-scan pins over <c>LunaPanel.Tray.Program</c>'s startup retry.
///
/// <b>Why scans and not behaviour.</b> Same reasoning as
/// <see cref="TrayShutdownSourceGuardTests"/>: the test project does not
/// reference the WinForms tray project, and what needs guarding is the
/// <i>shape</i> of <c>Main</c> - which is a compile-time property of the
/// source, not a runtime behaviour a test could drive. The behaviour itself
/// was proven on 2026-09-18 by launching the built exe while
/// <c>LunaPanel.Server.dll</c> was held open exclusively: before this change
/// that is a crash within 130 ms; after it the first process waits out the
/// hold, hands over to a fresh copy of itself, and that copy starts.
///
/// <b>What they are defending.</b> The runtime resolves an assembly when it
/// JIT-compiles the first method that mentions one of its types. If a later
/// edit moves any <c>LunaPanel.Server</c>/<c>LunaPanel.Core</c>/WinForms
/// reference back into <c>Main</c>, the load happens before <c>Main</c>'s
/// first line and the retry silently stops covering anything - the code
/// still compiles, still reads as if it retries, and the post-install crash
/// comes back. That is exactly the kind of regression a source pin is for.
/// </summary>
public class TrayStartupRetrySourceGuardTests
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

    private static string ReadProgramSource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Tray", "Program.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Extracts the body of <c>Main</c> - from its declaration to the
    /// declaration that follows it - so the pins below can say what
    /// <c>Main</c> itself may and may not mention.
    /// </summary>
    private static string MainBody(string source)
    {
        var start = source.IndexOf("private static void Main()", StringComparison.Ordinal);
        Assert.True(start > 0, "Expected Program.cs to declare 'private static void Main()'.");

        var end = source.IndexOf("private static ", start + 1, StringComparison.Ordinal);
        Assert.True(end > start, "Expected another private static member after Main.");

        return source[start..end];
    }

    /// <summary>
    /// The load-bearing constraint: <c>Main</c> must not mention any type
    /// from LunaPanel's own assemblies or from WinForms, or the retry cannot
    /// catch the failure it exists for.
    /// </summary>
    [Fact]
    public void Main_MentionsNothingFromLunaPanelsOwnAssembliesOrWinForms()
    {
        var main = MainBody(ReadProgramSource());

        foreach (var forbidden in new[] { "ServerHostBuilder", "RealServerEnvironment", "TrayApplicationContext", "Application.", "IDiagnosticLog", "WebApplication", "EliteSetupForm", "MessageBox", "Process.", "ProcessStartInfo" })
        {
            Assert.DoesNotContain(forbidden, main, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The work happens in out-of-line methods. Without <c>NoInlining</c>
    /// the JIT is free to fold <c>Run</c> (or the relaunch helper, which
    /// mentions <c>System.Diagnostics.Process</c>) into <c>Main</c>, which
    /// reinstates the exact failure mode the split avoids.
    /// </summary>
    [Theory]
    [InlineData("private static void Run(int attempt)")]
    [InlineData("private static bool TryRelaunchSelf(int nextAttempt)")]
    public void OutOfLineStartupMethods_AreMarkedNoInlining(string declaration)
    {
        var source = ReadProgramSource();

        var declaredAt = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(declaredAt > 0, $"Expected Program.cs to declare '{declaration}'.");

        var attributeAt = source.LastIndexOf("[MethodImpl(MethodImplOptions.NoInlining)]", declaredAt, StringComparison.Ordinal);
        Assert.True(attributeAt > 0, $"Expected a NoInlining attribute before '{declaration}'.");
        Assert.True(declaredAt - attributeAt < 120, $"Expected the NoInlining attribute to sit directly on '{declaration}', not somewhere earlier in the file.");
    }

    /// <summary>
    /// Recovery is a fresh process, never a second call in this one: the
    /// runtime caches a failed bind for the life of the process, so a loop
    /// here would spin through the same cached failure and then crash
    /// anyway (measured 2026-09-18 with the first draft of this fix).
    /// </summary>
    [Fact]
    public void Recovery_RelaunchesAFreshProcess_RatherThanLoopingInThisOne()
    {
        var source = ReadProgramSource();
        var main = MainBody(source);

        Assert.Contains("Run(attempt);", main, StringComparison.Ordinal);
        Assert.Contains("TryRelaunchSelf(attempt + 1)", main, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", main, StringComparison.Ordinal);
        Assert.DoesNotContain("while (", main, StringComparison.Ordinal);
        Assert.Contains("StartupAttemptSwitch + nextAttempt", source, StringComparison.Ordinal);
        Assert.Contains("Environment.GetCommandLineArgs()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Bounded, and only for the load-failure family. An unbounded loop
    /// would turn a genuinely missing file into a silent hang; catching
    /// everything would retry real bugs.
    /// </summary>
    [Fact]
    public void Retry_IsBounded_AndFiltersToAssemblyLoadFailures()
    {
        var source = ReadProgramSource();
        var main = MainBody(source);

        Assert.Contains("private const int StartupLoadAttempts = 5;", source, StringComparison.Ordinal);
        Assert.Contains("attempt < StartupLoadAttempts && IsTransientAssemblyLoadFailure(ex)", main, StringComparison.Ordinal);
        Assert.Contains("e is FileNotFoundException or FileLoadException or BadImageFormatException", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A retry waits for the real condition (the binaries openable the way
    /// the loader opens them) rather than a guessed fixed delay, and that
    /// wait is itself bounded.
    /// </summary>
    [Fact]
    public void Retry_WaitsForOwnBinariesToBeOpenable_WithABoundedWait()
    {
        var source = ReadProgramSource();

        Assert.Contains("WaitForOwnBinariesToBeOpenable(StartupLoadRetryWait);", MainBody(source), StringComparison.Ordinal);
        Assert.Contains("FileShare.Read | FileShare.Delete", source, StringComparison.Ordinal);
        Assert.Contains("while (stopwatch.Elapsed < wait)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The last attempt is left to crash loudly. If the relaunch itself
    /// cannot be started, the original failure is rethrown rather than
    /// swallowed - a silent exit here would look exactly like the bug.
    /// </summary>
    [Fact]
    public void WhenRelaunchIsImpossible_TheOriginalFailureIsRethrown()
    {
        var main = MainBody(ReadProgramSource());

        Assert.Contains("if (!TryRelaunchSelf(attempt + 1))", main, StringComparison.Ordinal);
        Assert.Contains("throw;", main, StringComparison.Ordinal);
    }
}
