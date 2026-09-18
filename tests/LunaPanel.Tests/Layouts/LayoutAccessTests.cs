using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;
using LunaPanel.Server.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="LayoutAccess.LoadOrSeed"/> against a real
/// <see cref="LayoutStore"/> over a real temp directory (never the repo tree
/// or the user's real profile) - same style as <see cref="LayoutStoreTests"/>.
/// Pins the task brief's seeding rule: only <see cref="LayoutLoadOutcome.NotFound"/>
/// triggers seeding from <see cref="StarterLayout"/>; an existing, corrupt,
/// or too-new layout is never silently replaced.
/// </summary>
public class LayoutAccessTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    // Every real action name the shipped starter layout references, so
    // LayoutValidator.ValidateForSave has something to recognize - built
    // from the shipped catalogue rather than hand-copied, so this list can
    // never silently drift out of sync with the starter layout itself.
    private static IReadOnlySet<string> AllStarterActionNames() =>
        LunaPanel.Server.Catalogue.CatalogueLoader.LoadShipped().Actions.Keys.ToHashSet(StringComparer.Ordinal);

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "layout-access", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LoadOrSeed_NoExistingLayout_SeedsFromStarterLayout_AndPersistsIt()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = AllStarterActionNames();

        var result = LayoutAccess.LoadOrSeed(store, "device-a", known, log);

        Assert.Equal(LayoutLoadOutcome.Loaded, result.Outcome);
        Assert.Equal("t30", result.Layout!.Pages[0].TemplateId);

        // Persisted for real: a brand-new LayoutStore instance over the same
        // directory (no shared in-memory state) now finds it on disk.
        var reloaded = new LayoutStore(dir, log).Load("device-a");
        Assert.Equal(LayoutLoadOutcome.Loaded, reloaded.Outcome);
        // [2026-09-07, superseded: was 30. Five starter-layout actions were
        // freed (indices 24-28); only one of the two new macros needed a
        // freed slot (request-docking already lived at 0), so net is
        // 30 - 5 + 1 = 26 - see StarterLayoutTests for the full accounting.]
        // [2026-09-16, superseded: was 26. The pip-management task filled
        // three more of the freed slots (25-27) - see StarterLayoutTests
        // for the full accounting.]
        Assert.Equal(29, reloaded.Layout!.Pages[0].Slots.Count);
    }

    [Fact]
    public void LoadOrSeed_ExistingLayout_ReturnsItUnchanged_NeverOverwritesWithTheStarter()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = new HashSet<string>(StringComparer.Ordinal) { "ToggleCargoScoop" };

        var custom = new Layout(1, new List<LayoutPage>
        {
            new("SHIP", "t6", new List<LayoutSlot> { new(0, "ToggleCargoScoop", null, null, null) }, new List<LayoutSlot>())
        });
        var saveResult = store.Save("device-b", custom, known);
        Assert.Equal(LayoutSaveOutcome.Saved, saveResult.Outcome);

        var result = LayoutAccess.LoadOrSeed(store, "device-b", known, log);

        Assert.Equal(LayoutLoadOutcome.Loaded, result.Outcome);
        Assert.Equal("t6", result.Layout!.Pages[0].TemplateId);
        Assert.Equal("ToggleCargoScoop", result.Layout.Pages[0].Slots[0].Action);
    }

    [Fact]
    public void LoadOrSeed_KnownActionNamesInsufficientToValidateTheStarter_StillReturnsItInMemory_ButNeverPersists()
    {
        // Simulates a device with no real bindings file discovered yet
        // (empty knownActionNames): LayoutValidator.ValidateForSave rejects
        // every one of the starter's action names, so Save fails - the
        // caller must still get something usable back, but nothing may
        // silently overwrite disk with a layout the validator refused.
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = new HashSet<string>(StringComparer.Ordinal); // empty

        var result = LayoutAccess.LoadOrSeed(store, "device-c", known, log);

        Assert.Equal(LayoutLoadOutcome.Loaded, result.Outcome);
        Assert.Equal("t30", result.Layout!.Pages[0].TemplateId);

        // Nothing was actually written: a fresh LayoutStore over the same
        // directory still reports NotFound.
        var reloaded = new LayoutStore(dir, log).Load("device-c");
        Assert.Equal(LayoutLoadOutcome.NotFound, reloaded.Outcome);

        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn && e.Message.Contains("could not persist"));
    }

    [Fact]
    public void LoadOrSeed_CorruptExistingFile_PassesThroughCorruptOutcome_NeverSeeds()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        File.WriteAllText(Path.Combine(dir, "layout-device-d.json"), "{ not valid json");
        var store = new LayoutStore(dir, log);

        var result = LayoutAccess.LoadOrSeed(store, "device-d", AllStarterActionNames(), log);

        Assert.Equal(LayoutLoadOutcome.Corrupt, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void LoadOrSeed_TooNewSchema_PassesThroughTooNewSchemaOutcome_NeverSeeds_AndLeavesTheFileIntact()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var path = Path.Combine(dir, "layout-device-e.json");
        var tooNewJson = $$"""{ "schemaVersion": {{LayoutMigrator.CurrentSchemaVersion + 1}}, "pages": [] }""";
        File.WriteAllText(path, tooNewJson);
        var store = new LayoutStore(dir, log);

        var result = LayoutAccess.LoadOrSeed(store, "device-e", AllStarterActionNames(), log);

        Assert.Equal(LayoutLoadOutcome.TooNewSchema, result.Outcome);
        Assert.Null(result.Layout);
        Assert.Equal(tooNewJson, File.ReadAllText(path));
    }

    // -------------------------------------------------------------------
    // SeedForNewDevice - proactive, device-class-aware seeding at pairing
    // (2026-09-16). Unlike LoadOrSeed above, this is unconditional: it does
    // not consult LayoutStore.Load first, because the caller (the pairing
    // route) only calls it once it already knows this is a brand-new device.
    // -------------------------------------------------------------------

    [Fact]
    public void SeedForNewDevice_Phone_WritesTheT30Variant_AndPersistsIt()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = AllStarterActionNames();

        var seeded = LayoutAccess.SeedForNewDevice(store, "device-f", "phone", known, log);

        Assert.Equal("t30", seeded.Pages[0].TemplateId);

        var reloaded = new LayoutStore(dir, log).Load("device-f");
        Assert.Equal(LayoutLoadOutcome.Loaded, reloaded.Outcome);
        Assert.Equal("t30", reloaded.Layout!.Pages[0].TemplateId);
    }

    [Fact]
    public void SeedForNewDevice_Tablet_WritesTheT64Variant_AndPersistsIt()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = AllStarterActionNames();

        var seeded = LayoutAccess.SeedForNewDevice(store, "device-g", "tablet", known, log);

        Assert.Equal("t64", seeded.Pages[0].TemplateId);

        var reloaded = new LayoutStore(dir, log).Load("device-g");
        Assert.Equal(LayoutLoadOutcome.Loaded, reloaded.Outcome);
        Assert.Equal("t64", reloaded.Layout!.Pages[0].TemplateId);
    }

    [Fact]
    public void SeedForNewDevice_Unknown_WritesThePhoneVariant()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = AllStarterActionNames();

        var seeded = LayoutAccess.SeedForNewDevice(store, "device-h", "unknown", known, log);

        Assert.Equal("t30", seeded.Pages[0].TemplateId);
    }

    [Fact]
    public void SeedForNewDevice_KnownActionNamesInsufficientToValidate_StillReturnsItInMemory_ButNeverPersists()
    {
        // Same tolerant treatment LoadOrSeed already has, tested above.
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var known = new HashSet<string>(StringComparer.Ordinal); // empty

        var seeded = LayoutAccess.SeedForNewDevice(store, "device-i", "tablet", known, log);

        Assert.Equal("t64", seeded.Pages[0].TemplateId);

        var reloaded = new LayoutStore(dir, log).Load("device-i");
        Assert.Equal(LayoutLoadOutcome.NotFound, reloaded.Outcome);
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn && e.Message.Contains("could not persist"));
    }
}
