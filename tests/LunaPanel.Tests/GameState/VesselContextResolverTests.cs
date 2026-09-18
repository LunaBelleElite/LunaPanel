using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// <c>ref/docs/vessel-context.md</c>'s "Four contexts come free from
/// Status.json", plus the one composition the doc insists on: the flags say
/// <em>whether</em>, the journal says <em>which</em>.
/// </summary>
public class VesselContextResolverTests
{
    // Bit numbers restated here ON PURPOSE, and only here: these tests are
    // what would catch a wrong row in StatusVocabulary, so deriving them from
    // the same table the production code reads would make the assertion an
    // identity rather than a claim. Measured against the commander's own
    // running game 2026-09-07 (LC17) and Frontier's Journal Manual.
    private const int InMainShipBit = 24;
    private const int InFighterBit = 25;
    private const int InSrvBit = 26;
    private const int OnFootBit2 = 0;

    private static StatusSnapshot Snapshot(uint flags, uint? flags2 = 0u) =>
        new(flags, flags2, null, GameRunning: true, SignedIn: true);

    [Fact]
    public void Resolve_NullSnapshot_IsUnknown_AndNamesNoVessel()
    {
        var context = VesselContextResolver.Resolve(null, "lander01");

        Assert.Equal(VesselContextKind.Unknown, context.Kind);
        Assert.Null(context.VesselType);
    }

    [Fact]
    public void Resolve_NoContextFlagSet_IsUnknown()
    {
        var context = VesselContextResolver.Resolve(Snapshot(0u), "lander01");

        Assert.Equal(VesselContextKind.Unknown, context.Kind);
    }

