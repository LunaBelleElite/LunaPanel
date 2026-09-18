using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Input;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// Drives the real <see cref="LatchRegistry"/> against a recording
/// <see cref="IKeyInjector"/> - the same pattern <c>MacroRunnerTests</c>
/// established, and for the same reason: every key event this feature would
/// ever send is observable as data, with no Win32 API anywhere near the
/// test.
///
/// <b>What this file exists to prove.</b> A latch's press is trivial; the
/// feature IS its five release rules (<c>ref/docs/latching-keys.md</c>).
/// Every one of the five has at least one test here that fires it
/// independently of the other four, so "four of five shipped" cannot look
/// like a pass.
/// </summary>
public class LatchRegistryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed record KeyEvent(bool IsDown, ScancodeInfo Key);

    private sealed class RecordingKeyInjector : IKeyInjector
    {
        public List<KeyEvent> Events { get; } = new();

        public void KeyDown(ScancodeInfo key) => Events.Add(new KeyEvent(true, key));

        public void KeyUp(ScancodeInfo key) => Events.Add(new KeyEvent(false, key));

        public int DownCount => Events.Count(e => e.IsDown);

        public int UpCount => Events.Count(e => !e.IsDown);
    }

    /// <summary>
    /// A settable clock. <see cref="GetUtcNow"/> is all the registry itself
    /// reads (it never schedules a timer - that is <see cref="LatchSweeper"/>'s
    /// job, tested separately).
    /// </summary>
    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = T0;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NullLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent entry) => Events.Add(entry);
    }

    private static ResolvedChord PlainKey(ushort scan) =>
        new(new ScancodeInfo(scan, false), Array.Empty<ScancodeInfo>(), $"0x{scan:X2}");

    private static ResolvedChord ShiftedKey(ushort modifier, ushort main) =>
        new(new ScancodeInfo(main, false), new[] { new ScancodeInfo(modifier, false) }, "Shift+X");

    private static (LatchRegistry Registry, RecordingKeyInjector Injector, SettableClock Clock, NullLog Log) Build()
    {
        var injector = new RecordingKeyInjector();
        var clock = new SettableClock();
        var log = new NullLog();
        return (new LatchRegistry(injector, clock, log), injector, clock, log);
    }

    // -------------------------------------------------------------------
    // The press, and rule 1 - the commander taps it again.
    // -------------------------------------------------------------------

    [Fact]
    public void Toggle_FirstTap_SendsExactlyOneKeyDown_AndNoKeyUp()
    {
        var (registry, injector, _, _) = Build();

        var outcome = registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        Assert.Equal(LatchRegistry.ToggleOutcome.Latched, outcome);
        Assert.Equal(1, injector.DownCount);
        Assert.Equal(0, injector.UpCount);
        Assert.Equal(new ScancodeInfo(0x11, false), injector.Events.Single().Key);
    }

    [Fact]
    public void Toggle_SecondTap_SendsExactlyOneKeyUp_AndNoSecondKeyDown()
    {
        var (registry, injector, _, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        var outcome = registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        Assert.Equal(LatchRegistry.ToggleOutcome.Released, outcome);
        Assert.Equal(1, injector.DownCount);
        Assert.Equal(1, injector.UpCount);
        Assert.False(injector.Events[1].IsDown);
        Assert.Equal(new ScancodeInfo(0x11, false), injector.Events[1].Key);
    }

    [Fact]
    public void Toggle_IsLatched_TracksTheKeyBetweenTaps()
    {
        var (registry, _, _, _) = Build();

        Assert.False(registry.IsLatched("dev", "SecondaryFire"));
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        Assert.True(registry.IsLatched("dev", "SecondaryFire"));
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        Assert.False(registry.IsLatched("dev", "SecondaryFire"));
    }

    /// <summary>
    /// The whole point of a latch: a key held down must NOT hold the caller.
    /// A macro run - or any other press - happening while a latch is held
    /// leaves the latch exactly as it was, because the latch is a property
    /// of the key, not of whatever ran afterwards.
    /// </summary>
    [Fact]
    public void Toggle_LatchedKey_SurvivesAnUnrelatedMacroRunningAgainstTheSameInjector()
    {
        var (registry, injector, _, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        // Exactly what MacroRunner.PressChordOnceAsync does to this same
        // injector for one unrelated chord: down, then up.
        injector.KeyDown(new ScancodeInfo(0x20, false));
        injector.KeyUp(new ScancodeInfo(0x20, false));

        Assert.True(registry.IsLatched("dev", "SecondaryFire"));
        Assert.True(registry.Any);

        // And the latched key is still the one released when it is finally
        // tapped again - the macro's own key never became the registry's.
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        Assert.Equal(new ScancodeInfo(0x11, false), injector.Events[^1].Key);
        Assert.False(injector.Events[^1].IsDown);
    }

    /// <summary>
    /// A release must be sent once and only once. The dangerous shape here
    /// is a second KeyUp arriving from a rule that fired after the commander
    /// already tapped it - harmless at the OS level, but it means the
    /// registry lost track of what it holds, which is exactly the state that
    /// makes a stuck key possible.
    /// </summary>
    [Fact]
    public void Release_Twice_NeverSendsASecondKeyUp()
    {
        var (registry, injector, _, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        var releasedByShutdown = registry.ReleaseAll(LatchRelease.ServerShutdown);

        Assert.Equal(0, releasedByShutdown);
        Assert.Equal(1, injector.UpCount);
    }

    /// <summary>
    /// A chord's modifiers stay down for as long as the main key does -
    /// DirectInput polls key STATE per frame, so a modifier released early
    /// would leave the game reading the unmodified key. Released in reverse
    /// order, matching <c>MacroRunner</c>'s own end-of-chord choreography.
    /// </summary>
    [Fact]
    public void Toggle_ChordWithAModifier_HoldsBothDown_AndReleasesMainKeyFirst()
    {
        var (registry, injector, _, _) = Build();

        registry.Toggle("dev", "SecondaryFire", ShiftedKey(0x2A, 0x11));

        Assert.Equal(2, injector.DownCount);
        Assert.Equal(0, injector.UpCount);
        Assert.Equal(new ScancodeInfo(0x2A, false), injector.Events[0].Key);
        Assert.Equal(new ScancodeInfo(0x11, false), injector.Events[1].Key);

        registry.Toggle("dev", "SecondaryFire", ShiftedKey(0x2A, 0x11));

        Assert.Equal(2, injector.UpCount);
        Assert.Equal(new ScancodeInfo(0x11, false), injector.Events[2].Key);
        Assert.Equal(new ScancodeInfo(0x2A, false), injector.Events[3].Key);
    }

    // -------------------------------------------------------------------
    // Rule 2 - the device's live channel drops.
    // -------------------------------------------------------------------

    [Fact]
    public void CloseChannel_ReleasesTheLatchThatDeviceMade()
    {
        var (registry, injector, _, _) = Build();
        var channel = registry.OpenChannel("dev");
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        var released = registry.CloseChannel(channel);

        Assert.Equal(1, released);
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.IsLatched("dev", "SecondaryFire"));
    }

    /// <summary>
    /// A page change closes the old stream and opens a new one
    /// (<c>connectLive()</c> in the client). If a late close of the OLD
    /// channel could release a latch made under the NEW one, a commander
    /// latching immediately after a page change would watch it drop for no
    /// visible reason.
    /// </summary>
    [Fact]
    public void CloseChannel_StaleChannelId_NeverReleasesALatchMadeUnderTheCurrentChannel()
    {
        var (registry, injector, _, _) = Build();
        var stale = registry.OpenChannel("dev");
        var current = registry.OpenChannel("dev");
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        var released = registry.CloseChannel(stale);

        Assert.Equal(0, released);
        Assert.Equal(0, injector.UpCount);
        Assert.True(registry.IsLatched("dev", "SecondaryFire"));

        // ...and the current channel still releases it, so the stale close
        // did not merely fail to release, it also left the live one intact.
        Assert.Equal(1, registry.CloseChannel(current));
    }

    [Fact]
    public void CloseChannel_NeverReleasesAnotherDevicesLatch()
    {
        var (registry, injector, _, _) = Build();
        var deviceA = registry.OpenChannel("A");
        registry.OpenChannel("B");
        registry.Toggle("A", "SecondaryFire", PlainKey(0x11));
        registry.Toggle("B", "PrimaryFire", PlainKey(0x12));

        var released = registry.CloseChannel(deviceA);

        Assert.Equal(1, released);
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.IsLatched("A", "SecondaryFire"));
        Assert.True(registry.IsLatched("B", "PrimaryFire"));
    }

    /// <summary>
    /// A latch made with no live channel open at all still latches - this
    /// project never refuses an action for uncertainty. It simply cannot be
    /// covered by rule 2, and the backstop is what covers it instead; that
    /// is stated here so the hole is a recorded property, not a surprise.
    /// </summary>
    [Fact]
    public void Toggle_WithNoChannelOpen_StillLatches_AndIsCoveredByTheBackstopInstead()
    {
        var (registry, injector, clock, _) = Build();

        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        Assert.True(registry.IsLatched("dev", "SecondaryFire"));

        Assert.Equal(0, registry.CloseChannel(Guid.NewGuid()));
        Assert.Equal(0, injector.UpCount);

        clock.Now = T0 + LatchDefaults.Backstop;
        Assert.Equal(1, registry.Sweep(eliteForeground: true, clock.Now));
        Assert.Equal(1, injector.UpCount);
    }

    // -------------------------------------------------------------------
    // Rule 3 - Elite loses foreground.
    // -------------------------------------------------------------------

    [Fact]
    public void Sweep_EliteNotForeground_ReleasesEveryLatch()
    {
        var (registry, injector, clock, _) = Build();
        registry.Toggle("A", "SecondaryFire", PlainKey(0x11));
        registry.Toggle("B", "PrimaryFire", PlainKey(0x12));

        var released = registry.Sweep(eliteForeground: false, clock.Now);

        Assert.Equal(2, released);
        Assert.Equal(2, injector.UpCount);
        Assert.False(registry.Any);
    }

    [Fact]
    public void Sweep_EliteStillForeground_WellInsideTheBackstop_ReleasesNothing()
    {
        var (registry, injector, clock, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        clock.Now = T0 + TimeSpan.FromSeconds(30);
        var released = registry.Sweep(eliteForeground: true, clock.Now);

        Assert.Equal(0, released);
        Assert.Equal(0, injector.UpCount);
        Assert.True(registry.IsLatched("dev", "SecondaryFire"));
    }

    // -------------------------------------------------------------------
    // Rule 4 - the server shuts down.
    // -------------------------------------------------------------------

    [Fact]
    public void ReleaseAll_ServerShutdown_ReleasesEveryLatchAcrossEveryDevice()
    {
        var (registry, injector, _, _) = Build();
        registry.Toggle("A", "SecondaryFire", PlainKey(0x11));
        registry.Toggle("B", "PrimaryFire", PlainKey(0x12));
        registry.Toggle("B", "ThirdThing", PlainKey(0x13));

        var released = registry.ReleaseAll(LatchRelease.ServerShutdown);

        Assert.Equal(3, released);
        Assert.Equal(3, injector.UpCount);
        Assert.False(registry.Any);
    }

    [Fact]
    public void ReleaseAll_CalledTwice_IsIdempotent_SecondCallSendsNothing()
    {
        var (registry, injector, _, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        Assert.Equal(1, registry.ReleaseAll(LatchRelease.ServerShutdown));
        Assert.Equal(0, registry.ReleaseAll(LatchRelease.ServerShutdown));
        Assert.Equal(1, injector.UpCount);
    }

    // -------------------------------------------------------------------
    // Rule 5 - the two-minute backstop.
    // -------------------------------------------------------------------

    /// <summary>
    /// Pinned against <see cref="LatchDefaults.Backstop"/> itself on the
    /// clock side, and against the literal two minutes the user ruled on in
    /// the assertion below - a test that only re-typed the constant on both
    /// sides would pass no matter what the constant said.
    /// </summary>
    [Fact]
    public void Backstop_IsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), LatchDefaults.Backstop);
    }

    [Fact]
    public void Sweep_OneTickBeforeTheBackstop_DoesNotRelease()
    {
        var (registry, injector, clock, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        clock.Now = T0 + LatchDefaults.Backstop - TimeSpan.FromMilliseconds(1);
        Assert.Equal(0, registry.Sweep(eliteForeground: true, clock.Now));
        Assert.Equal(0, injector.UpCount);
    }

    [Fact]
    public void Sweep_AtTheBackstop_ReleasesTheLatch_EvenWithEliteStillForeground()
    {
        var (registry, injector, clock, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        clock.Now = T0 + LatchDefaults.Backstop;
        Assert.Equal(1, registry.Sweep(eliteForeground: true, clock.Now));
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.Any);
    }

    /// <summary>
    /// The backstop is measured from each latch's OWN press, not from the
    /// first one - otherwise a key latched ninety seconds after another
    /// would be cut short by thirty seconds of somebody else's clock.
    /// </summary>
    [Fact]
    public void Sweep_AtTheBackstop_ReleasesOnlyTheLatchThatActuallyExpired()
    {
        var (registry, injector, clock, _) = Build();
        registry.Toggle("dev", "First", PlainKey(0x11));

        clock.Now = T0 + TimeSpan.FromMinutes(1);
        registry.Toggle("dev", "Second", PlainKey(0x12));

        clock.Now = T0 + LatchDefaults.Backstop;
        Assert.Equal(1, registry.Sweep(eliteForeground: true, clock.Now));
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.IsLatched("dev", "First"));
        Assert.True(registry.IsLatched("dev", "Second"));
    }

    // -------------------------------------------------------------------
    // Reporting - what the panel and the log see.
    // -------------------------------------------------------------------

    [Fact]
    public void LatchedActions_ReportsOnlyTheCallingDevicesOwnLatches()
    {
        var (registry, _, _, _) = Build();
        registry.Toggle("A", "SecondaryFire", PlainKey(0x11));
        registry.Toggle("B", "PrimaryFire", PlainKey(0x12));

        Assert.Equal(new[] { "SecondaryFire" }, registry.LatchedActions("A").OrderBy(a => a));
        Assert.Equal(new[] { "PrimaryFire" }, registry.LatchedActions("B").OrderBy(a => a));
        Assert.Empty(registry.LatchedActions("C"));
    }

    [Fact]
    public void Changed_FiresOnEveryLatchAndEveryRelease_SoTheLiveChannelCanRepaint()
    {
        var (registry, _, clock, _) = Build();
        var fired = 0;
        registry.Changed += () => fired++;

        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        Assert.Equal(1, fired);

        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        Assert.Equal(2, fired);

        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        clock.Now = T0 + LatchDefaults.Backstop;
        registry.Sweep(eliteForeground: true, clock.Now);
        Assert.Equal(4, fired);
    }

    [Fact]
    public void Changed_DoesNotFireForASweepThatReleasedNothing()
    {
        var (registry, _, clock, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
        var fired = 0;
        registry.Changed += () => fired++;

        registry.Sweep(eliteForeground: true, clock.Now);

        Assert.Equal(0, fired);
    }

    /// <summary>
    /// Every release names its rule in the log. A commander whose key let go
    /// on its own needs to be able to read WHICH of the five did it -
    /// "something released it" is the report that makes a real defect
    /// indistinguishable from the backstop doing its job.
    /// </summary>
    [Theory]
    [InlineData(nameof(LatchRelease.TappedAgain))]
    [InlineData(nameof(LatchRelease.ChannelClosed))]
    [InlineData(nameof(LatchRelease.ForegroundLost))]
    [InlineData(nameof(LatchRelease.ServerShutdown))]
    [InlineData(nameof(LatchRelease.Backstop))]
    public void Release_LogsWhichOfTheFiveRulesFired(string reasonName)
    {
        var (registry, _, clock, log) = Build();
        var channel = registry.OpenChannel("dev");
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));

        switch (reasonName)
        {
            case nameof(LatchRelease.TappedAgain):
                registry.Toggle("dev", "SecondaryFire", PlainKey(0x11));
                break;
            case nameof(LatchRelease.ChannelClosed):
                registry.CloseChannel(channel);
                break;
            case nameof(LatchRelease.ForegroundLost):
                registry.Sweep(eliteForeground: false, clock.Now);
                break;
            case nameof(LatchRelease.ServerShutdown):
                registry.ReleaseAll(LatchRelease.ServerShutdown);
                break;
            case nameof(LatchRelease.Backstop):
                clock.Now = T0 + LatchDefaults.Backstop;
                registry.Sweep(eliteForeground: true, clock.Now);
                break;
            default:
                throw new InvalidOperationException($"Unhandled reason '{reasonName}'.");
        }

        var line = Assert.Single(log.Events, e => e.Category == "Latch" && e.Message.Contains("Released", StringComparison.Ordinal));
        Assert.Contains(reasonName, line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sweep of the whole enum: every member of <see cref="LatchRelease"/>
    /// is reachable from a real release path above (five here plus
    /// <c>HoldReleased</c>, covered by <c>LatchRegistryHoldTests</c>). A
    /// member added without a route to fire it reddens here, which is the
    /// shape of defect "four of five rules shipped" actually takes.
    ///
    /// [2026-09-12] Supersedes this test's own name and count - before-value:
    /// exactly five members (<c>TappedAgain</c>, <c>ChannelClosed</c>,
    /// <c>ForegroundLost</c>, <c>ServerShutdown</c>, <c>Backstop</c>), the
    /// five release rules <c>ref/docs/latching-keys.md</c> named. The hold
    /// gesture's release (<c>ref/docs/latching-keys.md</c>'s hold-to-thrust
    /// extension) is a sixth, genuinely new release path - a pointerup
    /// ending a hold, distinct from <c>TappedAgain</c>'s second tap - not a
    /// weakening of the original five-rule claim.
    /// </summary>
    [Fact]
    public void LatchRelease_HasExactlyTheSixApprovedRules()
    {
        Assert.Equal(
            new[] { "TappedAgain", "ChannelClosed", "ForegroundLost", "ServerShutdown", "Backstop", "HoldReleased" },
            Enum.GetNames<LatchRelease>());
    }
}
