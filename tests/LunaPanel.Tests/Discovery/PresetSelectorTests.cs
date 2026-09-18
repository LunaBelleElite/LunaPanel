using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Discovery;

/// <summary>
/// Drives <see cref="PresetSelector.Select"/> - <c>ref/docs/bindings-source.md</c>'s
/// "The rule" - against synthetic temp-directory trees, never a real
/// install. Every fixture here is a hand-written minimal <c>.binds</c>/
/// <c>.start</c> file constructed inline, matching <c>BindingsDiscoveryTests</c>'
/// own convention - nothing under <c>Fixtures/</c> is touched, since a
/// stock-scheme <c>.binds</c> file is Frontier's own content and this task's
/// brief forbids copying one into a fixture.
/// </summary>
public class PresetSelectorTests
{
    private const string CommanderXml = """<Root PresetName="{0}" MajorVersion="{1}" MinorVersion="{2}"></Root>""";
    private const string StockXml = """<Root PresetName="{0}" SortOrder="0"></Root>""";

    private static string CommanderContent(string presetName, int major, int minor) =>
        string.Format(CommanderXml, presetName, major, minor);

    private static string StockContent(string presetName) => string.Format(StockXml, presetName);

    // -----------------------------------------------------------------
    // Defect 1: the commander who has never rebound anything - no file at
    // all in Options\Bindings, only the shipped stock scheme.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_NoCommanderFileAtAll_ButStartPresetNamesAStockScheme_ResolvesToTheStockFile_NotDead()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/StartPreset.4.start", "KeyboardMouseOnly\n");
        var controlSchemesDir = temp.CreateSubdirectory("ControlSchemes");
        temp.CreateFile("ControlSchemes/KeyboardMouseOnly.binds", StockContent("KeyboardMouseOnly"));

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        // The old rule finds nothing at all here - this IS the dead-panel bug.
        Assert.Null(bindingsDiscovery.LatestBindsFilePath);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDir, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("ControlSchemes", "KeyboardMouseOnly.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Stock, result.Method);
        Assert.Equal(PresetOrigin.Stock, result.Origin);
        Assert.Null(result.Version);
        Assert.False(result.PresetNameMismatch);
        Assert.Equal("KeyboardMouseOnly", result.ActualPresetNameInFile);
    }

