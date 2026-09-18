using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Pins <see cref="LayoutParker.ChangeTemplate"/>: shrinking parks
/// out-of-range slots, growing restores parked slots that now fit, and the
/// function is pure - calling it (a "preview") never mutates the page it
/// was handed, and calling it twice on the same inputs produces the same
/// result, which is exactly what lets the editor preview a shrink before
/// the user confirms it using the very same call that applies it.
/// </summary>
public class LayoutParkerTests
{
    private static PanelTemplate Template(string id)
    {
        Assert.True(Templates.TryGet(id, out var template), $"Expected template '{id}' to exist.");
        return template!;
    }

    [Fact]
    public void ChangeTemplate_Shrink_MovesOutOfRangeActiveSlotsToParked()
    {
        var page = new LayoutPage(
            "SHIP",
            "t12",
            new[]
            {
                new LayoutSlot(0, "LandingGearToggle", null, null, null),
                new LayoutSlot(5, "ToggleCargoScoop", null, null, null),
                new LayoutSlot(8, "ShipSpotLightToggle", null, null, null),
            },
            Array.Empty<LayoutSlot>());

        var result = LayoutParker.ChangeTemplate(page, Template("t6"));

        Assert.Equal("t6", result.TemplateId);
        Assert.Equal(new[] { 0, 5 }, result.Slots.Select(s => s.Index));
        Assert.Equal(new[] { 8 }, result.Parked.Select(s => s.Index));
        Assert.Equal("ShipSpotLightToggle", result.Parked.Single().Action);
    }

    [Fact]
    public void ChangeTemplate_SlotAtExactlyTheNewSlotCount_IsParked_NotKeptActive()
    {
        // t6's valid range is [0, 6) - index 6 is exactly one past the last
        // valid index, the precise boundary this pure function must get right.
        var page = new LayoutPage("SHIP", "t12", new[] { new LayoutSlot(6, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>());

        var result = LayoutParker.ChangeTemplate(page, Template("t6"));

        Assert.Empty(result.Slots);
        Assert.Equal(new[] { 6 }, result.Parked.Select(s => s.Index));
    }

    [Fact]
    public void ChangeTemplate_SlotAtExactlyTheLastValidIndex_StaysActive()
    {
        // t6's valid range is [0, 6) - index 5 is the last valid index.
        var page = new LayoutPage("SHIP", "t12", new[] { new LayoutSlot(5, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>());

        var result = LayoutParker.ChangeTemplate(page, Template("t6"));

        Assert.Equal(new[] { 5 }, result.Slots.Select(s => s.Index));
        Assert.Empty(result.Parked);
    }

    [Fact]
    public void ChangeTemplate_Grow_RestoresOnlyParkedSlotsThatNowFit()
    {
        var page = new LayoutPage(
            "SHIP",
            "t6",
            new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) },
            new[]
            {
                new LayoutSlot(7, "ToggleCargoScoop", null, null, null),
                new LayoutSlot(10, "ShipSpotLightToggle", null, null, null),
            });

        // t9 has 9 slots [0,9) - index 7 now fits, index 10 still doesn't.
        var result = LayoutParker.ChangeTemplate(page, Template("t9"));

        Assert.Equal("t9", result.TemplateId);
        Assert.Equal(new[] { 0, 7 }, result.Slots.Select(s => s.Index));
        Assert.Equal(new[] { 10 }, result.Parked.Select(s => s.Index));
    }

    [Fact]
    public void ChangeTemplate_DoesNotMutateTheOriginalPage()
    {
        var originalSlots = new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null), new LayoutSlot(8, "ToggleCargoScoop", null, null, null) };
        var page = new LayoutPage("SHIP", "t12", originalSlots, Array.Empty<LayoutSlot>());

        LayoutParker.ChangeTemplate(page, Template("t6"));

        Assert.Equal("t12", page.TemplateId);
        Assert.Equal(2, page.Slots.Count);
        Assert.Empty(page.Parked);
    }

    [Fact]
    public void ChangeTemplate_CalledTwiceOnTheSameInputs_ProducesTheSameResult_SoPreviewMatchesApply()
    {
        var page = new LayoutPage(
            "SHIP",
            "t12",
            new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null), new LayoutSlot(9, "ToggleCargoScoop", null, null, null) },
            Array.Empty<LayoutSlot>());

        var preview = LayoutParker.ChangeTemplate(page, Template("t6"));
        var applied = LayoutParker.ChangeTemplate(page, Template("t6"));

        Assert.Equal(preview.TemplateId, applied.TemplateId);
        Assert.Equal(preview.Slots.Select(s => s.Index), applied.Slots.Select(s => s.Index));
        Assert.Equal(preview.Parked.Select(s => s.Index), applied.Parked.Select(s => s.Index));
    }

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    [Fact]
    public void ChangeTemplateAndLog_SlotsActuallyMove_LogsOneLayoutCategoryEvent()
    {
        var page = new LayoutPage(
            "SHIP",
            "t12",
            new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null), new LayoutSlot(8, "ToggleCargoScoop", null, null, null) },
            Array.Empty<LayoutSlot>());
        var log = new CapturingDiagnosticLog();

        var result = LayoutParker.ChangeTemplateAndLog(page, Template("t6"), log);

        Assert.Equal(new[] { 8 }, result.Parked.Select(s => s.Index));
        var evt = Assert.Single(log.Events);
        Assert.Equal("Layout", evt.Category);
        Assert.Contains("1 slot(s) parked", evt.Message);
    }

    [Fact]
    public void ChangeTemplateAndLog_NothingMoves_LogsNothing()
    {
        // t9 -> t12 is a pure grow with no parked slots to restore and
        // nothing out of range - no event actually happened.
        var page = new LayoutPage("SHIP", "t9", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>());
        var log = new CapturingDiagnosticLog();

        LayoutParker.ChangeTemplateAndLog(page, Template("t12"), log);

        Assert.Empty(log.Events);
    }
}