    [Fact]
    public void Resolve_InMainShipBit_IsMainShip()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InMainShipBit), null);

        Assert.Equal(VesselContextKind.MainShip, context.Kind);
    }

    [Fact]
    public void Resolve_InFighterBit_IsFighter()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InFighterBit), null);

        Assert.Equal(VesselContextKind.Fighter, context.Kind);
    }

    [Fact]
    public void Resolve_InSrvBit_IsSrv()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), null);

        Assert.Equal(VesselContextKind.Srv, context.Kind);
    }

    [Fact]
    public void Resolve_OnFootBitOfFlags2_IsOnFoot()
    {
        var context = VesselContextResolver.Resolve(Snapshot(0u, 1u << OnFootBit2), null);

        Assert.Equal(VesselContextKind.OnFoot, context.Kind);
    }

    /// <summary>
    /// LC17, measured live in the Nomad: <c>Flags</c> bit 26 set and bit 24
    /// clear. The Nomad reports as an SRV, not as a fighter - genuinely
    /// unknown before it was read off the running game.
    /// </summary>
    [Fact]
    public void Resolve_TheNomadAsMeasuredLive_IsSrv_NotFighter_AndNotMainShip()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), "lander01");

        Assert.Equal(VesselContextKind.Srv, context.Kind);
        Assert.Equal("lander01", context.VesselType);
    }

    [Fact]
    public void Resolve_InSrv_CarriesTheJournalsVesselType()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), "testbuggy");

        Assert.Equal(new VesselContext(VesselContextKind.Srv, "testbuggy"), context);
    }

    /// <summary>
    /// The casing trap, at the resolver's own boundary.
    /// <see cref="JournalStateStore"/> lower-cases on ingest, but this method
    /// takes a bare string from any caller, and <c>LoadGame</c>'s spelling is
    /// the one that only bites a commander who logged in already inside their
    /// SRV - the case no launch-path test reaches.
    /// </summary>
    [Fact]
    public void Resolve_LoadGameSpelling_IsLowerCased_NotStoredAsWritten()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), "Lander01");

        Assert.Equal("lander01", context.VesselType);
    }

    /// <summary>
    /// Why the resolver normalises at all, stated as the behaviour it buys
    /// rather than as "it calls ToLowerInvariant": <see cref="VesselContext"/>
    /// equality is what <see cref="Core.Layouts.AutoPageSwitcher"/> reads as a
    /// context CHANGE, and it compares strings ordinally. Two readings of the
    /// same vessel that differ only in casing - which is exactly what
    /// <c>LoadGame</c> then <c>LaunchSRV</c> produce - would otherwise fire a
    /// switch at a commander who had not gone anywhere.
    ///
    /// [2026-09-10: the second spelling named here was <c>DockSRV</c>, which
    /// no longer names a vessel to be in at all - it clears. The trap is
    /// unchanged; the events that spring it are <c>LoadGame</c>'s capital L
    /// against either launch event's lower-case one.]
    /// </summary>
    [Fact]
    public void Resolve_TheSameVesselInBothJournalSpellings_ProducesEqualContexts_NotAChange()
    {
        var fromLoadGame = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), "Lander01");
        var fromDockSrv = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), "lander01");

        Assert.Equal(fromLoadGame, fromDockSrv);
    }

    /// <summary>
    /// <c>ref/docs/gamestate.md</c>: "Don't read CurrentVesselType as 'the
    /// vessel the commander is in'". <c>LoadGame</c>'s <c>Ship</c>
    /// legitimately names a main ship, so a main-ship context must not report
    /// it as the vessel type - otherwise a <c>Vessel:</c> term would match a
    /// commander sitting in their Krait as though they were in an SRV named
    /// <c>krait_mkii</c>.
    /// </summary>
    [Fact]
    public void Resolve_InMainShip_NamesNoVesselType_EvenWhenTheJournalKnowsOne()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InMainShipBit), "krait_mkii");

        Assert.Equal(VesselContextKind.MainShip, context.Kind);
        Assert.Null(context.VesselType);
    }

    [Fact]
    public void Resolve_OnFoot_NamesNoVesselType_EvenWhenTheJournalKnowsOne()
    {
        var context = VesselContextResolver.Resolve(Snapshot(0u, 1u << OnFootBit2), "lander01");

        Assert.Null(context.VesselType);
    }

    [Fact]
    public void Resolve_InSrv_WithNoJournalVesselTypeYet_IsSrvWithNoType()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InSrvBit), null);

        Assert.Equal(new VesselContext(VesselContextKind.Srv, null), context);
    }

    /// <summary>
    /// The four flags are believed mutually exclusive and nothing has ever
    /// measured them together - so the resolver's order is a deliberate
    /// tiebreak, pinned here so it stays a decision rather than whatever the
    /// last edit left behind. SRV wins over every other bit; main ship,
    /// broadest, loses to all three.
    /// </summary>
    [Theory]
    [InlineData((1u << InSrvBit) | (1u << InFighterBit), 0u, VesselContextKind.Srv)]
    [InlineData((1u << InSrvBit) | (1u << InMainShipBit), 0u, VesselContextKind.Srv)]
    [InlineData(1u << InSrvBit, 1u << OnFootBit2, VesselContextKind.Srv)]
    [InlineData((1u << InFighterBit) | (1u << InMainShipBit), 0u, VesselContextKind.Fighter)]
    [InlineData(1u << InFighterBit, 1u << OnFootBit2, VesselContextKind.Fighter)]
    [InlineData(1u << InMainShipBit, 1u << OnFootBit2, VesselContextKind.OnFoot)]
    public void Resolve_MoreThanOneContextFlagSet_ResolvesByTheDocumentedTiebreak(uint flags, uint flags2, VesselContextKind expected)
    {
        Assert.Equal(expected, VesselContextResolver.Resolve(Snapshot(flags, flags2), null).Kind);
    }

    /// <summary>
    /// <c>Flags2</c> absent (pre-Odyssey data, or a closed game) must not
    /// throw or be read as on-foot - <see cref="Condition"/> already treats
    /// absent as zero, and this pins that the resolver inherits it rather
    /// than dereferencing a null.
    /// </summary>
    [Fact]
    public void Resolve_Flags2Absent_IsTreatedAsZero_NotAsOnFoot()
    {
        var context = VesselContextResolver.Resolve(Snapshot(1u << InMainShipBit, null), null);

        Assert.Equal(VesselContextKind.MainShip, context.Kind);
    }
}
