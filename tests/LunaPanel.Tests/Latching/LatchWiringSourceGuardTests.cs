namespace LunaPanel.Tests.Latching;

/// <summary>
/// Source-scan pins over <c>ServerHostBuilder.cs</c>, using the same repo-root
/// walk <c>PanelClientSourceGuardTests</c> and <c>CoreNeedleGuardTests</c>
/// already use.
///
/// <b>Why these are source scans and not behavioural tests, stated plainly
/// rather than left as a silence.</b> Release rules 3 (Elite loses
/// foreground), 4 (the server shuts down) and 5 (the backstop) are all
/// proven to WORK by <c>LatchRegistryTests</c>/<c>LatchSweeperTests</c>,
/// which drive the real registry and the real sweep loop against a recording
/// injector. What those cannot prove is that the production host actually
/// wires them up - and a rule nothing calls is a rule that does not fire,
/// which is the exact failure <c>ref/docs/latching-keys.md</c> warns about
/// ("anything you cannot implement, say so loudly rather than shipping four
/// of five").
///
/// Driving them behaviourally through the real host would mean latching a key
/// through the production <c>Win32KeyInjector</c>. Every other press test in
/// this suite stops at the injection guard because Elite is not foreground,
/// but that is a property of the machine running the tests rather than a
/// guarantee - and a latch that slipped through would leave a key HELD DOWN
/// in a commander's live game rather than merely toggling something. So these
/// three pins read the wiring instead of exercising it, and the report for
/// this task says so.
/// </summary>
public class LatchWiringSourceGuardTests
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

    private static string ReadHostBuilderSource()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "LunaPanel.Server", "Hosting", "ServerHostBuilder.cs");
        Assert.True(File.Exists(path), $"Expected {path} to exist.");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Release rule 4, both halves. The clean path
    /// (<c>ApplicationStopping</c>) and the unclean one
    /// (<c>ProcessExit</c>, which still runs for an <c>Environment.Exit</c>
    /// or an unhandled exception) must both release - "including on an
    /// unclean exit if that can be arranged". <c>ReleaseAll</c> is
    /// idempotent precisely so registering both is safe.
    /// </summary>
    [Fact]
    public void HostBuilder_ReleasesEveryLatchOnShutdown_OnBothTheCleanAndUncleanPaths()
    {
        var source = ReadHostBuilderSource();

        Assert.Contains(
            "ApplicationStopping.Register(() => latches.ReleaseAll(LatchRelease.ServerShutdown))",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "AppDomain.CurrentDomain.ProcessExit += (_, _) => latches.ReleaseAll(LatchRelease.ServerShutdown)",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Release rules 3 and 5. The sweep loop must be started, and it must be
    /// fed the injection guard's own verdict rather than a hand-rolled second
    /// opinion about what "Elite is foreground" means - <c>CheckGuard</c> is
    /// exactly the verdict a <c>KeyDown</c> would get, without spending one.
    /// </summary>
    [Fact]
    public void HostBuilder_StartsTheSweeper_FedByTheInjectionGuardsOwnVerdict()
    {
        var source = ReadHostBuilderSource();

        Assert.Contains("LatchSweeper.RunAsync(", source, StringComparison.Ordinal);
        Assert.Contains(
            "eliteIsForeground: () => keyInjector.CheckGuard().Outcome == InjectionOutcome.Sent",
            source,
            StringComparison.Ordinal);
        Assert.Contains("LatchDefaults.SweepInterval", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Release rule 2. The live channel is the thing that knows a device went
    /// away, so it has to both claim the channel on the way in and hand it
    /// back on the way out - a registration with no matching close would make
    /// rule 2 unreachable while every latch still recorded a channel id.
    /// </summary>
    [Fact]
    public void HostBuilder_LiveChannel_OpensAndClosesItsLatchChannel()
    {
        var source = ReadHostBuilderSource();

        Assert.Contains("latchRegistry.OpenChannel(deviceId)", source, StringComparison.Ordinal);
        Assert.Contains("latchRegistry.CloseChannel(latchChannel)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A latch changing has to reach the live channel, or a held button would
    /// only light up on the next unrelated <c>Status.json</c> tick - up to
    /// ~11.8 seconds on a quiet ship (LC6), which is a held key looking
    /// un-held for long enough to matter.
    /// </summary>
    [Fact]
    public void HostBuilder_LiveChannel_PushesWhenALatchChanges()
    {
        var source = ReadHostBuilderSource();

        Assert.Contains("latchRegistry.Changed += OnLatchChanged", source, StringComparison.Ordinal);
        Assert.Contains("latchRegistry.Changed -= OnLatchChanged", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both endpoints that project a lit level must be handed this device's
    /// own latched actions. Missing it on either one produces the same
    /// specific defect: a button that lights on the first paint and goes dark
    /// on the next push, or the reverse.
    /// </summary>
    [Fact]
    public void HostBuilder_BothLitProjections_AreHandedThisDevicesLatchedActions()
    {
        var source = ReadHostBuilderSource();

        // [2026-09-10 - SUPERSEDED, and by a decision rather than a
        // convenience. These two assertions used to read:
        //
        //     Assert.Contains("latchRegistry.LatchedActions(deviceId));", ...)
        //     Assert.Contains("PanelLiveEndpoint.BuildState(liveLayout, livePageIndex, cat, snapshot, latchRegistry.LatchedActions(deviceId))", ...)
        //
        // Both needles ended on the CALL's closing parenthesis, so both went
        // red the moment a second lit override (a running macro's own glow,
        // 2026-09-10) was passed as the argument after the latch one - a needle
        // pinned to punctuation rather than to the claim. The claim is "both
        // projections are handed this device's latches", which the count
        // below states directly and which survives any further argument
        // being added to either call.]
        Assert.Equal(2, Occurrences(source, "latchRegistry.LatchedActions(deviceId)"));
        Assert.Contains(
            "PanelLiveEndpoint.BuildState(liveLayout, livePageIndex, cat, snapshot, latchRegistry.LatchedActions(deviceId)",
            source,
            StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
