using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// "Pages declare their context", and "when no page matches: do nothing"
/// (<c>ref/docs/vessel-context.md</c>).
/// </summary>
public class ContextPageSelectorTests
{
    private const int InSrvBit = 26;
    private const int InMainShipBit = 24;
    private const int OnFootBit2 = 0;

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static StatusSnapshot Snapshot(uint flags, uint? flags2 = 0u) =>
        new(flags, flags2, null, GameRunning: true, SignedIn: true);

    private static LayoutPage Page(string name, params string[] showWhen) =>
        new(name, "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), showWhen.Length == 0 ? null : showWhen);

    private static Layout LayoutOf(params LayoutPage[] pages) => new(1, pages);

    private static readonly VesselContext Nomad = new(VesselContextKind.Srv, "lander01");
    private static readonly VesselContext MainShip = new(VesselContextKind.MainShip, null);
    private static readonly VesselContext OnFoot = new(VesselContextKind.OnFoot, null);

    [Fact]
    public void Select_APageDeclaringTheCurrentContext_IsChosenByIndex()
    {
        var layout = LayoutOf(Page("SHIP"), Page("SRV", "InSrv"));

        var index = ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog());

        Assert.Equal(1, index);
    }

    /// <summary>
    /// A page with no <c>showWhen</c> is context-free: always available by
    /// hand, and never a page automatic switching moves TO. If it matched,
    /// the first page - typically the context-free starter page - would be
    /// the answer for every context, and this feature would do nothing but
    /// jump to page 0 forever.
    /// </summary>
    [Fact]
    public void Select_AContextFreePageBeforeAMatchingOne_IsSkipped_NotChosen()
    {
        var layout = LayoutOf(Page("SHIP"), Page("SRV", "InSrv"));

        var index = ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog());

        Assert.NotEqual(0, index);
    }

    [Fact]
    public void Select_OnlyContextFreePages_MatchesNothing()
    {
        var layout = LayoutOf(Page("SHIP"), Page("PANEL 2"));

        Assert.Null(ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }

    /// <summary>
    /// An empty <c>showWhen</c> array declares no context at all, and must
    /// behave exactly as an absent one - not as a list of zero conditions
    /// that vacuously ANDs to true and therefore matches every context.
    /// </summary>
    [Fact]
    public void Select_AnEmptyShowWhenArray_IsContextFree_NotAMatchForEverything()
    {
        var layout = LayoutOf(new LayoutPage("SHIP", "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), Array.Empty<string>()));

        Assert.Null(ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }

    /// <summary>
    /// The coordinator's ruling, 2026-09-08: a context does not remember
    /// which of its pages was last used. Returning to a context lands on its
    /// FIRST matching page.
    /// </summary>
    [Fact]
    public void Select_TwoPagesDeclareTheSameContext_TheFirstInDeclarationOrderWins()
    {
        var layout = LayoutOf(Page("SHIP"), Page("SRV A", "InSrv"), Page("SRV B", "InSrv"));

        Assert.Equal(1, ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }

    [Fact]
    public void Select_NoPageDeclaresTheCurrentContext_IsNull_SoNothingMoves()
    {
        var layout = LayoutOf(Page("SHIP", "InMainShip"), Page("FOOT", "OnFoot"));

        Assert.Null(ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }

    /// <summary>
    /// The coordinator's ruling, 2026-09-08, made explicit: a
    /// <c>showWhen</c> naming a vessel the journal has never mentioned takes
    /// the "no page matches" path.
    /// </summary>
    [Fact]
    public void Select_APageNamingAVesselTheJournalNeverMentioned_TakesTheNoPageMatchesPath()
    {
        var layout = LayoutOf(Page("SHIP"), Page("SCARAB", "InSrv", "Vessel:testbuggy"));

        Assert.Null(ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }

    [Fact]
    public void Select_TheNomadPageAmongSrvPages_IsChosenOverTheScarabsOne()
    {
        var layout = LayoutOf(
            Page("SHIP"),
            Page("SCARAB", "InSrv", "Vessel:testbuggy"),
            Page("NOMAD", "InSrv", "Vessel:lander01"));

        Assert.Equal(2, ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }

    [Fact]
    public void Select_TheOnFootContext_ChoosesTheOnFootPage()
    {
        var layout = LayoutOf(Page("SHIP", "InMainShip"), Page("FOOT", "OnFoot"));

        Assert.Equal(1, ContextPageSelector.Select(layout, Snapshot(0u, 1u << OnFootBit2), OnFoot, new CapturingDiagnosticLog()));
    }

    [Fact]
    public void Select_TheMainShipContext_ChoosesTheMainShipPage()
    {
        var layout = LayoutOf(Page("FOOT", "OnFoot"), Page("SHIP", "InMainShip"));

        Assert.Equal(1, ContextPageSelector.Select(layout, Snapshot(1u << InMainShipBit), MainShip, new CapturingDiagnosticLog()));
    }

    /// <summary>
    /// A layout file is runtime data from the player's machine. An
    /// unparseable <c>showWhen</c> skips that page rather than taking down
    /// the whole selection - and says so, because silently skipping it is
    /// exactly how "the wrong page keeps appearing" becomes unexplainable.
    /// </summary>
    [Fact]
    public void Select_APageWithAnUnknownConditionName_IsSkipped_AndWarnedAbout()
    {
        var log = new CapturingDiagnosticLog();
        var layout = LayoutOf(Page("BROKEN", "NotAFlagAtAll"), Page("SRV", "InSrv"));

        var index = ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, log);

        Assert.Equal(1, index);
        var warning = Assert.Single(log.Events);
        Assert.Equal(DiagnosticLevel.Warn, warning.Level);
        Assert.Equal("Layout", warning.Category);
        Assert.Contains("BROKEN", warning.Message, StringComparison.Ordinal);
        Assert.Contains("NotAFlagAtAll", warning.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_EveryPageParsesCleanly_LogsNothing()
    {
        var log = new CapturingDiagnosticLog();
        var layout = LayoutOf(Page("SHIP", "InMainShip"), Page("SRV", "InSrv"));

        ContextPageSelector.Select(layout, Snapshot(1u << InSrvBit), Nomad, log);

        Assert.Empty(log.Events);
    }

    [Fact]
    public void Select_AnEmptyLayout_IsNull()
    {
        Assert.Null(ContextPageSelector.Select(LayoutOf(), Snapshot(1u << InSrvBit), Nomad, new CapturingDiagnosticLog()));
    }
}
