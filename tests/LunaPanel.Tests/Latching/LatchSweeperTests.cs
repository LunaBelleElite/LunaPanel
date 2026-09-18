using System.Diagnostics;
using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Input;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// The polling half of release rules 3 and 5. <see cref="LatchRegistryTests"/>
/// proves the registry releases correctly when asked; this file proves
/// something actually asks, on a timer, in production - the distinction that
/// separates "the rule is implemented" from "the rule fires".
/// </summary>
public class LatchSweeperTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingKeyInjector : IKeyInjector
    {
        public int DownCount { get; private set; }

        public int UpCount { get; private set; }

        public void KeyDown(ScancodeInfo key) => DownCount++;

        public void KeyUp(ScancodeInfo key) => UpCount++;
    }

    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = T0;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NullLog : IDiagnosticLog
    {
        public void Write(DiagnosticEvent diagnosticEvent)
        {
        }
    }

    private static ResolvedChord PlainKey(ushort scan) =>
        new(new ScancodeInfo(scan, false), Array.Empty<ScancodeInfo>(), $"0x{scan:X2}");

    private static (LatchRegistry Registry, RecordingKeyInjector Injector, SettableClock Clock) Build()
    {
        var injector = new RecordingKeyInjector();
        var clock = new SettableClock();
        return (new LatchRegistry(injector, clock, new NullLog()), injector, clock);
    }

    /// <summary>
    /// The foreground check costs a real Win32 round trip
    /// (<c>OpenProcess</c>/<c>OpenProcessToken</c>/<c>GetTokenInformation</c>,
    /// see <c>ref/docs/injection.md</c>). Spending it four times a second
    /// forever on a server holding nothing would be pure waste, so this pins
    /// that the predicate is not even evaluated when nothing is latched -
    /// not merely that its answer is ignored.
    /// </summary>
    [Fact]
    public void Tick_NothingLatched_NeverEvaluatesTheForegroundPredicate()
    {
        var (registry, _, clock) = Build();
        var asked = 0;

        var released = LatchSweeper.Tick(registry, () => { asked++; return false; }, clock);

        Assert.Equal(0, released);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Tick_SomethingLatched_EvaluatesTheForegroundPredicate()
    {
        var (registry, _, clock) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        var asked = 0;

        LatchSweeper.Tick(registry, () => { asked++; return true; }, clock);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void Tick_EliteNotForeground_ReleasesTheLatch()
    {
        var (registry, injector, clock) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        Assert.Equal(1, LatchSweeper.Tick(registry, () => false, clock));
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.Any);
    }

    [Fact]
    public void Tick_ForegroundStillElite_ButPastTheBackstop_ReleasesTheLatch()
    {
        var (registry, injector, clock) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        clock.Now = T0 + LatchDefaults.Backstop;

        Assert.Equal(1, LatchSweeper.Tick(registry, () => true, clock));
        Assert.Equal(1, injector.UpCount);
    }

    [Fact]
    public void Tick_ForegroundStillElite_InsideTheBackstop_ReleasesNothing()
    {
        var (registry, injector, clock) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        clock.Now = T0 + TimeSpan.FromSeconds(45);

        Assert.Equal(0, LatchSweeper.Tick(registry, () => true, clock));
        Assert.Equal(0, injector.UpCount);
        Assert.True(registry.Any);
    }

    /// <summary>
    /// Drives the real loop on the real system clock at a deliberately tiny
    /// interval. <see cref="LatchSweeper.Tick"/>'s own tests above cannot
    /// prove anything ever CALLS it; this can, and it is the difference
    /// between rule 3 being implemented and rule 3 firing.
    ///
    /// Uses <see cref="TimeProvider.System"/> rather than a fake, because
    /// what is under test here is precisely that the loop's own delay
    /// elapses and comes back round - a fake timer would prove only that a
    /// callback this test itself triggered was wired up.
    /// </summary>
    [Fact]
    public async Task RunAsync_EliteLosesForeground_ReleasesTheLatchWithoutAnybodyAskingIt()
    {
        var injector = new RecordingKeyInjector();
        var registry = new LatchRegistry(injector, TimeProvider.System, new NullLog());
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        using var cts = new CancellationTokenSource();
        var loop = LatchSweeper.RunAsync(
            registry,
            eliteIsForeground: () => false,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(5),
            cts.Token);

        var deadline = Stopwatch.StartNew();
        while (registry.Any && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(5);
        }

        await cts.CancelAsync();
        await loop;

        Assert.False(registry.Any);
        Assert.Equal(1, injector.UpCount);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsCleanly_WithoutThrowing()
    {
        var (registry, _, _) = Build();
        using var cts = new CancellationTokenSource();

        var loop = LatchSweeper.RunAsync(registry, () => true, TimeProvider.System, TimeSpan.FromMilliseconds(5), cts.Token);
        await cts.CancelAsync();

        await loop;
        Assert.True(loop.IsCompletedSuccessfully);
    }
}
