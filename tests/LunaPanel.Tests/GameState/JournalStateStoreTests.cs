using LunaPanel.Core.GameState;

namespace LunaPanel.Tests.GameState;

/// <summary>
/// Pins the journal-side store: vessel-type tracking (including the casing
/// trap that only bites a commander who logged in already inside their SRV),
/// vocabulary filtering, and the deliberately timeout-free wait.
///
/// No <see cref="TimeProvider"/> appears anywhere in this file, and that is
/// the point rather than an omission - see
/// <see cref="JournalStateStore.WaitForEdgeAsync"/>. Nothing here waits on
/// real time either.
/// </summary>
public class JournalStateStoreTests
{
    private static JournalEvent Event(string name, params (string Key, string Value)[] strings) =>
        new(name, null, strings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));

    private static JournalEvent LoadGameInTheNomad() =>
        Event("LoadGame", ("Ship", "Lander01"), ("Ship_Localised", "Nomad"));

    // -----------------------------------------------------------------
    // Vessel type
    // -----------------------------------------------------------------

    [Fact]
    public void CurrentVesselType_IsNull_BeforeAnythingIsRecorded()
    {
        Assert.Null(new JournalStateStore().CurrentVesselType);
    }

    [Fact]
    public void LoadGame_AnchorsTheVesselType_AndNormalisesItsCapitalL()
    {
        // THE casing pin. LoadGame writes "Lander01"; LaunchVessel and
        // DockSRV write "lander01". An ordinal comparison passes every test
        // written against the launch path - which is the path anyone would
        // naturally write first - and fails only here, for a commander who
        // logged in already inside their SRV. This test uses the LoadGame
        // spelling specifically, for exactly that reason.
        var store = new JournalStateStore();

        store.Record(LoadGameInTheNomad());

        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void LoadGame_KeepsTheLocalisedNameVerbatim()
    {
        var store = new JournalStateStore();

        store.Record(LoadGameInTheNomad());

        Assert.Equal("Nomad", store.CurrentVesselTypeLocalised);
    }

    [Fact]
    public void LaunchVessel_SetsTheVesselTypeFromItsOwnFieldName()
    {
        var store = new JournalStateStore();

        store.Record(Event("LaunchVessel", ("VesselType", "lander01"), ("VesselType_Localised", "Nomad")));

        Assert.Equal("lander01", store.CurrentVesselType);
        Assert.Equal("Nomad", store.CurrentVesselTypeLocalised);
    }

    /// <summary>
    /// <b>The launch spelling depends on the vessel.</b> Counted 2026-09-10
    /// over the commander's own journal history: <c>LaunchVessel</c> is
    /// written for <c>lander01</c> and nothing else (33), <c>LaunchSRV</c>
    /// only for <c>testbuggy</c> (15) and <c>combat_multicrew_srv_01</c> (8).
    /// Knowing only the first spelling is what sent the first Scarab boarding
    /// of 2026-09-10 to the Nomad's page. Note the field name is
    /// <c>SRVType</c>, as <c>DockSRV</c> uses - not <c>VesselType</c>.
    /// </summary>
    [Fact]
    public void LaunchSrv_SetsTheVesselTypeFromItsOwnFieldName()
    {
        var store = new JournalStateStore();

        store.Record(Event("LaunchSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")));

        Assert.Equal("testbuggy", store.CurrentVesselType);
        Assert.Equal("SRV Scarab", store.CurrentVesselTypeLocalised);
    }

    /// <summary>
    /// <b>[2026-09-10, superseded.]</b> This was
    /// <c>DockSrv_SetsTheVesselTypeFromItsOwnFieldName</c>, and it asserted
    /// <c>Assert.Equal("testbuggy", store.CurrentVesselType)</c> and
    /// <c>Assert.Equal("SRV Scarab", store.CurrentVesselTypeLocalised)</c>
    /// after a lone <c>DockSRV</c>.
    ///
    /// <para><c>DockSRV</c> names the vessel the commander has just got
    /// <em>out</em> of. Recording it left a vessel type that outlived the
    /// vessel, so the next boarding was decided by whatever they drove last
    /// time - which is precisely how the Nomad's page won a Scarab boarding
    /// on 2026-09-10, and which would still have been reachable through the
    /// gap between <c>Status.json</c> flipping <c>InSrv</c> and the launch
    /// line being read even after <c>LaunchSRV</c> was understood. Clearing
    /// is the honest state, and it is what makes "no page matches" - stay
    /// put - the outcome instead of a switch to the wrong page followed by a
    /// correction.</para>
    /// </summary>
    [Fact]
    public void DockSrv_ClearsTheVesselType_BecauseItNamesTheOneJustLeft()
    {
        var store = new JournalStateStore();
        store.Record(Event("LaunchSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")));

        store.Record(Event("DockSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")));

        Assert.Null(store.CurrentVesselType);
        Assert.Null(store.CurrentVesselTypeLocalised);
    }

    /// <summary>
    /// <b>[2026-09-10, superseded.]</b> This was
    /// <c>TheThreeVesselEvents_EachSupersedeTheLast</c>, and its third step
    /// recorded <c>DockSRV combat_multicrew_srv_01</c> and asserted
    /// <c>Assert.Equal("combat_multicrew_srv_01", store.CurrentVesselType)</c>.
    /// There are four vessel events now, and the fourth is an exit rather
    /// than a supersession - see
    /// <see cref="DockSrv_ClearsTheVesselType_BecauseItNamesTheOneJustLeft"/>.
    /// </summary>
    [Fact]
    public void TheThreeEntryEvents_EachSupersedeTheLast()
    {
        var store = new JournalStateStore();

        store.Record(LoadGameInTheNomad());
        Assert.Equal("lander01", store.CurrentVesselType);

        store.Record(Event("LaunchVessel", ("VesselType", "testbuggy"), ("VesselType_Localised", "SRV Scarab")));
        Assert.Equal("testbuggy", store.CurrentVesselType);

        store.Record(Event("LaunchSRV", ("SRVType", "combat_multicrew_srv_01"), ("SRVType_Localised", "SRV Scorpion")));
        Assert.Equal("combat_multicrew_srv_01", store.CurrentVesselType);
    }

    /// <summary>
    /// The commander's real sequence of 2026-09-10, at the store's own level:
    /// the Nomad launched and docked, then the Scarab launched. The Scarab's
    /// launch must win outright - not merely "eventually", and not by the
    /// Nomad's name still standing.
    /// </summary>
    [Fact]
    public void LaunchingTheScarabAfterTheNomad_NamesTheScarab_NotTheNomad()
    {
        var store = new JournalStateStore();
        store.Record(Event("LaunchVessel", ("VesselType", "lander01"), ("VesselType_Localised", "Nomad")));
        store.Record(Event("DockSRV", ("SRVType", "lander01"), ("SRVType_Localised", "Nomad")));

        store.Record(Event("LaunchSRV", ("SRVType", "testbuggy"), ("SRVType_Localised", "SRV Scarab")));

        Assert.Equal("testbuggy", store.CurrentVesselType);
    }

    [Fact]
    public void TheNomadAndTheScarab_AreDistinguishable_NotJustBothAnSrv()
    {
        // The commander asked for these to be separate contexts because they
        // play differently. Status.json says only InSrv; this is the whole
        // reason the journal is involved at all.
        var nomad = new JournalStateStore();
        nomad.Record(Event("LaunchVessel", ("VesselType", "lander01"), ("VesselType_Localised", "Nomad")));

        var scarab = new JournalStateStore();
        scarab.Record(Event("LaunchVessel", ("VesselType", "testbuggy"), ("VesselType_Localised", "SRV Scarab")));

        Assert.NotEqual(nomad.CurrentVesselType, scarab.CurrentVesselType);
    }

    [Fact]
    public void AnEventCarryingAShipFieldButNotAVesselEvent_DoesNotTouchTheVesselType()
    {
        // Loadout also carries a "Ship" property, and it names the main ship,
        // not the vessel the commander is in. Routing by field name instead
        // of by event name would silently overwrite the SRV type with the
        // ship's type at every session start.
        var store = new JournalStateStore();
        store.Record(LoadGameInTheNomad());

        store.Record(Event("Loadout", ("Ship", "krait_mkii")));

        Assert.Equal("lander01", store.CurrentVesselType);
    }

    /// <summary>
    /// [2026-09-10: this used to drive a bare <c>DockSRV</c> with no
    /// <c>SRVType</c>. <c>DockSRV</c> now clears whatever it carries, so it
    /// could no longer make this claim - the needle had moved out of reach
    /// of the assertion. Driven through a bare <c>LaunchSRV</c> instead,
    /// which is an entry event and so is the one this rule is actually
    /// about.]
    /// </summary>
    [Fact]
    public void AnEntryEventMissingItsTypeField_LeavesTheCurrentVesselAlone()
    {
        var store = new JournalStateStore();
        store.Record(LoadGameInTheNomad());

        store.Record(Event("LaunchSRV"));

        Assert.Equal("lander01", store.CurrentVesselType);
    }

    // -----------------------------------------------------------------
    // Vocabulary filtering and the sequence
    // -----------------------------------------------------------------

    [Fact]
    public void AnEventOutsideTheVocabulary_IsNotRecorded()
    {
        var store = new JournalStateStore();
        var before = store.Mark();

        store.Record(Event("ReservoirReplenished"));

        Assert.Equal(before, store.Mark());
    }

    [Fact]
    public void AKnownEvent_AdvancesTheSequence()
    {
        var store = new JournalStateStore();
        var before = store.Mark();

        store.Record(Event("FSDJump"));

        Assert.NotEqual(before, store.Mark());
    }

    [Fact]
    public void Changed_FiresForAKnownEvent_AndNotForAnUnknownOne()
    {
        var store = new JournalStateStore();
        var seen = new List<string>();
        store.Changed += e => seen.Add(e.EventName);

        store.Record(Event("FSDJump"));
        store.Record(Event("ReservoirReplenished"));

        Assert.Equal(new[] { "FSDJump" }, seen);
    }

    // -----------------------------------------------------------------
    // isBackScan - Fix 4 (2026-09-07): the explicit live/back-scan boundary
    // Changed now depends on, rather than the accident of subscription order
    // JournalTailer used to rely on.
    // -----------------------------------------------------------------

    [Fact]
    public void Changed_DoesNotFire_ForAnEventRecordedAsBackScan()
    {
        var store = new JournalStateStore();
        var seen = new List<string>();
        store.Changed += e => seen.Add(e.EventName);

        store.Record(Event("LoadGame"), isBackScan: true);

        Assert.Empty(seen);
    }

    [Fact]
    public void Changed_StillFires_ForALiveEvent_AfterABackScannedOne()
    {
        var store = new JournalStateStore();
        var seen = new List<string>();
        store.Changed += e => seen.Add(e.EventName);
        store.Record(Event("LoadGame"), isBackScan: true);

        store.Record(Event("FSDJump"));

        Assert.Equal(new[] { "FSDJump" }, seen);
    }

    [Fact]
    public void Record_WithIsBackScanTrue_StillUpdatesEverythingElse()
    {
        // "Changed doesn't fire" must not mean "this event never happened" -
        // a watermark-relative reader (HasSeenSince, LastEvent, the vessel
        // type) still needs to see it, exactly as it did before isBackScan
        // existed. Only the level-triggered notification is suppressed.
        var store = new JournalStateStore();
        var before = store.Mark();

        store.Record(LoadGameInTheNomad(), isBackScan: true);

        Assert.True(store.HasSeenSince("LoadGame", before));
        Assert.Equal("LoadGame", store.LastEvent("LoadGame")?.EventName);
        Assert.Equal("lander01", store.CurrentVesselType);
    }

    [Fact]
    public void HasSeenSince_IgnoresAnEventRecordedBeforeTheMark()
    {
        var store = new JournalStateStore();
        store.Record(Event("FSDJump"));
        var mark = store.Mark();

        Assert.False(store.HasSeenSince("FSDJump", mark));
    }

    // -----------------------------------------------------------------
    // LastEvent - built so a waitForEdge failure branch can read a payload
    // field (DockingDenied's Reason) off the event that satisfied it
    // -----------------------------------------------------------------

    [Fact]
    public void LastEvent_IsNull_WhenTheEventHasNeverBeenSeen()
    {
        Assert.Null(new JournalStateStore().LastEvent("DockingDenied"));
    }

    [Fact]
    public void LastEvent_ReturnsTheWholeEvent_IncludingItsStringFields()
    {
        var store = new JournalStateStore();

        store.Record(Event("DockingDenied", ("Reason", "Distance")));

        var last = store.LastEvent("DockingDenied");
        Assert.NotNull(last);
        Assert.Equal("Distance", last!.String("Reason"));
    }

    [Fact]
    public void LastEvent_IsSupersededByALaterRecordingOfTheSameName()
    {
        var store = new JournalStateStore();

        store.Record(Event("DockingDenied", ("Reason", "Distance")));
        store.Record(Event("DockingDenied", ("Reason", "NoSpace")));

        Assert.Equal("NoSpace", store.LastEvent("DockingDenied")!.String("Reason"));
    }

    [Fact]
    public void LastEvent_ForAnEventOutsideTheVocabulary_IsNull()
    {
        var store = new JournalStateStore();

        store.Record(Event("ReservoirReplenished", ("Foo", "Bar")));

        Assert.Null(store.LastEvent("ReservoirReplenished"));
    }

    [Fact]
    public void LastEvent_IsCaseInsensitiveByName_LikeHasSeenSince()
    {
        var store = new JournalStateStore();

        store.Record(Event("DockingDenied", ("Reason", "Distance")));

        Assert.NotNull(store.LastEvent("dockingdenied"));
    }

    // -----------------------------------------------------------------
    // WaitForEdgeAsync - no timeout, on purpose
    // -----------------------------------------------------------------

    [Fact]
    public async Task WaitForEdgeAsync_CompletesWhenTheEventArrives()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();

        var waiting = store.WaitForEdgeAsync(EdgeCondition.Parse("Journal:DockSRV"), mark);
        Assert.False(waiting.IsCompleted);

        store.Record(Event("DockSRV"));

        // WaitAsync is a failure bound, not a delay: it completes the instant
        // `waiting` does, and only turns a hang into a reported failure. The
        // success path never waits for it.
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void WaitForEdgeAsync_ReturnsAnAlreadyCompletedTask_WhenTheEventAlreadyLanded()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();
        store.Record(Event("DockSRV"));

        var waiting = store.WaitForEdgeAsync(EdgeCondition.Parse("Journal:DockSRV"), mark);

        Assert.True(waiting.IsCompleted);
    }

    [Fact]
    public async Task WaitForEdgeAsync_IgnoresUnrelatedEvents_AndKeepsWaiting()
    {
        var store = new JournalStateStore();
        var mark = store.Mark();

        var waiting = store.WaitForEdgeAsync(EdgeCondition.Parse("Journal:DockSRV"), mark);

        store.Record(Event("SupercruiseEntry"));
        store.Record(Event("SupercruiseExit"));
        store.Record(Event("FSDJump"));

        // Yield so any wrongly-completing continuation gets a chance to run
        // before we assert it did not. No real delay, no clock.
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        store.Record(Event("DockSRV"));
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task WaitForEdgeAsync_IsCancellable()
    {
        // There is deliberately no timeout overload: LC17 measured DockSRV at
        // 54s and 61s with an unbounded spread, so a timeout would be a guess
        // dressed as a bound. Cancellation is how a caller gives up.
        var store = new JournalStateStore();
        using var cts = new CancellationTokenSource();

        var waiting = store.WaitForEdgeAsync(EdgeCondition.Parse("Journal:DockSRV"), store.Mark(), cts.Token);
        cts.Cancel();

        // WaitAsync here is a test-side failure bound only, same convention
        // as this file's own WaitForEdgeAsync_CompletesWhenTheEventArrives -
        // it does not add a timeout to the production API (see the remark
        // above for why that would be wrong). If a mutation stopped
        // cancellation from propagating, `waiting` would otherwise hang
        // forever with nothing else in this test able to unblock it (O27).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task WaitForEdgeAsync_AlreadyCancelledToken_ThrowsWithoutWaiting()
    {
        var store = new JournalStateStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.WaitForEdgeAsync(EdgeCondition.Parse("Journal:DockSRV"), store.Mark(), cts.Token));
    }
}
