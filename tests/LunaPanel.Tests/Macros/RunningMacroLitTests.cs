using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// How a macro that is running right now looks (2026-09-10,
/// <c>ref/docs/lit-state.md</c>). Same shape as
/// <c>LatchedLitTests</c>, guarding the same failure from the other side: a
/// button that is doing something the commander cannot see. The commander
/// asked for this knowing most runs are brief - "sometimes this will be very
/// briefly, but if a commander builds a long macro, it could be 5-10
/// seconds".
/// </summary>
public class RunningMacroLitTests
{
    private static LayoutPage PageWith(params LayoutSlot[] slots) =>
        new("SHIP", "t6", slots, Array.Empty<LayoutSlot>());

    private static IReadOnlySet<string> Running(params string[] macroIds) =>
        macroIds.ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<SlotLitState> Levels(params (int Index, SlotLitLevel Level)[] levels) =>
        levels.Select(l => new SlotLitState(l.Index, l.Level)).ToList();

    [Fact]
    public void Apply_SlotNamingARunningMacro_BecomesFull()
    {
        var page = PageWith(new LayoutSlot(0, null, "disembark", null, null));

        var result = RunningMacroLit.Apply(Levels((0, SlotLitLevel.Off)), page, Running("disembark"));

        Assert.Equal(SlotLitLevel.Full, Assert.Single(result).Level);
    }

    [Fact]
    public void Apply_SlotNamingAMacroThatIsNotRunning_IsLeftAlone()
    {
        var page = PageWith(
            new LayoutSlot(0, null, "disembark", null, null),
            new LayoutSlot(1, null, "request-docking", null, null));

        var result = RunningMacroLit.Apply(Levels((0, SlotLitLevel.Off), (1, SlotLitLevel.Off)), page, Running("disembark"));

        Assert.Equal(SlotLitLevel.Full, result.Single(l => l.Index == 0).Level);
        Assert.Equal(SlotLitLevel.Off, result.Single(l => l.Index == 1).Level);
    }

    /// <summary>
    /// The discriminating case: a macro id and an action name are different
    /// namespaces, and matching an ACTION against the running macro ids would
    /// light a button that has nothing to do with the run. Both slots below
    /// carry the same string; only the one that names it as a MACRO lights.
    /// </summary>
    [Fact]
    public void Apply_ActionSlotWhoseActionNameEqualsARunningMacroId_IsLeftAlone()
    {
        var page = PageWith(
            new LayoutSlot(0, null, "disembark", null, null),
            new LayoutSlot(1, "disembark", null, null, null));

        var result = RunningMacroLit.Apply(Levels((0, SlotLitLevel.Off), (1, SlotLitLevel.Off)), page, Running("disembark"));

        Assert.Equal(SlotLitLevel.Full, result.Single(l => l.Index == 0).Level);
        Assert.Equal(SlotLitLevel.Off, result.Single(l => l.Index == 1).Level);
    }

    /// <summary>
    /// The overwhelmingly common path - nothing is running - allocates
    /// nothing and hands back the very same instance.
    /// </summary>
    [Fact]
    public void Apply_NothingRunning_ReturnsTheSameInstanceUnchanged()
    {
        var page = PageWith(new LayoutSlot(0, null, "disembark", null, null));
        var levels = Levels((0, SlotLitLevel.Partial));

        Assert.Same(levels, RunningMacroLit.Apply(levels, page, Running()));
    }

    /// <summary>
    /// A macro can be started from one page and keep running while the
    /// commander taps to another (an automatic vessel-context switch can do
    /// that on its own), so a page with no button for it must neither throw
    /// nor invent a slot.
    /// </summary>
    [Fact]
    public void Apply_RunningMacroWithNoButtonOnThisPage_ChangesNothing()
    {
        var page = PageWith(new LayoutSlot(3, "LandingGearToggle", null, null, null));

        var result = RunningMacroLit.Apply(Levels((3, SlotLitLevel.Off)), page, Running("disembark"));

        Assert.Equal(SlotLitLevel.Off, Assert.Single(result).Level);
    }
}