    [Fact]
    public void Select_NoCommanderFile_NoStockFileEither_NoStartPreset_ReportsNothingFound_NotAnException()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Null(result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Fallback, result.Method);
        Assert.Null(result.Origin);
    }

    // -----------------------------------------------------------------
    // Defect 2: two same-version commander presets, which today tie on
    // major.minor (Elite's own schema version, shared machine-wide) and are
    // broken arbitrarily by OS enumeration order.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_TwoCommanderPresetsAtTheSameVersion_StartPresetOrderDecides_NotEnumerationOrder()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/MyHOTAS.4.2.binds", CommanderContent("MyHOTAS", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "MyHOTAS\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "MyHOTAS.4.2.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.CommanderAuthored, result.Method);
    }

    [Fact]
    public void Select_TwoCommanderPresetsAtTheSameVersion_SwappingStartPresetOrder_SwapsTheWinner()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/MyHOTAS.4.2.binds", CommanderContent("MyHOTAS", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
    }

    // -----------------------------------------------------------------
    // Defect 3: StartPreset names a preset that is NOT the highest-versioned
    // file in the folder - today's "just take the highest version" rule
    // would pick the wrong preset entirely, not merely the wrong version of
    // the right one.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_StartPresetNamesAPresetThatIsNotTheHighestVersionedFile_StillWinsOverTheHigherVersionedOther()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        // A leftover/abandoned preset that happens to carry a HIGHER version
        // number than the one actually live - today's rule would pick this.
        temp.CreateFile("Bindings/OldPreset.9.9.binds", CommanderContent("OldPreset", 9, 9));
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        // Confirms the premise: the OLD rule really does pick the wrong file here.
        Assert.Equal(temp.Combine("Bindings", "OldPreset.9.9.binds"), bindingsDiscovery.LatestBindsFilePath);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.CommanderAuthored, result.Method);
        Assert.Equal(new BindsVersion(4, 2), result.Version);
    }

    // -----------------------------------------------------------------
    // Step 2: commander file preferred over stock even when both exist for
    // the same name; highest version taken among a name's OWN files.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_NameHasBothACommanderFileAndAStockFile_PrefersTheCommanderFile()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/KeyboardMouseOnly.4.2.binds", CommanderContent("KeyboardMouseOnly", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "KeyboardMouseOnly\n");
        var controlSchemesDir = temp.CreateSubdirectory("ControlSchemes");
        temp.CreateFile("ControlSchemes/KeyboardMouseOnly.binds", StockContent("KeyboardMouseOnly"));

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDir, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "KeyboardMouseOnly.4.2.binds"), result.SelectedFilePath);
        Assert.Equal(PresetOrigin.CommanderAuthored, result.Origin);
    }

    [Fact]
    public void Select_NameHasTwoCommanderVersions_TakesTheHighestOfThatNamesOwnFiles()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/Custom.4.9.binds", CommanderContent("Custom", 4, 9));
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.9.binds"), result.SelectedFilePath);
        Assert.Equal(new BindsVersion(4, 9), result.Version);
    }

    // -----------------------------------------------------------------
    // Step 1: highest-numbered StartPreset file, distinct lines, first
    // appearance preserved.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_TwoStartPresetFiles_UsesTheHighestNumberedOne()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/Newer.9.0.binds", CommanderContent("Newer", 9, 0));
        // The lower-numbered file names the WRONG preset - if this were read
        // instead of StartPreset.9, the test would resolve "Custom".
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\n");
        temp.CreateFile("Bindings/StartPreset.9.start", "Newer\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Newer.9.0.binds"), result.SelectedFilePath);
    }

    [Fact]
    public void Select_StartPresetHasDuplicateLines_DedupedButFirstAppearanceOrderPreserved()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/KeyboardMouseOnly.4.4.binds", CommanderContent("KeyboardMouseOnly", 4, 4));
        temp.CreateFile("Bindings/Custom.4.4.binds", CommanderContent("Custom", 4, 4));
        // Matches the real reference-machine shape: KeyboardMouseOnly,
        // Custom, Custom, KeyboardMouseOnly - four lines, two distinct names.
        temp.CreateFile("Bindings/StartPreset.4.start", "KeyboardMouseOnly\nCustom\nCustom\nKeyboardMouseOnly\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        // Both names resolve to a commander file, so step 3's "first name
        // that resolved commander" picks the FIRST-APPEARING one -
        // KeyboardMouseOnly, not Custom - proving the dedup preserved order
        // rather than, say, sorting or taking the last-seen line.
        Assert.Equal(temp.Combine("Bindings", "KeyboardMouseOnly.4.4.binds"), result.SelectedFilePath);
        Assert.Equal("KeyboardMouseOnly", result.SelectedPresetName);
    }

    [Fact]
    public void Select_StartPresetBlankLines_AreIgnored()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "\n\nCustom\n\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
    }

    // -----------------------------------------------------------------
    // Step 3: stock preferred over nothing when NO name resolved to a
    // commander file, but a later name in the list resolves to stock.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_NoNameResolvesToCommander_FirstNameResolvingToStockWins()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        // "Xbox360Controller" resolves to nothing at all (no commander file,
        // no stock file for it either) - "KeyboardMouseOnly" is the first
        // name that resolves to anything, and it's stock.
        temp.CreateFile("Bindings/StartPreset.4.start", "Xbox360Controller\nKeyboardMouseOnly\n");
        var controlSchemesDir = temp.CreateSubdirectory("ControlSchemes");
        temp.CreateFile("ControlSchemes/KeyboardMouseOnly.binds", StockContent("KeyboardMouseOnly"));

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDir, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("ControlSchemes", "KeyboardMouseOnly.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Stock, result.Method);
    }

    /// <summary>
    /// The dedicated witness for step 3's ORIGIN preference, independent of
    /// list position: a name resolving to a stock file appears FIRST in
    /// <c>StartPreset</c>, and a name resolving to a commander file appears
    /// SECOND - commander must still win, because step 3 prefers "the first
    /// name that resolved to commander" over "the first name that resolved
    /// to anything at all". None of this file's other tests can tell this
    /// apart from "commander wins because it's a stronger candidate somehow"
    /// without also mixing origins across positions like this.
    /// </summary>
    [Fact]
    public void Select_StockNameAppearsFirst_ButALaterNameResolvesToCommander_CommanderStillWins()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "KeyboardMouseOnly\nCustom\n");
        var controlSchemesDir = temp.CreateSubdirectory("ControlSchemes");
        temp.CreateFile("ControlSchemes/KeyboardMouseOnly.binds", StockContent("KeyboardMouseOnly"));

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDir, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.CommanderAuthored, result.Method);
        Assert.Equal("Custom", result.SelectedPresetName);
    }

    // -----------------------------------------------------------------
    // Step 4: fallback, both the "found something" and "found nothing at
    // all" cases, each flagged rather than silent.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_NoStartPresetFileAtAll_FallsBackToHighestVersionInOptionsFolder()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Fallback, result.Method);
        Assert.Equal(PresetOrigin.CommanderAuthored, result.Origin);
        Assert.Null(result.SelectedPresetName);
    }

    [Fact]
    public void Select_StartPresetNamesOnlyUnresolvableNames_FallsBackToHighestVersion()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        // Names a preset that resolves to nothing at all (no commander file
        // for it, no ControlSchemes directory supplied to check stock).
        temp.CreateFile("Bindings/StartPreset.4.start", "SomeUnknownPreset\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
        Assert.Equal(PresetSelectionMethod.Fallback, result.Method);
    }

    // -----------------------------------------------------------------
    // Step 5: verification - a mismatch is reported, not swallowed, and
    // does not stop the file from being used.
    // -----------------------------------------------------------------

    [Fact]
    public void Select_ChosenFilesOwnPresetNameDisagreesWithTheSelectingName_ReportsMismatch_ButStillUsesTheFile()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        // Filename says "Custom" (which is what StartPreset names and what
        // ResolveCandidate matches on), but the file's own <Root PresetName>
        // disagrees - the exact case step 5 exists to catch.
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("SomethingElse", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.Equal(temp.Combine("Bindings", "Custom.4.2.binds"), result.SelectedFilePath);
        Assert.True(result.PresetNameMismatch);
        Assert.Equal("SomethingElse", result.ActualPresetNameInFile);
        Assert.Equal("Custom", result.SelectedPresetName);
        Assert.Contains(
            log.Snapshot(),
            e => e.Level == DiagnosticLevel.Warn && e.Message.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void Select_ChosenFilesOwnPresetNameAgrees_NoMismatchReported()
    {
        using var temp = TempDirectory.Create();
        var bindingsDir = temp.CreateSubdirectory("Bindings");
        temp.CreateFile("Bindings/Custom.4.2.binds", CommanderContent("Custom", 4, 2));
        temp.CreateFile("Bindings/StartPreset.4.start", "Custom\n");

        var log = new DiagnosticRingBuffer(200);
        var bindingsDiscovery = BindingsDiscovery.Discover(bindingsDir, log);

        var result = PresetSelector.Select(bindingsDir, controlSchemesDirectory: null, bindingsDiscovery, log);

        Assert.False(result.PresetNameMismatch);
        Assert.Equal("Custom", result.ActualPresetNameInFile);
    }
}
