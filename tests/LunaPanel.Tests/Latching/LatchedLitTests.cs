using LunaPanel.Core.GameState;
using LunaPanel.Core.Latching;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Latching;

/// <summary>
/// How a latched button looks. The failure this guards is the one
/// <c>ref/docs/latching-keys.md</c> names as equal in severity to a key that
/// cannot be released: a held key that is invisible on the panel, because
/// the commander cannot act on what they cannot see.
/// </summary>
public class LatchedLitTests
{
    private static LayoutPage PageWith(params LayoutSlot[] slots) =>
        new("SHIP", "t6", slots, Array.Empty<LayoutSlot>());

    private static IReadOnlySet<string> Latched(params string[] actions) =>
        actions.ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<SlotLitState> Levels(params (int Index, SlotLitLevel Level)[] levels) =>
        levels.Select(l => new SlotLitState(l.Index, l.Level)).ToList();

    [Fact]
    public void Apply_LatchSlotWhoseActionIsHeld_BecomesFull()
    {
        var page = PageWith(new LayoutSlot(0, "SecondaryFire", null, null, null, Latch: true));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Off)), page, Latched("SecondaryFire"));

        Assert.Equal(SlotLitLevel.Full, Assert.Single(result).Level);
    }

    /// <summary>
    /// A latch slot that is NOT currently held keeps whatever its own
    /// curated <c>lit</c> condition said. Forcing it to Off here would break
    /// every latchable control that also has a real engaged state.
    /// </summary>
    [Fact]
    public void Apply_LatchSlotNotCurrentlyHeld_KeepsItsOwnResolvedLevel()
    {
        var page = PageWith(new LayoutSlot(0, "LandingGearToggle", null, null, null, Latch: true));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Partial)), page, Latched());

        Assert.Equal(SlotLitLevel.Partial, Assert.Single(result).Level);
    }

    /// <summary>
    /// The discriminating case. A latch is keyed by action, and a slot that
    /// does NOT declare itself a latch must not light up just because the
    /// same action happens to be held from a different button - it would be
    /// a full-beam button that releases nothing when tapped.
    /// </summary>
    [Fact]
    public void Apply_NonLatchSlotNamingTheSameHeldAction_IsLeftAlone()
    {
        var page = PageWith(
            new LayoutSlot(0, "SecondaryFire", null, null, null, Latch: true),
            new LayoutSlot(1, "SecondaryFire", null, null, null, Latch: false));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Off), (1, SlotLitLevel.Off)), page, Latched("SecondaryFire"));

        Assert.Equal(SlotLitLevel.Full, result.Single(l => l.Index == 0).Level);
        Assert.Equal(SlotLitLevel.Off, result.Single(l => l.Index == 1).Level);
    }

    [Fact]
    public void Apply_LatchSlotNamingADifferentAction_IsLeftAlone()
    {
        var page = PageWith(
            new LayoutSlot(0, "SecondaryFire", null, null, null, Latch: true),
            new LayoutSlot(1, "PrimaryFire", null, null, null, Latch: true));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Off), (1, SlotLitLevel.Off)), page, Latched("SecondaryFire"));

        Assert.Equal(SlotLitLevel.Full, result.Single(l => l.Index == 0).Level);
        Assert.Equal(SlotLitLevel.Off, result.Single(l => l.Index == 1).Level);
    }

    [Fact]
    public void Apply_NothingLatched_ReturnsEveryLevelUnchanged()
    {
        var page = PageWith(new LayoutSlot(0, "SecondaryFire", null, null, null, Latch: true));
        var levels = Levels((0, SlotLitLevel.Partial));

        Assert.Same(levels, LatchedLit.Apply(levels, page, Latched()));
    }

    /// <summary>
    /// A latch survives the page moving under it (vessel-context switching),
    /// but a page that does not carry the latched button has nothing to
    /// light - and must not throw or invent a slot for it.
    /// </summary>
    [Fact]
    public void Apply_HeldActionNotOnThisPageAtAll_ChangesNothing()
    {
        var page = PageWith(new LayoutSlot(3, "LandingGearToggle", null, null, null, Latch: true));

        var result = LatchedLit.Apply(Levels((3, SlotLitLevel.Off)), page, Latched("SecondaryFire"));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    /// <summary>
    /// A macro slot can never be latched (<c>LayoutValidator</c> rejects it),
    /// so a null action must be skipped rather than matched against the held
    /// set - the shape that would otherwise be a null-reference on a layout
    /// hand-edited past the validator.
    /// </summary>
    [Fact]
    public void Apply_LatchFlagOnAMacroSlot_IsIgnoredRatherThanThrowing()
    {
        var page = PageWith(new LayoutSlot(0, null, "request-docking", null, null, Latch: true));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Off)), page, Latched("SecondaryFire"));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }

    // -------------------------------------------------------------------
    // The hold gesture (ref/docs/latching-keys.md's hold-to-thrust
    // extension) - a currently-held hold-slot lives in the same held-actions
    // set a toggled latch does, so it lights the same way, through the same
    // OR condition rather than a second code path.
    // -------------------------------------------------------------------

    [Fact]
    public void Apply_HoldSlotWhoseActionIsHeld_BecomesFull()
    {
        var page = PageWith(new LayoutSlot(0, "ForwardThrust", null, null, null, Hold: true));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Off)), page, Latched("ForwardThrust"));

        Assert.Equal(SlotLitLevel.Full, Assert.Single(result).Level);
    }

    [Fact]
    public void Apply_HoldSlotNotCurrentlyHeld_KeepsItsOwnResolvedLevel()
    {
        var page = PageWith(new LayoutSlot(0, "ForwardThrust", null, null, null, Hold: true));

        var result = LatchedLit.Apply(Levels((0, SlotLitLevel.Partial)), page, Latched());

        Assert.Equal(SlotLitLevel.Partial, Assert.Single(result).Level);
    }
}
