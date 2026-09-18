using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Macros;
using LunaPanel.Server.Macros;

namespace LunaPanel.Tests.Macros;

/// <summary>
/// Drives <see cref="UserMacroStore"/> against real temp directories under
/// the test output (never the repo tree or the commander's real profile) -
/// same discipline as <c>MacroTimingSettingsStoreTests</c> and
/// <c>LunaPanel.Tests.Layouts.LayoutStoreTests</c>.
///
/// The behaviours pinned here are the ones <c>ref/docs/macro-builder.md</c>
/// argued for: machine-wide (no device id anywhere in this file's API),
/// one file per macro, a kept <c>.bak</c> generation, and a corrupt file
/// that costs exactly one macro instead of the whole panel.
/// </summary>
public class UserMacroStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "user-macro-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static MacroDefinition Macro(string id, string name = "Test", string steps = """[ { "press": "UI_Down", "repeat": 2 } ]""") =>
        MacroDefinition.Parse($$"""{ "id": "{{id}}", "name": "{{name}}", "steps": {{steps}} }""");

    [Fact]
    public void LoadAll_NothingSavedYet_IsEmpty_NotAnError()
    {
        var store = new UserMacroStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.Empty(store.LoadAll());
    }

    /// <summary>
    /// The founding round trip: what a commander authored comes back as the
    /// same macro, with the same steps, through a fresh store instance -
    /// which is what a real host restart looks like from disk's point of
    /// view.
    /// </summary>
    [Fact]
    public void Save_ThenLoadFromAFreshStore_RoundTripsIdNameAndSteps()
    {
        var dir = NewTempDir();
        // The name carries a JSON-escaped newline, the way request-docking's
        // own two-line name does (ref/docs/button-naming.md's two-lines-of-
        // twelve budget) - the case a writer that quoted names naively would
        // corrupt, and the one a single-line example would never reach.
        var saved = Macro("user-abc123", @"My\nMacro", """[ { "require": ["Docked"] }, { "press": "UI_Down", "repeat": 3 }, { "wait": 150 } ]""");

        new UserMacroStore(dir, new CapturingDiagnosticLog()).Save(saved);
        var loaded = new UserMacroStore(dir, new CapturingDiagnosticLog()).Load("user-abc123");

        Assert.NotNull(loaded);
        Assert.Equal("user-abc123", loaded!.Id);
        Assert.Equal("My\nMacro", loaded.Name);
        Assert.Equal(3, loaded.Steps.Count);
        Assert.IsType<RequireStep>(loaded.Steps[0]);
        Assert.Equal("UI_Down", Assert.IsType<PressStep>(loaded.Steps[1]).Action);
        Assert.Equal(3, Assert.IsType<PressStep>(loaded.Steps[1]).Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<WaitStep>(loaded.Steps[2]).Duration);
    }

    /// <summary>
    /// One file per macro, named for the id - not one file holding all of
    /// them. Pinned by the file name itself rather than only by behaviour,
    /// because the whole "a corrupt file costs one macro" property below
    /// depends on this layout and would be silently lost by a rewrite to a
    /// single file that still round-tripped.
    /// </summary>
    [Fact]
    public void Save_WritesOneFilePerMacro_NamedForItsId()
    {
        var dir = NewTempDir();
        var store = new UserMacroStore(dir, new CapturingDiagnosticLog());

        store.Save(Macro("user-one"));
        store.Save(Macro("user-two"));

        Assert.True(File.Exists(Path.Combine(dir, "macro-user-one.json")));
        Assert.True(File.Exists(Path.Combine(dir, "macro-user-two.json")));
        Assert.Equal(2, store.LoadAll().Count);
    }

    /// <summary>
    /// No device id anywhere in this API - the machine-wide decision
    /// (<c>ref/docs/macro-builder.md</c>, question 2), pinned as the
    /// observable consequence rather than as an absent parameter: a second
    /// store over the same directory, which is what a second paired device
    /// gets, sees the first one's macros.
    /// </summary>
    [Fact]
    public void LoadAll_IsSharedAcrossDevices_NotKeyedPerDevice()
    {
        var dir = NewTempDir();
        new UserMacroStore(dir, new CapturingDiagnosticLog()).Save(Macro("user-shared", "Shared"));

        var secondDevicesView = new UserMacroStore(dir, new CapturingDiagnosticLog()).LoadAll();

        Assert.Equal("user-shared", Assert.Single(secondDevicesView).Id);
    }

    [Fact]
    public void LoadAll_IsOrderedById_SoTwoReadsOfAnUnchangedDirectoryAgree()
    {
        var store = new UserMacroStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(Macro("user-ccc"));
        store.Save(Macro("user-aaa"));
        store.Save(Macro("user-bbb"));

        Assert.Equal(new[] { "user-aaa", "user-bbb", "user-ccc" }, store.LoadAll().Select(m => m.Id));
    }

    [Fact]
    public void Save_Twice_OverwritesInPlace_AndKeepsThePreviousGenerationAsBak()
    {
        var dir = NewTempDir();
        var store = new UserMacroStore(dir, new CapturingDiagnosticLog());

        store.Save(Macro("user-edit", "First", """[ { "wait": 10 } ]"""));
        store.Save(Macro("user-edit", "Second", """[ { "wait": 20 } ]"""));

        Assert.Equal("Second", store.Load("user-edit")!.Name);
        Assert.Single(store.LoadAll());
        var bak = Path.Combine(dir, "macro-user-edit.json.bak");
        Assert.True(File.Exists(bak));
        Assert.Contains("First", File.ReadAllText(bak));
    }

    /// <summary>
    /// A <c>.bak</c> generation is a sibling file in the same directory, and
    /// the enumeration must not offer it as a second, phantom macro - the
    /// exact failure <c>LayoutStore.ListDeviceIds</c>' own remarks describe
    /// for a layout <c>.bak</c> being read as another device.
    /// </summary>
    [Fact]
    public void LoadAll_NeverListsABakOrCorruptGenerationAsASecondMacro()
    {
        var dir = NewTempDir();
        var store = new UserMacroStore(dir, new CapturingDiagnosticLog());
        store.Save(Macro("user-gen", "First"));
        store.Save(Macro("user-gen", "Second"));
        File.WriteAllText(Path.Combine(dir, "macro-user-gen.json.corrupt-20260909120000000"), "{ nonsense");

        Assert.Equal("user-gen", Assert.Single(store.LoadAll()).Id);
    }

    [Fact]
    public void Delete_RemovesTheMacro_AndReportsTrue()
    {
        var store = new UserMacroStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(Macro("user-gone"));

        Assert.True(store.Delete("user-gone"));
        Assert.Null(store.Load("user-gone"));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Delete_NoSuchMacro_ReportsFalse_AndChangesNothing()
    {
        var store = new UserMacroStore(NewTempDir(), new CapturingDiagnosticLog());
        store.Save(Macro("user-kept"));

        Assert.False(store.Delete("user-never-existed"));
        Assert.Single(store.LoadAll());
    }

    /// <summary>
    /// The one that keeps a damaged file from taking the panel down. These
    /// definitions are read on EVERY panel and press request, so a
    /// hand-mangled file that threw would cost every button, not one.
    /// Renamed aside like a corrupt layout, logged at Error, never
    /// overwritten in place, and the healthy macro beside it still loads.
    /// </summary>
    [Fact]
    public void LoadAll_CorruptFile_IsRenamedAside_AndCostsOnlyThatOneMacro()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new UserMacroStore(dir, log);
        store.Save(Macro("user-healthy", "Healthy"));
        File.WriteAllText(Path.Combine(dir, "macro-user-broken.json"), "{ this is not json");

        var loaded = store.LoadAll();

        Assert.Equal("user-healthy", Assert.Single(loaded).Id);
        Assert.False(File.Exists(Path.Combine(dir, "macro-user-broken.json")));
        Assert.Single(Directory.EnumerateFiles(dir, "macro-user-broken.json.corrupt-*"));
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Error && e.Category == "Macro");
    }

    /// <summary>
    /// Valid JSON that is not a valid macro (an unknown condition token -
    /// exactly what a hand-edit or an older schema produces) takes the same
    /// path as unreadable JSON. It reaches
    /// <see cref="MacroDefinition.TryParse"/>, not <c>Parse</c>: the
    /// difference is invisible when it succeeds and is the whole point when
    /// it does not.
    /// </summary>
    [Fact]
    public void LoadAll_ValidJsonButInvalidMacro_IsRenamedAside_RatherThanThrowing()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new UserMacroStore(dir, log);
        File.WriteAllText(
            Path.Combine(dir, "macro-user-badtoken.json"),
            """{ "id": "user-badtoken", "steps": [ { "require": ["NotARealFlag"] } ] }""");

        var loaded = store.LoadAll();

        Assert.Empty(loaded);
        Assert.Single(Directory.EnumerateFiles(dir, "macro-user-badtoken.json.corrupt-*"));
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Error && e.Detail is not null && e.Detail.Contains("NotARealFlag"));
    }

    /// <summary>
    /// The never-gate rule, at the storage layer
    /// (<c>.claude-memory/never-gate-a-macro.md</c>): a macro whose action
    /// is bound to nothing, whose <c>repeat</c> is enormous, and whose
    /// <c>pressUntil</c> re-presses a contextual toggle saves without
    /// argument. None of that is this store's to refuse; a degraded macro is
    /// reported as degraded at read time and refused at press time by
    /// machinery that already exists.
    /// </summary>
    [Fact]
    public void Save_NeverRefusesAMacroForBeingUnwise_NoAuthoringCapAnywhere()
    {
        var store = new UserMacroStore(NewTempDir(), new CapturingDiagnosticLog());
        var reckless = Macro(
            "user-reckless",
            "Reckless",
            """[ { "press": "NotBoundToAnything", "repeat": 100000 }, { "wait": 3600000 }, { "pressUntil": "UI_Select", "cond": ["Docked"], "timeoutMs": 500, "maxAttempts": 50 } ]""");

        store.Save(reckless);

        var loaded = store.Load("user-reckless");
        Assert.NotNull(loaded);
        Assert.Equal(100000, Assert.IsType<PressStep>(loaded!.Steps[0]).Repeat);
        Assert.Equal(TimeSpan.FromMilliseconds(3600000), Assert.IsType<WaitStep>(loaded.Steps[1]).Duration);
    }

    /// <summary>
    /// Copy-to-edit (<c>ref/docs/macro-builder.md</c>, question 3): a copy
    /// of a shipped macro under a fresh id is a user macro from that moment,
    /// and editing it does not reach back into the shipped one. Driven
    /// against a genuinely shipped definition rather than a hand-written
    /// stand-in, because "the copy is independent" is only interesting for
    /// the objects the real loader produces.
    /// </summary>
    [Fact]
    public void Save_ACopyOfAShippedMacro_IsIndependentOfItsOriginal()
    {
        var dir = NewTempDir();
        var store = new UserMacroStore(dir, new CapturingDiagnosticLog());
        var shipped = MacroLoader.LoadShipped().Single(m => m.Id == "disembark");

        // [2026-09-17] disembark's presses moved INSIDE a branch's arms when
        // that macro became conditional, so `copy.Steps.OfType<PressStep>()`
        // - what this test used to edit and then assert on - now matches
        // nothing at all and the test failed with "sequence contains no
        // elements". Taught to walk the arms rather than repointed at some
        // other shipped macro: the claim (a saved copy is independent of the
        // shipped original) is unchanged, and it is now made against a macro
        // that actually contains nested steps, which is the harder case.
        var copy = shipped with { Id = "user-mydisembark", Name = "Mine" };
        store.Save(copy);
        var edited = MacroDefinition.Parse(MacroJson.Serialize(copy with
        {
            Steps = copy.Steps.Select(BumpEveryPressTo9).ToList(),
        }));
        store.Save(edited);

        var reloaded = store.Load("user-mydisembark")!;
        Assert.Equal(9, Flatten(reloaded.Steps).OfType<PressStep>().First().Repeat);
        Assert.Equal("Mine", reloaded.Name);

        // The shipped macro is an embedded resource; a fresh load of it is
        // the only honest way to ask whether anything reached back into it.
        var shippedAfter = MacroLoader.LoadShipped().Single(m => m.Id == "disembark");
        Assert.Equal(3, Flatten(shippedAfter.Steps).OfType<PressStep>().First(p => p.Action == "UI_Down").Repeat);
        Assert.Equal("Disembark", shippedAfter.Name);
        Assert.Null(store.Load("disembark"));
    }

    /// <summary>Every step, arms included, in run-file order.</summary>
    private static IEnumerable<MacroStep> Flatten(IEnumerable<MacroStep> steps) =>
        steps.SelectMany(step => step is BranchStep branch
            ? new[] { step }.Concat(Flatten(branch.Then)).Concat(Flatten(branch.Else))
            : new[] { step });

    private static MacroStep BumpEveryPressTo9(MacroStep step) => step switch
    {
        PressStep press => press with { Repeat = 9 },
        BranchStep branch => branch with
        {
            Then = branch.Then.Select(BumpEveryPressTo9).ToList(),
            Else = branch.Else.Select(BumpEveryPressTo9).ToList(),
        },
        _ => step,
    };

    // -------------------------------------------------------------------
    // Copy-to-edit staleness metadata (ref/docs/macro-builder.md, question 3).
    // -------------------------------------------------------------------

    /// <summary>
    /// The founding round trip for provenance: a macro saved with a source
    /// id and hash comes back carrying both, through a fresh store instance
    /// - the same "survives a real host restart" bar
    /// <see cref="Save_ThenLoadFromAFreshStore_RoundTripsIdNameAndSteps"/>
    /// holds the macro itself to.
    /// </summary>
    [Fact]
    public void Save_WithSourceInfo_ThenLoadRecordFromAFreshStore_RoundTripsBothFields()
    {
        var dir = NewTempDir();
        new UserMacroStore(dir, new CapturingDiagnosticLog())
            .Save(Macro("user-copy"), sourceMacroId: "disembark", sourceStepsHash: "abc123");

        var record = new UserMacroStore(dir, new CapturingDiagnosticLog()).LoadRecord("user-copy");

        Assert.NotNull(record);
        Assert.Equal("disembark", record!.SourceMacroId);
        Assert.Equal("abc123", record.SourceStepsHash);
        Assert.Equal("user-copy", record.Macro.Id);
    }

    /// <summary>
    /// A macro saved with no source (the ordinary case: authored from
    /// scratch, or an edit that carried none forward) reports both fields
    /// null - never an empty string standing in for "none", which would be
    /// indistinguishable from a genuinely empty hash if one were ever
    /// produced.
    /// </summary>
    [Fact]
    public void Save_WithNoSourceInfo_LoadRecord_ReportsBothFieldsNull()
    {
        var dir = NewTempDir();
        new UserMacroStore(dir, new CapturingDiagnosticLog()).Save(Macro("user-plain"));

        var record = new UserMacroStore(dir, new CapturingDiagnosticLog()).LoadRecord("user-plain");

        Assert.NotNull(record);
        Assert.Null(record!.SourceMacroId);
        Assert.Null(record.SourceStepsHash);
    }

    /// <summary>
    /// <see cref="UserMacroStore.LoadAll"/> and <see cref="UserMacroStore.Load"/>
    /// - the two entry points every existing caller (the catalogue, the
    /// runner) already uses - must not change shape or behaviour just
    /// because a macro now carries provenance beside it on disk.
    /// </summary>
    [Fact]
    public void LoadAll_AndLoad_AreUnaffectedByAMacroCarryingSourceInfo()
    {
        var dir = NewTempDir();
        var store = new UserMacroStore(dir, new CapturingDiagnosticLog());
        store.Save(Macro("user-copy"), sourceMacroId: "disembark", sourceStepsHash: "abc123");

        Assert.Equal("user-copy", Assert.Single(store.LoadAll()).Id);
        Assert.Equal("user-copy", store.Load("user-copy")!.Id);
    }

    /// <summary>
    /// A from-scratch macro's file is byte-identical to what it was before
    /// this feature existed - <see cref="UserMacroStore.Save"/>'s own
    /// remarks promise this, and it is what keeps a shipped macro (which is
    /// never saved through this store at all, but is read by the identical
    /// <c>MacroDefinition.TryParse</c> call) unaffected in principle as well
    /// as in practice.
    /// </summary>
    [Fact]
    public void Save_WithNoSourceInfo_WritesNoSourceFieldsAtAll()
    {
        var dir = NewTempDir();
        new UserMacroStore(dir, new CapturingDiagnosticLog()).Save(Macro("user-plain"));

        var text = File.ReadAllText(Path.Combine(dir, "macro-user-plain.json"));

        Assert.DoesNotContain("sourceMacroId", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sourceStepsHash", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A corrupt file's source metadata is exactly as unreachable as its
    /// steps - it takes the same rename-aside path
    /// <see cref="LoadAll_CorruptFile_IsRenamedAside_AndCostsOnlyThatOneMacro"/>
    /// pins, through <see cref="UserMacroStore.LoadRecord"/> this time.
    /// </summary>
    [Fact]
    public void LoadRecord_CorruptFile_IsRenamedAside_ReportsNull_RatherThanThrowing()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new UserMacroStore(dir, log);
        File.WriteAllText(Path.Combine(dir, "macro-user-broken.json"), "{ this is not json");

        Assert.Null(store.LoadRecord("user-broken"));
        Assert.Single(Directory.EnumerateFiles(dir, "macro-user-broken.json.corrupt-*"));
    }
}
