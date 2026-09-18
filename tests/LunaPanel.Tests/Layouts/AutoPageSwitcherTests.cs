using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.GameState;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// "It fires on a context CHANGE, and then gets out of the way"
/// (<c>ref/docs/vessel-context.md</c>) - the rule that decides whether this
/// feature is liked or hated.
/// </summary>
public class AutoPageSwitcherTests
{
    private const int InSrvBit = 26;
    private const int InMainShipBit = 24;
    private const int OnFootBit2 = 0;

    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static readonly IDiagnosticLog Log = new CapturingDiagnosticLog();

    private static StatusSnapshot Snapshot(uint flags, uint? flags2 = 0u) =>
        new(flags, flags2, null, GameRunning: true, SignedIn: true);

    private static StatusSnapshot InShip => Snapshot(1u << InMainShipBit);
    private static StatusSnapshot InSrv => Snapshot(1u << InSrvBit);
    private static StatusSnapshot OnFoot => Snapshot(0u, 1u << OnFootBit2);

    private static LayoutPage Page(string name, params string[] showWhen) =>
        new(name, "t6", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>(), showWhen.Length == 0 ? null : showWhen);

    // Page 0 context-free, page 1 the ship's, page 2 the Nomad's, page 3 on foot.
    private static readonly Layout ThreeContexts = new(1, new[]
    {
        Page("PANEL 1"),
        Page("SHIP", "InMainShip"),
        Page("NOMAD", "InSrv", "Vessel:lander01"),
        Page("FOOT", "OnFoot"),
    });

    private static AutoPageSwitcher SeededIn(StatusSnapshot? snapshot, string? vesselType = null) =>
        new(VesselContextResolver.Resolve(snapshot, vesselType));

    [Fact]
    public void Decide_AContextChange_SwitchesToThePageThatDeclaresIt()
    {
        var switcher = SeededIn(InShip);

        var target = switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "lander01", Log);

