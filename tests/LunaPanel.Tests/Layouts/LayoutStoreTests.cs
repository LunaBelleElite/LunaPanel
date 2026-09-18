using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="LayoutStore"/> against real temp directories under the
/// test output (never the repo tree or the user's real profile), pinning:
/// absent-file-is-first-run, atomic save with exactly one kept previous
/// generation, corrupt-file rename-aside (never overwritten in place),
/// newer-schema refusal leaving the file byte-for-byte intact, migration
/// applying only in memory until the next save, and <see cref="LayoutStore.RestorePrevious"/>.
/// </summary>
public class LayoutStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static readonly HashSet<string> KnownActions = new(StringComparer.Ordinal)
    {
        "LandingGearToggle", "ToggleCargoScoop",
    };

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "layouts-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MainPath(string dir, string deviceId) => Path.Combine(dir, $"layout-{deviceId}.json");
    private static string BakPath(string dir, string deviceId) => MainPath(dir, deviceId) + ".bak";

    private static Layout LayoutNaming(string action) =>
        new(1, new[] { new LayoutPage("SHIP", "t6", new[] { new LayoutSlot(0, action, null, null, null) }, Array.Empty<LayoutSlot>()) });

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "layouts", name);

    [Fact]
    public void Load_NoFileForDevice_ReturnsNotFound()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.Equal(LayoutLoadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Layout);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        var layout = LayoutNaming("LandingGearToggle");

        var saveResult = store.Save("device-a", layout, KnownActions);
        Assert.Equal(LayoutSaveOutcome.Saved, saveResult.Outcome);

        var loadResult = store.Load("device-a");

        Assert.Equal(LayoutLoadOutcome.Loaded, loadResult.Outcome);
        Assert.Equal("LandingGearToggle", loadResult.Layout!.Pages[0].Slots[0].Action);
    }

    [Fact]
    public void Save_InvalidLayout_ReturnsValidationFailed_AndWritesNoFile()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        var invalidLayout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "not-a-real-template", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>())
        });

        var result = store.Save("device-a", invalidLayout, KnownActions);

        Assert.Equal(LayoutSaveOutcome.ValidationFailed, result.Outcome);
        Assert.NotEmpty(result.ValidationErrors);
        Assert.False(File.Exists(MainPath(dir, "device-a")));
    }

    [Fact]
    public void Save_TwiceInARow_KeepsExactlyThePreviousGenerationAsBak()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());

        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        Assert.False(File.Exists(BakPath(dir, "device-a")), "No .bak should exist after the first save.");

        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);

        Assert.Contains("ToggleCargoScoop", File.ReadAllText(MainPath(dir, "device-a")));
        Assert.Contains("LandingGearToggle", File.ReadAllText(BakPath(dir, "device-a")));
    }

    [Fact]
    public void Save_ThreeTimesInARow_BakHoldsOnlyTheImmediatelyPriorGeneration_NotTheOneBeforeThat()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());

        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions); // gen 1
        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);  // gen 2, bak = gen 1
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions); // gen 3, bak = gen 2

        Assert.Contains("LandingGearToggle", File.ReadAllText(MainPath(dir, "device-a")));
        Assert.Contains("ToggleCargoScoop", File.ReadAllText(BakPath(dir, "device-a")));
    }

    [Fact]
    public void Save_NeverLeavesATempFileBehind()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());

        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);

        Assert.False(File.Exists(MainPath(dir, "device-a") + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_RenamesAside_ReturnsCorrupt_AndOriginalBytesAreStillReadable()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        var corruptText = File.ReadAllText(FixturePath("corrupt.json"));
        File.WriteAllText(MainPath(dir, "device-a"), corruptText);

        var result = store.Load("device-a");

        Assert.Equal(LayoutLoadOutcome.Corrupt, result.Outcome);
        Assert.False(File.Exists(MainPath(dir, "device-a")), "The corrupt file must not be left at the main path.");

        var renamed = Directory.GetFiles(dir, "layout-device-a.json.corrupt-*");
        var corruptFile = Assert.Single(renamed);
        Assert.Equal(corruptText, File.ReadAllText(corruptFile));

        var errorEvent = Assert.Single(log.Events, e => e.Level == DiagnosticLevel.Error);
        Assert.Equal("Layout", errorEvent.Category);
        Assert.DoesNotContain(dir, errorEvent.Message);
        Assert.NotNull(errorEvent.Detail);
        Assert.DoesNotContain(dir, errorEvent.Detail!);
        // The detail is meant to carry only an exception type name or a
        // structural parse-error description - never a path - so it should
        // never contain this test's own temp directory.
    }

    [Fact]
    public void Load_NewerSchemaFile_RefusesAndLeavesFileByteForByteIntact()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        var originalText = File.ReadAllText(FixturePath("newer-schema.json"));
        File.WriteAllText(MainPath(dir, "device-a"), originalText);

        var result = store.Load("device-a");

        Assert.Equal(LayoutLoadOutcome.TooNewSchema, result.Outcome);
        Assert.Null(result.Layout);
        Assert.True(File.Exists(MainPath(dir, "device-a")));
        Assert.Equal(originalText, File.ReadAllText(MainPath(dir, "device-a")));
    }

    [Fact]
    public void Load_LegacySchemaFile_MigratesInMemory_ButDoesNotRewriteDiskUntilTheNextSave()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        var legacyText = File.ReadAllText(FixturePath("legacy-v0.json"));
        File.WriteAllText(MainPath(dir, "device-a"), legacyText);

        var result = store.Load("device-a");

        Assert.Equal(LayoutLoadOutcome.Loaded, result.Outcome);
        // [2026-09-16] SUPERSEDED by the folders schema bump - before-value:
        // the literal 1, which was both "the current schema" and "what a
        // migrated v0 file comes up to" while those were the same number.
        // They are not the same claim, and this test is making the second
        // one, so it now reads the constant rather than restating a number
        // that will drift again at v3.
        Assert.Equal(LayoutMigrator.CurrentSchemaVersion, result.Layout!.SchemaVersion);
        Assert.Equal("SHIP", result.Layout.Pages[0].Name);

        // Not rewritten on disk by Load alone.
        Assert.Equal(legacyText, File.ReadAllText(MainPath(dir, "device-a")));
    }

    [Fact]
    public void RestorePrevious_NoBakFile_ReturnsFalse_AndChangesNothing()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new LayoutStore(dir, log);
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);

        var restored = store.RestorePrevious("device-a");

        Assert.False(restored);
        Assert.Contains("LandingGearToggle", File.ReadAllText(MainPath(dir, "device-a")));
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn);
    }

    [Fact]
    public void RestorePrevious_SwapsMainAndBak()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions); // gen 1
        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);  // gen 2, bak = gen 1

        var restored = store.RestorePrevious("device-a");

        Assert.True(restored);
        Assert.Contains("LandingGearToggle", File.ReadAllText(MainPath(dir, "device-a")));
        Assert.Contains("ToggleCargoScoop", File.ReadAllText(BakPath(dir, "device-a")));
    }

    // -----------------------------------------------------------------
    // LayoutStore.Saved (2026-09-10) - the one signal that says a device's layout
    // on disk is not what a long-lived reader last read. GET /api/panel/live
    // is the subscriber; see ref/docs/lit-state.md's "The glow that never
    // arrived".
    // -----------------------------------------------------------------

    [Fact]
    public void Saved_RaisedWithTheDeviceId_AfterASuccessfulSave()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        var raisedFor = new List<string>();
        store.Saved += id => raisedFor.Add(id);

        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);

        Assert.Equal(new[] { "device-a" }, raisedFor);
    }

    /// <summary>
    /// The discriminating half: a refused save changed nothing on disk, so a
    /// subscriber re-reading in response to it would re-read the same bytes
    /// and, worse, would learn to distrust the signal.
    /// </summary>
    [Fact]
    public void Saved_NotRaised_WhenTheSaveWasRefused()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        var raisedFor = new List<string>();
        store.Saved += id => raisedFor.Add(id);
        var invalidLayout = new Layout(1, new[]
        {
            new LayoutPage("SHIP", "not-a-real-template", Array.Empty<LayoutSlot>(), Array.Empty<LayoutSlot>())
        });

        Assert.Equal(LayoutSaveOutcome.ValidationFailed, store.Save("device-a", invalidLayout, KnownActions).Outcome);

        Assert.Empty(raisedFor);
    }

    /// <summary>
    /// Undoing an import rewrites the layout exactly as a save does
    /// (<c>ref/docs/layout-import.md</c>), so a reader that only heard about
    /// saves would show the imported arrangement after it had been undone.
    /// </summary>
    [Fact]
    public void Saved_RaisedByARestore_ButNotByOneThatHadNothingToRestore()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);

        var raisedFor = new List<string>();
        store.Saved += id => raisedFor.Add(id);

        Assert.True(store.RestorePrevious("device-a"));
        Assert.Equal(new[] { "device-a" }, raisedFor);

        Assert.False(store.RestorePrevious("device-b"));
        Assert.Equal(new[] { "device-a" }, raisedFor);
    }

    [Fact]
    public void RestorePrevious_CalledTwice_TogglesBackToTheOriginal()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);

        store.RestorePrevious("device-a");
        var secondRestore = store.RestorePrevious("device-a");

        Assert.True(secondRestore);
        Assert.Contains("ToggleCargoScoop", File.ReadAllText(MainPath(dir, "device-a")));
        Assert.Contains("LandingGearToggle", File.ReadAllText(BakPath(dir, "device-a")));
    }

    // -----------------------------------------------------------------
    // ListDeviceIds - what backs the import list (ref/docs/layout-import.md)
    // -----------------------------------------------------------------

    [Fact]
    public void ListDeviceIds_EmptyDirectory_IsEmpty()
    {
        var store = new LayoutStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.Empty(store.ListDeviceIds());
    }

    [Fact]
    public void ListDeviceIds_ReturnsEveryDeviceWithALayout()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        store.Save("device-b", LayoutNaming("ToggleCargoScoop"), KnownActions);

        Assert.Equal(
            new[] { "device-a", "device-b" },
            store.ListDeviceIds().OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// This store writes .bak beside every main file the moment a layout is
    /// saved twice. A list that counted those would offer a device's own
    /// previous generation as if it were a separate device to import from -
    /// so the .bak has to be invisible here, and the only way to know is to
    /// create one and look.
    /// </summary>
    [Fact]
    public void ListDeviceIds_IgnoresTheBackupGenerationItWritesItself()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);
        Assert.True(File.Exists(BakPath(dir, "device-a")));

        Assert.Equal("device-a", Assert.Single(store.ListDeviceIds()));
    }

    /// <summary>
    /// Same again for the corrupt-rename-aside generation, and for the
    /// sibling stores that share this directory by design
    /// (panel-settings-*, theme-override-*, orphan-*).
    /// </summary>
    [Fact]
    public void ListDeviceIds_IgnoresCorruptAsidesAndTheSiblingStoresFiles()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "layout-device-a.json.corrupt-20260908120000000"), "{}");
        File.WriteAllText(Path.Combine(dir, "panel-settings-device-a.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "theme-override-device-a.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "orphan-device-a.json"), "{}");
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-b", LayoutNaming("LandingGearToggle"), KnownActions);

        Assert.Equal("device-b", Assert.Single(store.ListDeviceIds()));
    }

    // -----------------------------------------------------------------
    // Delete - the discard half of the import/recovery chooser
    // (ref/docs/layout-import.md)
    // -----------------------------------------------------------------

    [Fact]
    public void Delete_RemovesTheMainFile_AndReturnsTrue()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);

        var result = store.Delete("device-a");

        Assert.True(result);
        Assert.False(File.Exists(MainPath(dir, "device-a")));
    }

    [Fact]
    public void Delete_AlsoRemovesTheBakGenerationWhenPresent()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        store.Save("device-a", LayoutNaming("ToggleCargoScoop"), KnownActions);
        Assert.True(File.Exists(BakPath(dir, "device-a")));

        store.Delete("device-a");

        Assert.False(File.Exists(MainPath(dir, "device-a")));
        Assert.False(File.Exists(BakPath(dir, "device-a")));
    }

    [Fact]
    public void Delete_NoFileForDevice_ReturnsFalse_AndDoesNotThrow()
    {
        var store = new LayoutStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.False(store.Delete("never-existed"));
    }

    [Fact]
    public void Delete_OneDevice_NeverTouchesAnothers()
    {
        var dir = NewTempDir();
        var store = new LayoutStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", LayoutNaming("LandingGearToggle"), KnownActions);
        store.Save("device-b", LayoutNaming("ToggleCargoScoop"), KnownActions);

        store.Delete("device-a");

        Assert.False(File.Exists(MainPath(dir, "device-a")));
        Assert.True(File.Exists(MainPath(dir, "device-b")));
        Assert.Contains("ToggleCargoScoop", File.ReadAllText(MainPath(dir, "device-b")));
    }
}
