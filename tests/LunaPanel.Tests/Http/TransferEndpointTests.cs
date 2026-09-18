using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Core.Macros;
using LunaPanel.Core.Pairing;
using LunaPanel.Core.Transfer;
using LunaPanel.Server.Http;
using LunaPanel.Server.Layouts;
using LunaPanel.Server.Macros;

namespace LunaPanel.Tests.Http;

/// <summary>
/// Drives <see cref="TransferEndpoint"/> against a real
/// <see cref="LayoutStore"/>, a real <see cref="OrphanMarkerStore"/> and a
/// real <see cref="UserMacroStore"/> over temp directories under the test
/// output - the files written here are real files, and the <c>.bak</c>
/// generation asserted on is the one <see cref="LayoutStore"/> itself
/// writes, never one this test made.
///
/// The claim this file exists for is the one in
/// <c>ref/docs/transfer.md</c>'s "An import cannot break a device": a file
/// that is going to be refused is refused before anything is written, and
/// what was on the device is still exactly what it was.
/// </summary>
public class TransferEndpointTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    /// <summary>
    /// The starter layout's own action names, plus the handful this file
    /// places by hand.
    ///
    /// <b>Derived rather than typed, and that is load-bearing.</b>
    /// <c>LayoutAccess.LoadOrSeed</c> seeds the starter through
    /// <see cref="LayoutStore.Save"/>, which validates it against this very
    /// set - so a set that did not cover the starter would have that seed
    /// silently REFUSED, and the <c>.bak</c> these tests are about would
    /// never be written. Production hands the same call every action name in
    /// the commander's real bindings file, which does cover it.
    /// </summary>
    private static readonly HashSet<string> KnownActions = BuildKnownActions();

    private static HashSet<string> BuildKnownActions()
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "LandingGearToggle", "ToggleCargoScoop", "ShipSpotLightToggle", "UI_Down",
        };

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

    private static readonly DateTimeOffset Now = new(2026, 9, 10, 21, 30, 0, TimeSpan.Zero);

    private sealed record Harness(
        LayoutStore Store,
        OrphanMarkerStore Markers,
        MacroCatalogue Catalogue,
        UserMacroStore UserMacros,
        CapturingDiagnosticLog Log,
        string Directory)
    {
        public IReadOnlyList<TransferEndpoint.Target> Targets(params string[] liveDeviceIds) =>
            TransferEndpoint.Targets(Store, Markers, Live(liveDeviceIds));
    }

    private static Harness NewHarness([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "transfer", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var log = new CapturingDiagnosticLog();
        var userMacros = new UserMacroStore(Path.Combine(dir, "macros"), log);
        return new Harness(
            new LayoutStore(dir, log),
            new OrphanMarkerStore(dir, log),
            new MacroCatalogue(MacroLoader.LoadShipped(), userMacros),
            userMacros,
            log,
            dir);
    }

    private static IReadOnlyList<DeviceSummary> Live(params string[] deviceIds) =>
        deviceIds.Select(id => new DeviceSummary(id, "Tablet " + id, "tablet", Now, Now)).ToArray();

    private static MacroDefinition Macro(string id, string name = "Dock") =>
        MacroDefinition.Parse($$"""{ "id": "{{id}}", "name": "{{name}}", "steps": [ { "press": "UI_Down", "repeat": 2 } ] }""");

    private static Layout LayoutWith(params LayoutSlot[] slots) =>
        new(LayoutMigrator.CurrentSchemaVersion,
            new[] { new LayoutPage("SHIP", "t6", slots, Array.Empty<LayoutSlot>(), new[] { "Vessel:Srv" }) });

    private static Layout PlainLayout(string action = "LandingGearToggle") =>
        LayoutWith(new LayoutSlot(0, action, null, "Gear", null, Latch: true));

    /// <summary>
    /// Field-by-field, never <c>Assert.Equal</c> over the whole
    /// <see cref="Layout"/> record: its <c>Pages</c> is an
    /// <c>IReadOnlyList</c>, so record equality compares the list by
    /// reference and two structurally identical layouts are never equal.
    ///
    /// Slots ARE compared as records, which is the point - a field added to
    /// <see cref="LayoutSlot"/> later is covered the day it exists, and a
    /// writer that dropped a latch reddens here rather than passing because
    /// both sides went through the same serializer.
    /// </summary>
    private static void AssertSameLayout(Layout expected, Layout actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Pages.Count, actual.Pages.Count);

        for (var i = 0; i < expected.Pages.Count; i++)
        {
            var e = expected.Pages[i];
            var a = actual.Pages[i];

            Assert.Equal(e.Name, a.Name);
            Assert.Equal(e.TemplateId, a.TemplateId);
            Assert.Equal(e.ShowWhen, a.ShowWhen);
            Assert.Equal(e.Slots, a.Slots);
            Assert.Equal(e.Parked, a.Parked);
        }
    }

    // -----------------------------------------------------------------
    // Targets - the picker's list, and the guard both write paths use
    // -----------------------------------------------------------------

    /// <summary>
    /// The PC is always on the list, named for a person rather than for the
    /// internal id. Nothing else would name it: it is in no registry and has
    /// no orphan marker.
    /// </summary>
    [Fact]
    public void Targets_AlwaysIncludesThisPc_EvenWithNoLayoutFileYet()
    {
        var target = Assert.Single(NewHarness().Targets());

        Assert.Equal(HostRequest.HostDeviceId, target.DeviceId);
        Assert.Equal(TransferEndpoint.ThisPcName, target.Name);
        Assert.True(target.IsThisPc);
        Assert.False(target.HasLayout);
    }

    [Fact]
    public void Targets_ALiveDevice_IsNamedFromTheRegistry()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE1", PlainLayout(), KnownActions);

        var target = Assert.Single(harness.Targets("LIVE1"), t => t.DeviceId == "LIVE1");

        Assert.Equal("Tablet LIVE1", target.Name);
        Assert.False(target.IsThisPc);
        Assert.True(target.HasLayout);
    }

    /// <summary>
    /// A layout left behind by a device that has gone away is exportable too
    /// - which is the whole of what <c>ref/docs/layout-import.md</c>'s
    /// orphan markers were built to make readable.
    /// </summary>
    [Fact]
    public void Targets_AnOrphan_IsNamedFromItsMarker()
    {
        var harness = NewHarness();
        harness.Store.Save("GONE", PlainLayout(), KnownActions);
        harness.Markers.Write(new OrphanMarker("GONE", "Luna's phone", "phone", Now));

        Assert.Equal("Luna's phone", Assert.Single(harness.Targets(), t => t.DeviceId == "GONE").Name);
    }

    [Fact]
    public void Targets_ALayoutWithNoDescriptionAtAll_IsStillOffered_UnderASynthesisedName()
    {
        var harness = NewHarness();
        harness.Store.Save("NOMARKER", PlainLayout(), KnownActions);

        Assert.Equal("Unknown device NOMARKER", Assert.Single(harness.Targets(), t => t.DeviceId == "NOMARKER").Name);
    }

    /// <summary>
    /// A device that has paired and never drawn a panel is a perfectly good
    /// place to send an arrangement, and has nothing to send. The flag is
    /// what keeps it out of one half of the pane and in the other.
    /// </summary>
    [Fact]
    public void Targets_APairedDeviceWithNoLayoutYet_IsListed_WithHasLayoutFalse()
    {
        var target = Assert.Single(NewHarness().Targets("FRESH"), t => t.DeviceId == "FRESH");

        Assert.False(target.HasLayout);
    }

    [Fact]
    public void Targets_TheSameDeviceIsNeverListedTwice()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE1", PlainLayout(), KnownActions);
        harness.Markers.Write(new OrphanMarker("LIVE1", "Stale marker", "phone", Now));

        var targets = harness.Targets("LIVE1");

        Assert.Equal(targets.Select(t => t.DeviceId).Distinct(StringComparer.Ordinal).Count(), targets.Count);
        // And the live name wins over the stale marker, the same rule
        // LayoutImport.BuildCandidates already follows.
        Assert.Equal("Tablet LIVE1", Assert.Single(targets, t => t.DeviceId == "LIVE1").Name);
    }

    // -----------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------

    [Fact]
    public void ExportProfile_CarriesTheWholeArrangement()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE1", PlainLayout(), KnownActions);

        var result = TransferEndpoint.ExportProfile(harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", Now);

        Assert.Equal(TransferEndpoint.ExportOutcome.Exported, result.Outcome);
        var parsed = TransferFile.Parse(result.Json);
        Assert.True(parsed.Success, parsed.Error);
        AssertSameLayout(PlainLayout(), parsed.Layout!);
    }

    /// <summary>
    /// The file names itself after the device, not after its id - a hex
    /// string in a downloads folder is a thing nobody can pick from later.
    /// </summary>
    [Fact]
    public void ExportProfile_NamesTheFileAfterTheDevice()
    {
        var harness = NewHarness();
        harness.Store.Save("LIVE1", PlainLayout(), KnownActions);

        var result = TransferEndpoint.ExportProfile(harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", Now);

        Assert.Equal("lunapanel-profile-tablet-live1-20260910.lunapanel.json", result.FileName);
    }

    /// <summary>
    /// Carries the macros its buttons name and no others. Three cases in one
    /// test on purpose - the referenced one, the unreferenced one and the
    /// shipped one - because each is separately capable of being wrong and a
    /// test that only exercised the first would pass against an export that
    /// simply carried everything.
    /// </summary>
    [Fact]
    public void ExportProfile_CarriesReferencedUserMacros_NotUnreferencedOnes_AndNeverAShippedOne()
    {
        var harness = NewHarness();
        harness.UserMacros.Save(Macro("user-aaa111", "Referenced"));
        harness.UserMacros.Save(Macro("user-ccc333", "Unreferenced"));
        var shippedId = MacroLoader.LoadShipped()[0].Id;

        harness.Store.Save(
            "LIVE1",
            LayoutWith(
                new LayoutSlot(0, null, "user-aaa111", null, null),
                new LayoutSlot(1, null, shippedId, null, null)),
            KnownActions);

        var result = TransferEndpoint.ExportProfile(harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", Now);

        var parsed = TransferFile.Parse(result.Json);
        Assert.Equal("user-aaa111", Assert.Single(parsed.Macros).Id);
    }

    /// <summary>
    /// A long-press is a second thing the commander set on that button. An
    /// export that carried only the tap's macro would produce a profile
    /// whose long-presses died on arrival.
    /// </summary>
    [Fact]
    public void ExportProfile_CarriesAMacroNamedOnlyByALongPress()
    {
        var harness = NewHarness();
        harness.UserMacros.Save(Macro("user-bbb222", "Held"));
        harness.Store.Save(
            "LIVE1",
            LayoutWith(new LayoutSlot(0, "LandingGearToggle", null, null, new LongPressAction(null, "user-bbb222"))),
            KnownActions);

        var result = TransferEndpoint.ExportProfile(harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", Now);

        Assert.Equal("user-bbb222", Assert.Single(TransferFile.Parse(result.Json).Macros).Id);
    }

    [Fact]
    public void ExportProfile_ADeviceThatIsNotOnTheList_IsRefused()
    {
        var harness = NewHarness();

        var result = TransferEndpoint.ExportProfile(harness.Store, harness.Catalogue, harness.Targets(), "../../elsewhere", Now);

        Assert.Equal(TransferEndpoint.ExportOutcome.NoSuchDevice, result.Outcome);
        Assert.Null(result.Json);
    }

    [Fact]
    public void ExportProfile_ADeviceWithNoLayoutYet_SaysSoByName()
    {
        var harness = NewHarness();

        var result = TransferEndpoint.ExportProfile(harness.Store, harness.Catalogue, harness.Targets("FRESH"), "FRESH", Now);

        Assert.Equal(TransferEndpoint.ExportOutcome.NoLayout, result.Outcome);
        Assert.Contains("Tablet FRESH", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportMacros_OneMacro_CarriesExactlyThatOne()
    {
        var harness = NewHarness();
        harness.UserMacros.Save(Macro("user-aaa111", "Dock"));
        harness.UserMacros.Save(Macro("user-ccc333", "Undock"));

        var parsed = TransferFile.Parse(TransferEndpoint.ExportMacros(harness.Catalogue, "user-aaa111", Now).Json);

        Assert.Equal(TransferKind.Macros, parsed.Kind);
        Assert.Equal("user-aaa111", Assert.Single(parsed.Macros).Id);
    }

    [Fact]
    public void ExportMacros_NoIdAtAll_CarriesEveryUserMacro_AndNoShippedOne()
    {
        var harness = NewHarness();
        harness.UserMacros.Save(Macro("user-aaa111", "Dock"));
        harness.UserMacros.Save(Macro("user-ccc333", "Undock"));

        var parsed = TransferFile.Parse(TransferEndpoint.ExportMacros(harness.Catalogue, null, Now).Json);

        Assert.Equal(new[] { "user-aaa111", "user-ccc333" }, parsed.Macros.Select(m => m.Id));
    }

    /// <summary>
    /// The receiving copy ships the identical macro under the identical id,
    /// so exporting one could only ever be a no-op or a stale shadow of a
    /// later fix. Refused with the same outcome an unknown id gets.
    /// </summary>
    [Fact]
    public void ExportMacros_AShippedMacro_IsRefused()
    {
        var harness = NewHarness();

        var result = TransferEndpoint.ExportMacros(harness.Catalogue, MacroLoader.LoadShipped()[0].Id, Now);

        Assert.Equal(TransferEndpoint.ExportOutcome.NoSuchMacro, result.Outcome);
    }

    [Fact]
    public void ExportMacros_OnAMachineWithNoneOfItsOwn_SaysSo_RatherThanWritingAnEmptyFile()
    {
        var result = TransferEndpoint.ExportMacros(NewHarness().Catalogue, null, Now);

        Assert.Equal(TransferEndpoint.ExportOutcome.NothingToExport, result.Outcome);
        Assert.Null(result.Json);
    }

    // -----------------------------------------------------------------
    // Import - the round trip
    // -----------------------------------------------------------------

    /// <summary>
    /// The founding claim of the whole feature: an arrangement exported from
    /// one device arrives on another intact, macros and all.
    /// </summary>
    [Fact]
    public void ImportProfile_AFileThisMachineExported_ArrivesIntact_MacrosIncluded()
    {
        var source = NewHarness();
        source.UserMacros.Save(Macro("user-aaa111", "Dock"));
        var layout = LayoutWith(
            new LayoutSlot(0, "LandingGearToggle", null, "Gear", null, Latch: true),
            new LayoutSlot(1, null, "user-aaa111", "Dock", new LongPressAction("ToggleCargoScoop", null)));
        source.Store.Save("LIVE1", layout, KnownActions);
        var file = TransferEndpoint.ExportProfile(source.Store, source.Catalogue, source.Targets("LIVE1"), "LIVE1", Now).Json;

        var receiver = NewHarness();
        var result = TransferEndpoint.ImportProfile(
            receiver.Store, receiver.Catalogue, receiver.Targets("OTHER"), "OTHER", file, KnownActions, receiver.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Imported, result.Outcome);
        Assert.Equal(1, result.MacrosAdded);
        Assert.Equal(0, result.ReferencesToMissingMacros);

        var loaded = receiver.Store.Load("OTHER");
        Assert.Equal(LayoutLoadOutcome.Loaded, loaded.Outcome);
        AssertSameLayout(layout, loaded.Layout!);
        Assert.Equal("Dock", receiver.UserMacros.Load("user-aaa111")!.Name);
    }

    /// <summary>
    /// The way back. <see cref="LayoutStore"/>'s own single kept generation,
    /// written because the import went through <see cref="LayoutStore.Save"/>
    /// like every other layout write - not because anything here made a
    /// backup of its own.
    /// </summary>
    [Fact]
    public void ImportProfile_LeavesABakOfWhatWasThere_AndUndoPutsItBack()
    {
        var harness = NewHarness();
        var before = PlainLayout("ToggleCargoScoop");
        harness.Store.Save("LIVE1", before, KnownActions);
        var file = TransferFile.WriteProfile(PlainLayout("ShipSpotLightToggle"), Array.Empty<MacroDefinition>(), "Elsewhere", Now);

        TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", file, KnownActions, harness.Log);

        Assert.True(File.Exists(Path.Combine(harness.Directory, "layout-LIVE1.json.bak")));
        Assert.True(TransferEndpoint.Undo(harness.Store, harness.Targets("LIVE1"), "LIVE1"));
        AssertSameLayout(before, harness.Store.Load("LIVE1").Layout!);
    }

    /// <summary>
    /// A device that has never drawn a panel is seeded first, so the import
    /// displaces something and the undo means the same thing it does months
    /// later - the identical reason
    /// <c>LayoutImportEndpoint.Import</c> calls <c>LoadOrSeed</c>.
    /// </summary>
    [Fact]
    public void ImportProfile_OntoADeviceWithNoLayoutYet_StillLeavesSomethingToUndoTo()
    {
        var harness = NewHarness();
        var file = TransferFile.WriteProfile(PlainLayout(), Array.Empty<MacroDefinition>(), "Elsewhere", Now);

        TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("FRESH"), "FRESH", file, KnownActions, harness.Log);

        Assert.True(TransferEndpoint.Undo(harness.Store, harness.Targets("FRESH"), "FRESH"));
        Assert.Equal(LayoutLoadOutcome.Loaded, harness.Store.Load("FRESH").Outcome);
    }

    /// <summary>
    /// <b>Characterisation, not a pin - measured, not assumed.</b> Mutation
    /// M6 (2026-09-10) removed the targets-list guard from
    /// <c>TransferEndpoint.Find</c> and reddened the export and import tests
    /// as predicted; this one stayed green, which was predicted with it. The
    /// reason is structural: every id with a layout file is ON the targets
    /// list, so an id that is not on it has no layout and therefore no
    /// <c>.bak</c>, and <see cref="LayoutStore.RestorePrevious"/> refuses it
    /// on its own. The guard's value here is that
    /// <see cref="LayoutStore"/> is never handed a device id shaped like a
    /// path - which is a property of the code, not a behaviour this suite can
    /// observe. Recorded as O40 rather than dressed up as coverage.
    /// </summary>
    [Fact]
    public void Undo_ADeviceThatIsNotOnTheList_IsRefused()
    {
        var harness = NewHarness();

        Assert.False(TransferEndpoint.Undo(harness.Store, harness.Targets(), "../../elsewhere"));
    }

    // -----------------------------------------------------------------
    // Import - refusals, and what they must not have changed
    // -----------------------------------------------------------------

    /// <summary>
    /// The claim the brief calls out by name: a malformed or hostile file
    /// changes nothing at all. Every arm is checked against the same
    /// untouched device, so a refusal that half-wrote would show up as the
    /// layout no longer being the one that was there.
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "name": "something-else" }""")]
    [InlineData("""{ "lunaPanel": "profile", "formatVersion": 99, "layout": { "schemaVersion": 1, "pages": [] } }""")]
    [InlineData("""{ "lunaPanel": "profile", "formatVersion": 1, "layout": { "nope": true } }""")]
    [InlineData("""{ "lunaPanel": "macros", "formatVersion": 1, "macros": [] }""")]
    public void ImportProfile_AFileThatIsNotAUsableProfile_IsRefused_AndChangesNothing(string file)
    {
        var harness = NewHarness();
        var before = PlainLayout("ToggleCargoScoop");
        harness.Store.Save("LIVE1", before, KnownActions);

        var result = TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", file, KnownActions, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        AssertSameLayout(before, harness.Store.Load("LIVE1").Layout!);
    }

    /// <summary>
    /// <c>UserMacroStore</c> turns an id straight into a file name, and this
    /// is the first path in the project where an id arrives as content. The
    /// id here passes <c>UserMacroIds.IsUserMacroId</c>, which is exactly why
    /// that check alone is not the one made.
    /// </summary>
    [Fact]
    public void ImportMacros_AnIdShapedLikeAPath_IsRefused_AndWritesNoFileAnywhere()
    {
        var harness = NewHarness();
        var file =
            """
            {
              "lunaPanel": "macros",
              "formatVersion": 1,
              "macros": [ { "id": "user-../../../evil", "name": "Bad", "steps": [ { "wait": 5 } ] } ]
            }
            """;

        var result = TransferEndpoint.ImportMacros(harness.Catalogue, file, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, result.Outcome);
        Assert.Empty(harness.UserMacros.LoadAll());
        // The store's own directory, which is where a well-formed id would
        // have landed. A traversal that succeeded would write OUTSIDE it, so
        // the refusal above is the assertion that matters and this is only
        // the "and nothing landed here either" half.
        Assert.Empty(Directory.GetFiles(Path.Combine(harness.Directory, "macros")));
    }

    /// <summary>
    /// A file cannot introduce a macro that shadows a shipped one.
    /// <see cref="MacroCatalogue"/> would ignore such a file anyway, which is
    /// precisely why writing it would be worse than refusing: the import
    /// would report success and nothing would change.
    ///
    /// <b>This characterises an outcome; it is not a pin on any one line, and
    /// nothing can make it into one.</b> Measured 2026-09-10 (mutation M18,
    /// predicted 1 red, actual 0): deleting
    /// <c>TransferEndpoint.CheckMacroIds</c>' shipped-id clause reddens
    /// nothing, because <c>UserMacroIds.IsWellFormed</c> already refuses every
    /// id that does not begin with the reserved prefix and
    /// <see cref="UserMacroIdsTests.NoShippedMacroId_UsesTheReservedUserPrefix"/>
    /// proves no shipped id does. Two independent rules refuse this file, so
    /// no single-clause mutation can reach it - said here rather than left to
    /// be re-discovered, because a test that cannot fail looks exactly like
    /// one that guards something.
    /// </summary>
    [Fact]
    public void ImportMacros_AShippedId_IsRefused()
    {
        var harness = NewHarness();
        var shippedId = MacroLoader.LoadShipped()[0].Id;
        var file = TransferFile.WriteMacros(new[] { Macro(shippedId, "Impostor") }, "Elsewhere", Now);

        var result = TransferEndpoint.ImportMacros(harness.Catalogue, file, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, result.Outcome);
        Assert.Empty(harness.UserMacros.LoadAll());
    }

    /// <summary>
    /// One bad macro refuses the whole file, and the good one beside it is
    /// not written either. A partial import is the state nothing here is
    /// allowed to leave behind.
    /// </summary>
    [Fact]
    public void ImportMacros_OneBadIdAmongGoodOnes_WritesNoneOfThem()
    {
        var harness = NewHarness();
        var file =
            """
            {
              "lunaPanel": "macros",
              "formatVersion": 1,
              "macros": [
                { "id": "user-aaa111", "name": "Fine", "steps": [ { "wait": 5 } ] },
                { "id": "user-NOTHEX", "name": "Bad", "steps": [ { "wait": 5 } ] }
              ]
            }
            """;

        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, TransferEndpoint.ImportMacros(harness.Catalogue, file, harness.Log).Outcome);
        Assert.Empty(harness.UserMacros.LoadAll());
    }

    /// <summary>
    /// The layout is validated before a single macro is written, so a
    /// profile refused for naming a control this PC's Elite does not have
    /// costs no macros either.
    /// </summary>
    [Fact]
    public void ImportProfile_NamingAnActionThisMachineDoesNotHave_IsRefused_AndWritesNoMacros()
    {
        var harness = NewHarness();
        var file = TransferFile.WriteProfile(
            LayoutWith(new LayoutSlot(0, "SomeActionFromAnotherGame", null, null, null)),
            new[] { Macro("user-aaa111", "Dock") },
            "Elsewhere",
            Now);

        var result = TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", file, KnownActions, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, result.Outcome);
        Assert.Contains("SomeActionFromAnotherGame", result.Error!, StringComparison.Ordinal);
        Assert.Empty(harness.UserMacros.LoadAll());
    }

    [Fact]
    public void ImportProfile_ADeviceThatIsNotOnTheList_IsRefused_BeforeTheFileIsEvenRead()
    {
        var harness = NewHarness();

        var result = TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets(), "../../elsewhere", "not json at all", KnownActions, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.NoSuchTarget, result.Outcome);
    }

    /// <summary>
    /// The two kinds are told apart and each sends the commander to the
    /// other half of the pane, rather than being quietly accepted by the
    /// wrong importer.
    /// </summary>
    [Fact]
    public void TheTwoKinds_AreNotInterchangeable_AndEachRefusalSaysWhereToGo()
    {
        var harness = NewHarness();
        var profileFile = TransferFile.WriteProfile(PlainLayout(), Array.Empty<MacroDefinition>(), "Elsewhere", Now);
        var macroFile = TransferFile.WriteMacros(new[] { Macro("user-aaa111") }, "Elsewhere", Now);

        var wrongWay = TransferEndpoint.ImportMacros(harness.Catalogue, profileFile, harness.Log);
        var otherWay = TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", macroFile, KnownActions, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, wrongWay.Outcome);
        Assert.Contains("under Profile", wrongWay.Error!, StringComparison.Ordinal);
        Assert.Equal(TransferEndpoint.ImportOutcome.Refused, otherWay.Outcome);
        Assert.Contains("under Macros", otherWay.Error!, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // Import - the four decisions, driven
    // -----------------------------------------------------------------

    /// <summary>
    /// Add, never overwrite. The commander's own version of a macro whose id
    /// the file also carries is kept, byte for byte - which is what makes
    /// importing the same profile twice a no-op instead of a pile of
    /// duplicates, and what stops a file quietly replacing work.
    /// </summary>
    [Fact]
    public void ImportMacros_AnIdThisMachineAlreadyHas_IsLeftExactlyAsItWas()
    {
        var harness = NewHarness();
        harness.UserMacros.Save(Macro("user-aaa111", "Mine"));
        var file = TransferFile.WriteMacros(new[] { Macro("user-aaa111", "Theirs") }, "Elsewhere", Now);

        var result = TransferEndpoint.ImportMacros(harness.Catalogue, file, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Imported, result.Outcome);
        Assert.Equal(0, result.MacrosAdded);
        Assert.Equal(1, result.MacrosAlreadyPresent);
        Assert.Equal("Mine", harness.UserMacros.Load("user-aaa111")!.Name);
    }

    [Fact]
    public void ImportMacros_TheSameFileTwice_AddsNothingTheSecondTime()
    {
        var harness = NewHarness();
        var file = TransferFile.WriteMacros(new[] { Macro("user-aaa111", "Dock") }, "Elsewhere", Now);

        Assert.Equal(1, TransferEndpoint.ImportMacros(harness.Catalogue, file, harness.Log).MacrosAdded);
        Assert.Equal(0, TransferEndpoint.ImportMacros(harness.Catalogue, file, harness.Log).MacrosAdded);
        Assert.Single(harness.UserMacros.LoadAll());
    }

    /// <summary>
    /// A profile naming a macro nothing here defines is imported, counted
    /// and reported - never refused. Those buttons behave exactly as any
    /// slot naming an absent macro already does
    /// (<c>ref/docs/macro-builder.md</c>), which is the existing handling the
    /// brief asked be preferred over a new rule.
    /// </summary>
    [Fact]
    public void ImportProfile_NamingAMacroThisMachineDoesNotHave_StillImports_AndSaysHowMany()
    {
        var harness = NewHarness();
        var file = TransferFile.WriteProfile(
            LayoutWith(
                new LayoutSlot(0, null, "user-ffffff", null, null),
                new LayoutSlot(1, "LandingGearToggle", null, null, new LongPressAction(null, "user-eeeeee"))),
            Array.Empty<MacroDefinition>(),
            "Elsewhere",
            Now);

        var result = TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", file, KnownActions, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Imported, result.Outcome);
        // Two references, one of them a long-press - counted separately
        // because they are two things a commander set.
        Assert.Equal(2, result.ReferencesToMissingMacros);
        Assert.Equal(LayoutLoadOutcome.Loaded, harness.Store.Load("LIVE1").Outcome);
    }

    /// <summary>
    /// A shipped macro named by an imported profile resolves on arrival - it
    /// is not carried in the file and it is not missing either, which is the
    /// whole reason not carrying it is safe.
    /// </summary>
    [Fact]
    public void ImportProfile_NamingAShippedMacro_CountsNothingAsMissing()
    {
        var harness = NewHarness();
        var shippedId = MacroLoader.LoadShipped()[0].Id;
        var file = TransferFile.WriteProfile(
            LayoutWith(new LayoutSlot(0, null, shippedId, null, null)),
            Array.Empty<MacroDefinition>(),
            "Elsewhere",
            Now);

        var result = TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("LIVE1"), "LIVE1", file, KnownActions, harness.Log);

        Assert.Equal(TransferEndpoint.ImportOutcome.Imported, result.Outcome);
        Assert.Equal(0, result.ReferencesToMissingMacros);
    }

    /// <summary>
    /// Cross-size import is allowed, and the imported pages keep their own
    /// <c>templateId</c> - the ruling <c>ref/docs/layout-import.md</c> made
    /// for the in-place copy, applied unchanged here rather than a second,
    /// differing rule for files.
    /// </summary>
    [Fact]
    public void ImportProfile_ATemplateTheReceivingDeviceMayNotFit_IsCopiedVerbatim()
    {
        var harness = NewHarness();
        var big = new Layout(
            LayoutMigrator.CurrentSchemaVersion,
            new[] { new LayoutPage("SHIP", "t30", new[] { new LayoutSlot(0, "LandingGearToggle", null, null, null) }, Array.Empty<LayoutSlot>()) });
        var file = TransferFile.WriteProfile(big, Array.Empty<MacroDefinition>(), "Tablet", Now);

        TransferEndpoint.ImportProfile(
            harness.Store, harness.Catalogue, harness.Targets("PHONE"), "PHONE", file, KnownActions, harness.Log);

        Assert.Equal("t30", Assert.Single(harness.Store.Load("PHONE").Layout!.Pages).TemplateId);
    }
}