        Assert.Equal(2, target);
    }

    /// <summary>
    /// The commonest event by far: <c>Status.json</c> is rewritten on a
    /// steady ~11.8s idle heartbeat as well as on change (LC6). A switcher
    /// that answered the level rather than the change would re-assert on
    /// every one of those.
    /// </summary>
    [Fact]
    public void Decide_TheSameContextArrivingAgain_SwitchesNothing()
    {
        var switcher = SeededIn(InShip);

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InShip, null, Log));
    }

    /// <summary>
    /// The whole "gets out of the way" rule, driven end to end: a context
    /// change switches once; the commander then taps somewhere else; nothing
    /// drags them back, however many unchanged snapshots arrive.
    /// </summary>
    [Fact]
    public void Decide_AfterASwitch_AManualPageChoice_IsNeverOverridden()
    {
        var switcher = SeededIn(InShip);

        Assert.Equal(2, switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "lander01", Log));

        // The commander taps across to the context-free page 0 and stays
        // there while the game keeps rewriting Status.json.
        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InSrv, "lander01", Log));
        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InSrv, "lander01", Log));
        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InSrv, "lander01", Log));
    }

    [Fact]
    public void Decide_AfterAManualPageChoice_TheNextRealContextChangeStillSwitches()
    {
        var switcher = SeededIn(InShip);

        Assert.Equal(2, switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "lander01", Log));
        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InSrv, "lander01", Log));

        Assert.Equal(1, switcher.Decide(ThreeContexts, currentPageIndex: 0, InShip, "lander01", Log));
    }

    [Fact]
    public void Decide_NoPageDeclaresTheNewContext_SwitchesNothing()
    {
        var switcher = SeededIn(InShip);

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "testbuggy", Log));
    }

    /// <summary>
    /// 2026-09-19: a commander asked "why didn't it switch" with nothing in
    /// the log to answer from - <see cref="Decide"/> had zero logging on its
    /// normal path. This is the case that actually needed it: a REAL context
    /// change happened, but nothing matched, which is indistinguishable from
    /// "nothing happened at all" without this line.
    /// </summary>
    [Fact]
    public void Decide_NoPageDeclaresTheNewContext_LogsThatNothingMatched()
    {
        var log = new CapturingDiagnosticLog();
        var switcher = SeededIn(InShip);

        switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "testbuggy", log);

        var logged = Assert.Single(log.Events);
        Assert.Equal(DiagnosticLevel.Info, logged.Level);
        Assert.Equal("Layout", logged.Category);
        Assert.Contains("no page matches", logged.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The success side of the same 2026-09-19 gap - a switch that DID
    /// happen should also reach the log, for symmetry with the "nothing
    /// matched" case above.
    /// </summary>
    [Fact]
    public void Decide_AContextChangeThatSwitches_LogsIt()
    {
        var log = new CapturingDiagnosticLog();
        var switcher = SeededIn(InShip);

        switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "lander01", log);

        var logged = Assert.Single(log.Events);
        Assert.Equal(DiagnosticLevel.Info, logged.Level);
        Assert.Equal("Layout", logged.Category);
        Assert.Contains("switching to page", logged.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The far more common case (Status.json rewritten with nothing actually
    /// changed, LC6) must stay silent - logging every unchanged tick would
    /// flood this category. Confirms the new logging didn't accidentally
    /// widen to cover this case too.
    /// </summary>
    [Fact]
    public void Decide_TheSameContextArrivingAgain_LogsNothing()
    {
        var log = new CapturingDiagnosticLog();
        var switcher = SeededIn(InShip);

        switcher.Decide(ThreeContexts, currentPageIndex: 0, InShip, null, log);

        Assert.Empty(log.Events);
    }

    /// <summary>
    /// A context change that produces no switch is still CONSUMED. Without
    /// this, a commander who moved ship -> Scarab (no page) -> ship would get
    /// a backdated switch on the way back that they never asked for, because
    /// the switcher would still believe it was in the ship the whole time.
    /// </summary>
    [Fact]
    public void Decide_AContextChangeWithNoMatchingPage_IsStillConsumed()
    {
        var switcher = SeededIn(InShip);

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 1, InSrv, "testbuggy", Log));
        Assert.Equal(new VesselContext(VesselContextKind.Srv, "testbuggy"), switcher.LastContext);
    }

    /// <summary>
    /// Nomad -> Scarab is a context change even though both are
    /// <c>InSrv</c>: the two vehicles were separated at the commander's own
    /// request because they play differently.
    /// </summary>
    [Fact]
    public void Decide_ChangingSrvWithoutChangingTheFlag_IsAContextChange()
    {
        var switcher = SeededIn(InSrv, "testbuggy");

        Assert.Equal(2, switcher.Decide(ThreeContexts, currentPageIndex: 0, InSrv, "lander01", Log));
    }

    /// <summary>
    /// The journal naming a different SHIP is not a context change - the
    /// vessel type is part of the context only inside the SRV context. See
    /// <see cref="VesselContext.VesselType"/>.
    /// </summary>
    [Fact]
    public void Decide_TheJournalNamingADifferentShip_IsNotAContextChange()
    {
        var switcher = SeededIn(InShip, "krait_mkii");

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InShip, "python", Log));
    }

    [Fact]
    public void Decide_TheMatchingPageIsAlreadyShowing_SwitchesNothing()
    {
        var switcher = SeededIn(InShip);

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 2, InSrv, "lander01", Log));
    }

    [Fact]
    public void Decide_NoSnapshotYet_SwitchesNothing()
    {
        var switcher = SeededIn(InShip);

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 1, null, "lander01", Log));
    }

    /// <summary>
    /// The seed is what stops a device that connects mid-session being
    /// dragged anywhere: its first observation is the context it was already
    /// in, which is not a change.
    /// </summary>
    [Fact]
    public void Decide_SeededWithTheContextItOpenedIn_DoesNotSwitchOnItsFirstObservation()
    {
        var switcher = SeededIn(InSrv, "lander01");

        Assert.Null(switcher.Decide(ThreeContexts, currentPageIndex: 0, InSrv, "lander01", Log));
    }

    [Fact]
    public void Decide_SeededWithNoSnapshot_ThenTheGameStarts_Switches()
    {
        var switcher = SeededIn(null);

        Assert.Equal(3, switcher.Decide(ThreeContexts, currentPageIndex: 0, OnFoot, null, Log));
    }

    [Fact]
    public void LastContext_StartsAtTheSeed()
    {
        Assert.Equal(new VesselContext(VesselContextKind.MainShip, null), SeededIn(InShip).LastContext);
    }

    /// <summary>
    /// A page whose <c>showWhen</c> cannot be parsed must not stop the
    /// switcher deciding - the warning is <see cref="ContextPageSelector"/>'s
    /// job, and this pins that the exception never escapes to the live
    /// channel's push loop.
    /// </summary>
    [Fact]
    public void Decide_ALayoutContainingAnUnparseableShowWhen_DoesNotThrow()
    {
        var layout = new Layout(1, new[] { Page("BROKEN", "NotAFlagAtAll"), Page("SRV", "InSrv") });
        var switcher = SeededIn(InShip);

        Assert.Equal(1, switcher.Decide(layout, currentPageIndex: 0, InSrv, "lander01", Log));
    }
}
