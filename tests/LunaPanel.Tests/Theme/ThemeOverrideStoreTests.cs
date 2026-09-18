using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Theme;

namespace LunaPanel.Tests.Theme;

/// <summary>
/// Drives <see cref="ThemeOverrideStore"/> against real temp directories
/// under the test output (never the repo tree or the user's real profile) -
/// same discipline, and deliberately mirroring the same conventions, as
/// <c>LunaPanel.Tests.Layouts.LayoutStoreTests</c>: absent-file-is-first-run
/// (here: "resolve automatically"), atomic save with exactly one kept
/// previous generation, corrupt-file rename-aside (never overwritten in
/// place), and <see cref="ThemeOverrideStore.Clear"/> (this store's
/// equivalent of "reset to automatic").
/// </summary>
public class ThemeOverrideStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "theme-override-store", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MainPath(string dir, string deviceId) => Path.Combine(dir, $"theme-override-{deviceId}.json");
    private static string BakPath(string dir, string deviceId) => MainPath(dir, deviceId) + ".bak";

    private static ThemeOverride SampleOverride(byte seed) =>
        new(new HudColor(seed, 0, 0), new HudColor(0, seed, 0), new HudColor(0, 0, seed));

    [Fact]
    public void Load_NoFileForDevice_ReturnsNotFound()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());

        var result = store.Load("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Override);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        var overrideValue = SampleOverride(10);

        store.Save("device-a", overrideValue);
        var result = store.Load("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(overrideValue, result.Override);
    }

    [Fact]
    public void Save_TwiceInARow_KeepsExactlyThePreviousGenerationAsBak()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());

        store.Save("device-a", SampleOverride(10));
        Assert.False(File.Exists(BakPath(dir, "device-a")), "No .bak should exist after the first save.");

        store.Save("device-a", SampleOverride(20));

        Assert.Contains("14", File.ReadAllText(MainPath(dir, "device-a")), StringComparison.OrdinalIgnoreCase); // 20 = 0x14
        Assert.Contains("0a", File.ReadAllText(BakPath(dir, "device-a")), StringComparison.OrdinalIgnoreCase); // 10 = 0x0a
    }

    [Fact]
    public void Save_NeverLeavesATempFileBehind()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());

        store.Save("device-a", SampleOverride(10));

        Assert.False(File.Exists(MainPath(dir, "device-a") + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_RenamesAside_ReturnsCorrupt_AndOriginalBytesAreStillReadable()
    {
        var dir = NewTempDir();
        var log = new CapturingDiagnosticLog();
        var store = new ThemeOverrideStore(dir, log);
        const string corruptText = "{ not valid json";
        File.WriteAllText(MainPath(dir, "device-a"), corruptText);

        var result = store.Load("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.Corrupt, result.Outcome);
        Assert.False(File.Exists(MainPath(dir, "device-a")), "The corrupt file must not be left at the main path.");

        var renamed = Directory.GetFiles(dir, "theme-override-device-a.json.corrupt-*");
        var corruptFile = Assert.Single(renamed);
        Assert.Equal(corruptText, File.ReadAllText(corruptFile));

        var errorEvent = Assert.Single(log.Events, e => e.Level == DiagnosticLevel.Error);
        Assert.Equal("Theme", errorEvent.Category);
        Assert.DoesNotContain(dir, errorEvent.Message);
    }

    [Fact]
    public void Load_MissingColourField_IsTreatedAsCorrupt()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        File.WriteAllText(MainPath(dir, "device-a"), """{ "border": "#ff8000", "text": "#ff8000" }"""); // lit missing

        var result = store.Load("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.Corrupt, result.Outcome);
    }

    [Fact]
    public void Clear_NoOverrideStored_ReturnsFalse_AndChangesNothing()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());

        var cleared = store.Clear("device-a");

        Assert.False(cleared);
    }

    [Fact]
    public void Clear_OverrideStored_RemovesMainFile_SoLoadReturnsNotFound()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", SampleOverride(10));

        var cleared = store.Clear("device-a");
        var result = store.Load("device-a");

        Assert.True(cleared);
        Assert.Equal(ThemeOverrideLoadOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void Clear_KeepsTheClearedOverrideAsBak_RatherThanDestroyingItOutright()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", SampleOverride(10));

        store.Clear("device-a");

        Assert.True(File.Exists(BakPath(dir, "device-a")));
        Assert.Contains("0a", File.ReadAllText(BakPath(dir, "device-a")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clear_DoesNotAffectADifferentDevicesOverride()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", SampleOverride(10));
        store.Save("device-b", SampleOverride(20));

        store.Clear("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.Loaded, store.Load("device-b").Outcome);
    }

    // -------------------------------------------------------------------
    // Background, the fourth commander-chosen role (2026-09-10,
    // ref/docs/theme.md) - stored by the same store, in the same
    // per-device file, cleared by the same Clear.
    // -------------------------------------------------------------------

    [Fact]
    public void Save_ThenLoad_RoundTripsAChosenBackground()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        var overrideValue = SampleOverride(10) with { Background = new HudColor(0x33, 0x78, 0xFF) };

        store.Save("device-a", overrideValue);
        var result = store.Load("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.Loaded, result.Outcome);
        Assert.Equal(new HudColor(0x33, 0x78, 0xFF), result.Override!.Background);
        Assert.Equal(overrideValue, result.Override);
    }

    [Fact]
    public void Load_AFileWrittenBeforeBackgroundExisted_StillLoads_WithNoBackgroundChosen()
    {
        // The literal three-colour file shape sitting on a commander's
        // machine right now. Written by hand rather than by Serialize, so
        // this cannot pass by accident if Serialize later starts emitting
        // a background key.
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        File.WriteAllText(
            MainPath(dir, "device-a"),
            """{ "border": "#ff8000", "text": "#00b8ff", "lit": "#ffffff" }""");

        var result = store.Load("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.Loaded, result.Outcome);
        Assert.Null(result.Override!.Background);
    }

    [Fact]
    public void Clear_RemovesAChosenBackgroundAlongWithTheRestOfTheOverride()
    {
        var dir = NewTempDir();
        var store = new ThemeOverrideStore(dir, new CapturingDiagnosticLog());
        store.Save("device-a", SampleOverride(10) with { Background = new HudColor(0x33, 0x78, 0xFF) });

        store.Clear("device-a");

        Assert.Equal(ThemeOverrideLoadOutcome.NotFound, store.Load("device-a").Outcome);
    }
}
