using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Pairing;
using LunaPanel.Server.Http;
using LunaPanel.Server.Layouts;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="LayoutImportEndpoint"/> against a real
/// <see cref="LayoutStore"/>, a real <see cref="OrphanMarkerStore"/> and a
/// real <see cref="DeviceRegistry"/>, all over temp directories under the
/// test output - the layouts copied here are real files, and the
/// <c>.bak</c> generation asserted on is the one <see cref="LayoutStore"/>
/// itself writes, never one this test made.
/// </summary>
public class LayoutImportEndpointTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static readonly HashSet<string> KnownActions = new(StringComparer.Ordinal)
    {
        "LandingGearToggle", "ToggleCargoScoop", "ShipSpotLightToggle",
    };

    private static readonly DateTimeOffset LastSeen = new(2026, 9, 7, 20, 14, 0, TimeSpan.Zero);

    private sealed record Harness(
        LayoutStore Store,
        OrphanMarkerStore Markers,
        CapturingDiagnosticLog Log,
        string Directory);

    private static Harness NewHarness([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "layout-import", testName, Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var log = new CapturingDiagnosticLog();
        return new Harness(new LayoutStore(dir, log), new OrphanMarkerStore(dir, log), log, dir);
    }

    private static Layout LayoutNaming(string action) =>
        new(1, new[] { new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, action, null, null, null) }, Array.Empty<LayoutSlot>()) });

    private static IReadOnlyList<DeviceSummary> Live(params string[] deviceIds) =>
        deviceIds.Select(id => new DeviceSummary(id, "Live", "phone", LastSeen, LastSeen)).ToArray();

    /// <summary>An orphan with a layout on disk and a marker describing it.</summary>
    private static void GiveOrphan(Harness harness, string deviceId, string name, string deviceClass, string action)
    {
        Assert.Equal(LayoutSaveOutcome.Saved, harness.Store.Save(deviceId, LayoutNaming(action), KnownActions).Outcome);
        Assert.True(harness.Markers.Write(new OrphanMarker(deviceId, name, deviceClass, LastSeen)));
    }

    // -----------------------------------------------------------------
    // Candidates / list
    // -----------------------------------------------------------------

    [Fact]
    public void Candidates_ALayoutOwnedByALiveDevice_IsNotOffered()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE", LayoutNaming("LandingGearToggle"), KnownActions);
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");

        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live("LIVE"));

        Assert.Equal("ORPHAN", Assert.Single(candidates).DeviceId);
    }

    [Fact]
    public void BuildListResponse_CarriesTheDescriptionTheImportListRenders()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone 2", "phone", "ToggleCargoScoop");

        var response = LayoutImportEndpoint.BuildListResponse(
            LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live()));

        var candidate = Assert.Single(response.Candidates);
        Assert.Equal("ORPHAN", candidate.DeviceId);
        Assert.Equal("Phone 2", candidate.Name);
        Assert.Equal("phone", candidate.DeviceClass);
        Assert.Equal(LastSeen, candidate.LastSeenAt);
    }

    // -----------------------------------------------------------------
    // Import
    // -----------------------------------------------------------------

    [Fact]
    public void Import_CopiesTheOrphansLayoutOntoTheCallingDevice()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        var outcome = LayoutImportEndpoint.Import(harness.Store, "ME", "ORPHAN", candidates, KnownActions, harness.Log);

        Assert.Equal(LayoutImportEndpoint.ImportOutcome.Imported, outcome);
        var mine = harness.Store.Load("ME");
        Assert.Equal(LayoutLoadOutcome.Loaded, mine.Outcome);
        Assert.Equal("ToggleCargoScoop", mine.Layout!.Pages[0].Slots[0].Action);
    }

    /// <summary>
    /// "Adoption copies; it never moves" (<c>ref/docs/layout-import.md</c>).
    /// The source has to be byte-for-byte where it was, so adopting the
    /// wrong one costs nothing and one layout can seed several devices.
    /// </summary>
    [Fact]
    public void Import_LeavesTheSourceLayoutExactlyWhereItWas()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var sourcePath = Path.Combine(harness.Directory, "layout-ORPHAN.json");
        var before = File.ReadAllBytes(sourcePath);
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        LayoutImportEndpoint.Import(harness.Store, "ME", "ORPHAN", candidates, KnownActions, harness.Log);

        Assert.Equal(before, File.ReadAllBytes(sourcePath));
    }

    [Fact]
    public void Import_OneOrphan_CanSeedTwoDifferentDevices()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        Assert.Equal(
            LayoutImportEndpoint.ImportOutcome.Imported,
            LayoutImportEndpoint.Import(harness.Store, "ONE", "ORPHAN", candidates, KnownActions, harness.Log));
        Assert.Equal(
            LayoutImportEndpoint.ImportOutcome.Imported,
            LayoutImportEndpoint.Import(harness.Store, "TWO", "ORPHAN", candidates, KnownActions, harness.Log));

        Assert.Equal("ToggleCargoScoop", harness.Store.Load("ONE").Layout!.Pages[0].Slots[0].Action);
        Assert.Equal("ToggleCargoScoop", harness.Store.Load("TWO").Layout!.Pages[0].Slots[0].Action);
    }

    /// <summary>
    /// The recoverability the spec asks for, driven rather than asserted
    /// about: after importing over an existing arrangement, the previous one
    /// is in <see cref="LayoutStore"/>'s own <c>.bak</c>, and
    /// <see cref="LayoutImportEndpoint.Undo"/> puts it back.
    /// </summary>
    [Fact]
    public void Import_OverAnExistingLayout_LeavesTheOldOneInBak_AndUndoRestoresIt()
    {
        var harness = NewHarness();
        harness.Store.Save("ME", LayoutNaming("LandingGearToggle"), KnownActions);
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        LayoutImportEndpoint.Import(harness.Store, "ME", "ORPHAN", candidates, KnownActions, harness.Log);

        var bakPath = Path.Combine(harness.Directory, "layout-ME.json.bak");
        Assert.True(File.Exists(bakPath));
        Assert.Contains("LandingGearToggle", File.ReadAllText(bakPath), StringComparison.Ordinal);

        Assert.True(LayoutImportEndpoint.Undo(harness.Store, "ME"));
        Assert.Equal("LandingGearToggle", harness.Store.Load("ME").Layout!.Pages[0].Slots[0].Action);
    }

    /// <summary>
    /// A device that has never rendered a panel has no layout file at all,
    /// so nothing would be displaced into <c>.bak</c> and the undo the
    /// pairing screen offers would have nothing to return to. The import
    /// seeds the starter first for exactly this reason - so the undo means
    /// the same thing seconds after pairing as it does months later.
    /// </summary>
    [Fact]
    public void Import_OntoADeviceWithNoLayoutYet_StillLeavesTheStarterInBak_SoUndoWorks()
    {
        var harness = NewHarness();

        // Discriminated by a user label, not by an action name: the starter
        // layout already uses most of this project's action names, so an
        // action-based marker would not tell the imported layout apart from
        // the starter it is meant to have displaced. (Found by this test's
        // own guard failing on first run, 2026-09-08.)
        var orphanLayout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, "ToggleCargoScoop", null, "ORPHANMARK", null) }, Array.Empty<LayoutSlot>()),
        });
        Assert.Equal(LayoutSaveOutcome.Saved, harness.Store.Save("ORPHAN", orphanLayout, KnownActions).Outcome);
        Assert.True(harness.Markers.Write(new OrphanMarker("ORPHAN", "Phone", "phone", LastSeen)));
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        Assert.Equal(
            LayoutImportEndpoint.ImportOutcome.Imported,
            LayoutImportEndpoint.Import(harness.Store, "FRESH", "ORPHAN", candidates, StarterKnownActions(), harness.Log));

        Assert.Contains("ORPHANMARK", File.ReadAllText(Path.Combine(harness.Directory, "layout-FRESH.json")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(harness.Directory, "layout-FRESH.json.bak")));

        Assert.True(LayoutImportEndpoint.Undo(harness.Store, "FRESH"));

        var restored = harness.Store.Load("FRESH");
        Assert.Equal(LayoutLoadOutcome.Loaded, restored.Outcome);
        Assert.DoesNotContain(
            "ORPHANMARK",
            restored.Layout!.Pages.SelectMany(page => page.Slots).Select(slot => slot.Label ?? string.Empty));

        // And what came back is genuinely the starter, not merely "not the
        // import" - the undo the pairing screen offers says "return to the
        // starter layout", so that is what it has to do.
        Assert.Equal(
            StarterLayout.Load().Pages.Select(page => page.Name),
            restored.Layout!.Pages.Select(page => page.Name));
    }

    /// <summary>Every action name the starter layout uses, so it can be saved.</summary>
    private static IReadOnlySet<string> StarterKnownActions()
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "LandingGearToggle", "ToggleCargoScoop" };
        foreach (var page in StarterLayout.Load().Pages)
        {
            foreach (var slot in page.Slots.Concat(page.Parked))
            {
                if (slot.Action is not null)
                {
                    names.Add(slot.Action);
                }

                if (slot.LongPress?.Action is not null)
                {
                    names.Add(slot.LongPress.Action);
                }
            }
        }

        return names;
    }

    [Fact]
    public void Import_NoSourceDeviceId_IsRefused()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        Assert.Equal(
            LayoutImportEndpoint.ImportOutcome.MissingSource,
            LayoutImportEndpoint.Import(harness.Store, "ME", null, candidates, KnownActions, harness.Log));
    }

    /// <summary>
    /// A currently-paired device's layout is not on the orphan list, and
    /// naming its id directly must not be a way round that. This is the one
    /// access-control property this route has: it can copy an abandoned
    /// arrangement, never a live device's current one.
    /// </summary>
    [Fact]
    public void Import_ALiveDevicesLayout_IsRefused_AndNothingIsWritten()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE", LayoutNaming("LandingGearToggle"), KnownActions);
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live("LIVE"));

        var outcome = LayoutImportEndpoint.Import(harness.Store, "ME", "LIVE", candidates, KnownActions, harness.Log);

        Assert.Equal(LayoutImportEndpoint.ImportOutcome.NotAnOrphan, outcome);
        Assert.Equal(LayoutLoadOutcome.NotFound, harness.Store.Load("ME").Outcome);
    }

    [Fact]
    public void Import_AnIdWithNoLayoutAtAll_IsRefused()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live());

        Assert.Equal(
            LayoutImportEndpoint.ImportOutcome.NotAnOrphan,
            LayoutImportEndpoint.Import(harness.Store, "ME", "NEVEREXISTED", candidates, KnownActions, harness.Log));
    }

    [Fact]
    public void Undo_WithNothingToGoBackTo_ReturnsFalse()
    {
        var harness = NewHarness();

        Assert.False(LayoutImportEndpoint.Undo(harness.Store, "ME"));
    }

    // -----------------------------------------------------------------
    // Discard - the delete half of the import/recovery chooser
    // -----------------------------------------------------------------

    [Fact]
    public void Discard_NoDeviceId_IsRefused()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");

        Assert.Equal(
            LayoutImportEndpoint.DiscardOutcome.MissingSource,
            LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live(), null));
    }

    /// <summary>
    /// The one access-control property this route has, mirroring
    /// <see cref="Import_ALiveDevicesLayout_IsRefused_AndNothingIsWritten"/>:
    /// a live-paired device's own layout can never be discarded through this
    /// route, and nothing on disk changes when it is refused.
    /// </summary>
    [Fact]
    public void Discard_ALiveDevicesLayout_IsRefused_AndTheFileSurvives()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE", LayoutNaming("LandingGearToggle"), KnownActions);
        var candidates = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live("LIVE"));

        var outcome = LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live("LIVE"), "LIVE");

        Assert.Equal(LayoutImportEndpoint.DiscardOutcome.NotAnOrphan, outcome);
        Assert.Equal(LayoutLoadOutcome.Loaded, harness.Store.Load("LIVE").Outcome);
    }

    [Fact]
    public void Discard_AnIdWithNoLayoutAtAll_IsRefused()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");

        Assert.Equal(
            LayoutImportEndpoint.DiscardOutcome.NotAnOrphan,
            LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live(), "NEVEREXISTED"));
    }

    /// <summary>
    /// The real path: an orphan's layout and marker are both gone afterward,
    /// and it no longer appears in the next call to <see cref="LayoutImportEndpoint.Candidates"/> -
    /// proven by mutation in <see cref="Discard_OnceDiscarded_TheSameIdCanNeverBeDiscardedAgain"/>.
    /// </summary>
    [Fact]
    public void Discard_ARealOrphan_RemovesItsLayoutAndMarker_AndItLeavesCandidates()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        Assert.True(File.Exists(Path.Combine(harness.Directory, "layout-ORPHAN.json")));
        Assert.True(File.Exists(Path.Combine(harness.Directory, "orphan-ORPHAN.json")));

        var outcome = LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live(), "ORPHAN");

        Assert.Equal(LayoutImportEndpoint.DiscardOutcome.Discarded, outcome);
        Assert.False(File.Exists(Path.Combine(harness.Directory, "layout-ORPHAN.json")));
        Assert.False(File.Exists(Path.Combine(harness.Directory, "orphan-ORPHAN.json")));
        Assert.Empty(LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live()));
    }

    /// <summary>
    /// A discarded id is genuinely gone, not merely hidden: discarding it a
    /// second time is refused exactly like any other id with no layout at
    /// all, because that is what it now is.
    /// </summary>
    [Fact]
    public void Discard_OnceDiscarded_TheSameIdCanNeverBeDiscardedAgain()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "ORPHAN", "Phone", "phone", "ToggleCargoScoop");
        Assert.Equal(
            LayoutImportEndpoint.DiscardOutcome.Discarded,
            LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live(), "ORPHAN"));

        var outcome = LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live(), "ORPHAN");

        Assert.Equal(LayoutImportEndpoint.DiscardOutcome.NotAnOrphan, outcome);
    }

    /// <summary>
    /// Discarding one orphan never touches another's layout or marker.
    /// </summary>
    [Fact]
    public void Discard_OneOrphan_NeverTouchesAnothers()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "A", "Tablet", "tablet", "ToggleCargoScoop");
        GiveOrphan(harness, "B", "Tablet 2", "tablet", "ShipSpotLightToggle");

        LayoutImportEndpoint.Discard(harness.Store, harness.Markers, Live(), "A");

        Assert.Equal(LayoutLoadOutcome.Loaded, harness.Store.Load("B").Outcome);
        Assert.Equal("B", Assert.Single(LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live())).DeviceId);
    }

    // -----------------------------------------------------------------
    // Recover - the three arms at the end of a successful pair
    // -----------------------------------------------------------------

    /// <summary>
    /// Arm 3, and the one that has to be exactly nothing: a first-ever
    /// pairing must not be interrupted by an empty chooser, so this is
    /// <see langword="null"/> rather than a recovery object with an empty
    /// list in it.
    /// </summary>
    [Fact]
    public void Recover_NoOrphansAtAll_SaysNothingAtAll()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE", LayoutNaming("LandingGearToggle"), KnownActions);

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("LIVE"), "LIVE", "Phone", KnownActions, harness.Log);

        Assert.Null(recovery);
    }

    /// <summary>Arm 1: exactly one orphan answering to this device's name.</summary>
    [Fact]
    public void Recover_OneOrphanMatchingTheName_AdoptsItAndSaysSo()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "OLDPHONE", "Phone", "phone", "ToggleCargoScoop");

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("NEWPHONE"), "NEWPHONE", "Phone", KnownActions, harness.Log);

        Assert.NotNull(recovery);
        Assert.NotNull(recovery!.Adopted);
        Assert.Equal("OLDPHONE", recovery.Adopted!.DeviceId);
        Assert.Empty(recovery.Candidates);
        Assert.Equal("ToggleCargoScoop", harness.Store.Load("NEWPHONE").Layout!.Pages[0].Slots[0].Action);
    }

    /// <summary>
    /// Arm 2, the "several orphans" case: the list is presented and
    /// <b>nothing is written</b> - a device that quietly inherited an
    /// arrangement nobody chose is indistinguishable from a bug.
    /// </summary>
    [Fact]
    public void Recover_SeveralOrphansNoneMatching_PresentsTheListAndAdoptsNothing()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "A", "Tablet", "tablet", "ToggleCargoScoop");
        GiveOrphan(harness, "B", "Tablet 2", "tablet", "ShipSpotLightToggle");

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("ME"), "ME", "Phone", KnownActions, harness.Log);

        Assert.NotNull(recovery);
        Assert.Null(recovery!.Adopted);
        Assert.Equal(2, recovery.Candidates.Count);
        Assert.Equal(LayoutLoadOutcome.NotFound, harness.Store.Load("ME").Outcome);
    }

    /// <summary>
    /// Arm 2's ambiguous case. Two orphans called "Phone" is exactly where
    /// guessing is worse than asking - both are offered, neither is taken.
    /// </summary>
    [Fact]
    public void Recover_TwoOrphansSharingTheMatchingName_AdoptsNeither()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "A", "Phone", "phone", "ToggleCargoScoop");
        GiveOrphan(harness, "B", "Phone", "phone", "ShipSpotLightToggle");

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("ME"), "ME", "Phone", KnownActions, harness.Log);

        Assert.NotNull(recovery);
        Assert.Null(recovery!.Adopted);
        Assert.Equal(2, recovery.Candidates.Count);
        Assert.Equal(LayoutLoadOutcome.NotFound, harness.Store.Load("ME").Outcome);
    }

    /// <summary>
    /// One orphan, no name match: still arm 2. The list appears (it is the
    /// commander's own layout and they may well want it), but nothing is
    /// adopted for them.
    /// </summary>
    [Fact]
    public void Recover_OneOrphanWhoseNameDoesNotMatch_PresentsItWithoutAdopting()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "OLDTABLET", "Tablet", "tablet", "ToggleCargoScoop");

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("ME"), "ME", "Phone", KnownActions, harness.Log);

        Assert.NotNull(recovery);
        Assert.Null(recovery!.Adopted);
        Assert.Equal("OLDTABLET", Assert.Single(recovery.Candidates).DeviceId);
        Assert.Equal(LayoutLoadOutcome.NotFound, harness.Store.Load("ME").Outcome);
    }

    /// <summary>
    /// Several orphans, exactly one of which matches - adopted, per the
    /// coordinator's reading (2026-09-08). Also proves the automatic arm
    /// does not require the orphan to be the only one on disk.
    /// </summary>
    [Fact]
    public void Recover_SeveralOrphansButExactlyOneMatchingName_AdoptsThatOne()
    {
        var harness = NewHarness();
        GiveOrphan(harness, "A", "Tablet", "tablet", "ShipSpotLightToggle");
        GiveOrphan(harness, "B", "Phone", "phone", "ToggleCargoScoop");
        GiveOrphan(harness, "C", "Phone 2", "phone", "LandingGearToggle");

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("ME"), "ME", "Phone", KnownActions, harness.Log);

        Assert.Equal("B", recovery!.Adopted!.DeviceId);
        Assert.Equal("ToggleCargoScoop", harness.Store.Load("ME").Layout!.Pages[0].Slots[0].Action);
    }

    /// <summary>
    /// An orphan left by a device forgotten before markers existed carries a
    /// synthesised name and must never be adopted automatically - the whole
    /// justification for adopting without asking is that a person chose that
    /// name. It is still offered.
    /// </summary>
    [Fact]
    public void Recover_AnUndescribedOrphan_IsOfferedButNeverAdoptedAutomatically()
    {
        var harness = NewHarness();
        harness.Store.Save("NOMARKER", LayoutNaming("ToggleCargoScoop"), KnownActions);
        var name = LayoutImportEndpoint.Candidates(harness.Store, harness.Markers, Live("ME"))[0].Name;

        var recovery = LayoutImportEndpoint.Recover(
            harness.Store, harness.Markers, Live("ME"), "ME", name, KnownActions, harness.Log);

        Assert.NotNull(recovery);
        Assert.Null(recovery!.Adopted);
        Assert.Equal("NOMARKER", Assert.Single(recovery.Candidates).DeviceId);
    }
}
