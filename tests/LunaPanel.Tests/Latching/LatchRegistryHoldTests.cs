using LunaPanel.Core.Bindings;
using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Input;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// <see cref="LatchRegistry.Hold"/>/<see cref="LatchRegistry.Release"/> - the
/// hold gesture's two idempotent entry points
/// (<c>ref/docs/latching-keys.md</c>'s hold-to-thrust extension), added
/// alongside <see cref="LatchRegistry.Toggle"/> rather than reusing it (a
/// duplicate pointerdown or an orphaned pointerup would flip
/// <see cref="LatchRegistry.Toggle"/> to the wrong state).
///
/// <b>What this file exists to prove, beyond "it presses and it releases":</b>
/// both calls share the SAME held-state dictionary <see cref="LatchRegistry.Toggle"/>
/// already uses, so the four generic safety rules - <c>ChannelClosed</c>,
/// <c>ForegroundLost</c>, <c>ServerShutdown</c>, <c>Backstop</c> - release a
/// hold-registered press exactly as they already release a toggled latch,
/// with zero changes to their own logic. This is the whole reason the plan
/// chose to extend <see cref="LatchRegistry"/> rather than build a second,
/// parallel key-down/key-up tracker - so this file proves the reuse actually
/// holds, for at least the two rules that matter live for a stuck thrust
/// key (<c>ForegroundLost</c>, <c>Backstop</c>), rather than merely asserting
/// the two new methods exist.
/// </summary>
public class LatchRegistryHoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed record KeyEvent(bool IsDown, ScancodeInfo Key);

    private sealed class RecordingKeyInjector : IKeyInjector
    {
        public List<KeyEvent> Events { get; } = new();

        public void KeyDown(ScancodeInfo key) => Events.Add(new KeyEvent(true, key));

        public void KeyUp(ScancodeInfo key) => Events.Add(new KeyEvent(false, key));

        public int DownCount => Events.Count(e => e.IsDown);

        public int UpCount => Events.Count(e => !e.IsDown);
    }

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

    private static (LatchRegistry Registry, RecordingKeyInjector Injector, SettableClock Clock, NullLog Log) Build()
    {
        var injector = new RecordingKeyInjector();
        var clock = new SettableClock();
        var log = new NullLog();
        return (new LatchRegistry(injector, clock, log), injector, clock, log);
    }

    // -------------------------------------------------------------------
    // The press.
    // -------------------------------------------------------------------

    [Fact]
    public void Hold_FirstCall_SendsExactlyOneKeyDown_AndNoKeyUp()
    {
        var (registry, injector, _, _) = Build();

        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        Assert.Equal(1, injector.DownCount);
        Assert.Equal(0, injector.UpCount);
        Assert.True(registry.IsLatched("dev", "ForwardThrust"));
    }

    /// <summary>
    /// The idempotency claim itself: a second <c>Hold</c> call while already
    /// held - the shape a duplicate pointerdown produces - must not re-send
    /// the key-down, and critically must NOT be read as "already held,
    /// release it" the way <see cref="LatchRegistry.Toggle"/> would.
    /// </summary>
    [Fact]
    public void Hold_CalledTwiceWhileHeld_IsIdempotent_SendsNoSecondKeyDown_AndDoesNotRelease()
    {
        var (registry, injector, _, _) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        Assert.Equal(1, injector.DownCount);
        Assert.Equal(0, injector.UpCount);
        Assert.True(registry.IsLatched("dev", "ForwardThrust"));
    }

    // -------------------------------------------------------------------
    // The release.
    // -------------------------------------------------------------------

    [Fact]
    public void Release_WhileHeld_SendsExactlyOneKeyUp()
    {
        var (registry, injector, _, _) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        registry.Release("dev", "ForwardThrust");

        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.IsLatched("dev", "ForwardThrust"));
    }

    /// <summary>
    /// The idempotency claim on the other side: a pointerup with no matching
    /// pointerdown (or a duplicate pointerup) must never send a stray
    /// key-up, and must never error.
    /// </summary>
    [Fact]
    public void Release_WhileNotHeld_IsIdempotent_SendsNothing_AndDoesNotError()
    {
        var (registry, injector, _, _) = Build();

        registry.Release("dev", "ForwardThrust");

        Assert.Equal(0, injector.UpCount);
        Assert.Equal(0, injector.DownCount);
    }

    [Fact]
    public void Release_CalledTwiceAfterAHold_SecondCallSendsNoSecondKeyUp()
    {
        var (registry, injector, _, _) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));
        registry.Release("dev", "ForwardThrust");

        registry.Release("dev", "ForwardThrust");

        Assert.Equal(1, injector.UpCount);
    }

    /// <summary>
    /// <see cref="LatchRelease.HoldReleased"/>, not
    /// <see cref="LatchRelease.TappedAgain"/> - a pointerup ending a hold is
    /// a different gesture from a second tap toggling a latch off, even
    /// though both end in the same key going up. A commander reading the
    /// diagnostics log has to be able to tell them apart.
    /// </summary>
    [Fact]
    public void Release_LogsUnderHoldReleased_NotTappedAgain()
    {
        var (registry, _, _, log) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        registry.Release("dev", "ForwardThrust");

        var line = Assert.Single(log.Events, e => e.Category == "Latch" && e.Message.Contains("Released", StringComparison.Ordinal));
        Assert.Contains(nameof(LatchRelease.HoldReleased), line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(LatchRelease.TappedAgain), line.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------
    // The four generic safety rules, proven against a HOLD-registered
    // press rather than a Toggle-registered one - the whole reason this
    // feature reuses LatchRegistry instead of a second tracker.
    // -------------------------------------------------------------------

    /// <summary>
    /// Rule 3 - Elite loses foreground. Live-relevant for a thrust key: an
    /// alt-tab away from the game must not leave a control surface thrusting
    /// into whatever the commander tabbed to.
    /// </summary>
    [Fact]
    public void Sweep_EliteNotForeground_ReleasesAHoldRegisteredPress_ExactlyAsItReleasesALatch()
    {
        var (registry, injector, clock, _) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        var released = registry.Sweep(eliteForeground: false, clock.Now);

        Assert.Equal(1, released);
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.Any);
    }

    /// <summary>
    /// Rule 5 - the two-minute backstop. Live-relevant for a thrust key: a
    /// dropped pointerup (screen sleep, a crashed tab, a lost connection)
    /// must not leave the ship thrusting forever.
    /// </summary>
    [Fact]
    public void Sweep_AtTheBackstop_ReleasesAHoldRegisteredPress_ExactlyAsItReleasesALatch()
    {
        var (registry, injector, clock, _) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        clock.Now = T0 + LatchDefaults.Backstop;
        var released = registry.Sweep(eliteForeground: true, clock.Now);

        Assert.Equal(1, released);
        Assert.Equal(1, injector.UpCount);
        Assert.False(registry.Any);
    }

    /// <summary>Rule 2 - the device's live channel drops.</summary>
    [Fact]
    public void CloseChannel_ReleasesAHoldRegisteredPress_ExactlyAsItReleasesALatch()
    {
        var (registry, injector, _, _) = Build();
        var channel = registry.OpenChannel("dev");
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        var released = registry.CloseChannel(channel);

        Assert.Equal(1, released);
        Assert.Equal(1, injector.UpCount);
    }

    /// <summary>Rule 4 - the server shuts down.</summary>
    [Fact]
    public void ReleaseAll_ReleasesAHoldRegisteredPress_ExactlyAsItReleasesALatch()
    {
        var (registry, injector, _, _) = Build();
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        var released = registry.ReleaseAll(LatchRelease.ServerShutdown);

        Assert.Equal(1, released);
        Assert.Equal(1, injector.UpCount);
    }

    /// <summary>
    /// A hold and a latch coexisting for two different actions on the same
    /// device must not interfere with each other - the dictionary is shared,
    /// but each entry is keyed by (device, action) exactly as before.
    /// </summary>
    [Fact]
    public void Hold_AndToggle_OnDifferentActions_TrackIndependently()
    {
        var (registry, injector, _, _) = Build();
        registry.Toggle("dev", "SecondaryFire", PlainKey(0x12));
        registry.Hold("dev", "ForwardThrust", PlainKey(0x11));

        registry.Release("dev", "ForwardThrust");

        Assert.False(registry.IsLatched("dev", "ForwardThrust"));
        Assert.True(registry.IsLatched("dev", "SecondaryFire"));
        Assert.Equal(1, injector.UpCount);
    }
}
