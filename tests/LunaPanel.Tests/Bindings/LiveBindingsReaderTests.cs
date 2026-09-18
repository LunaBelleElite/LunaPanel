using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Bindings;
using LunaPanel.Server.Discovery;

namespace LunaPanel.Tests.Bindings;

/// <summary>
/// Drives <see cref="LiveBindingsReader"/> against real temp files (never
/// the repo tree or the user's real profile). Pins the "never throws, never
/// returns null" degrade-to-empty discipline: an absent, unreadable, or
/// unparseable bindings file all fall back to an element-less
/// <see cref="LunaPanel.Core.Bindings.BindingsFile"/>, not an exception.
/// </summary>
public class LiveBindingsReaderTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "live-bindings-reader", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Builds a <see cref="DiscoveryResultHolder"/> whose current
    /// <see cref="PathDiscoveryResult.BindingsSelection"/> selects
    /// <paramref name="path"/> directly (as though the rule had already
    /// chosen it) - <see cref="PresetSelector"/>'s own rule steps are pinned
    /// separately in <c>PresetSelectorTests</c>; this file only needs a
    /// selection result to exist, not to re-derive one.
    /// </summary>
    private static DiscoveryResultHolder HolderWithBindsPath(string? path)
    {
        var selection = path is not null
            ? new PresetSelectionResult(path, "Custom", PresetSelectionMethod.CommanderAuthored, PresetOrigin.CommanderAuthored, new BindsVersion(4, 2), false, "Custom")
            : new PresetSelectionResult(null, null, PresetSelectionMethod.Fallback, null, null, false, null);

        var discovery = new PathDiscoveryResult(
            EliteInstallations: Array.Empty<EliteInstallation>(),
            Bindings: new BindingsDiscoveryResult(path, path is not null ? new BindsVersion(4, 2) : null, Array.Empty<string>()),
            BindingsSelection: selection,
            Edhm: new EdhmDiscoveryResult(false, null, null, Array.Empty<EdhmEditionData>()),
            LunaPanelDirectories: new LunaPanelDirectoryLayout(@"C:\fake\Logs", @"C:\fake\Layouts", @"C:\fake\Pairing\device-registry.json"),
            StatusJson: new StatusJsonDiscoveryResult(null));

        return new DiscoveryResultHolder(discovery);
    }

    private const string ValidXml = """
        <Root PresetName="Custom" MajorVersion="4" MinorVersion="2">
            <LandingGearToggle>
                <Primary Device="Keyboard" Key="Key_G" />
                <Secondary Device="{NoDevice}" Key="" />
            </LandingGearToggle>
        </Root>
        """;

    [Fact]
    public void Read_NoBindsPathDiscovered_ReturnsEmptyBindingsFile_NeverThrows()
    {
        var log = new CapturingDiagnosticLog();
        var reader = new LiveBindingsReader(HolderWithBindsPath(null), log);

        var result = reader.Read();

        Assert.Empty(result.Elements);
        Assert.Null(result.PresetName);
    }

    [Fact]
    public void Read_RealFile_ParsesFresh_EveryCall()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "Custom.4.2.binds");
        File.WriteAllText(path, ValidXml);
        var log = new CapturingDiagnosticLog();
        var reader = new LiveBindingsReader(HolderWithBindsPath(path), log);

        var first = reader.Read();
        Assert.Single(first.Elements);
        Assert.Equal("LandingGearToggle", first.Elements[0].Name);

        // A rebind made while LunaPanel is running (same path, new content)
        // is picked up on the very next read - no caching, no restart.
        File.WriteAllText(path, ValidXml.Replace("LandingGearToggle", "ToggleCargoScoop"));
        var second = reader.Read();
        Assert.Equal("ToggleCargoScoop", second.Elements[0].Name);
    }

    [Fact]
    public void Read_PathDiscoveredButFileMissing_ReturnsEmptyBindingsFile_AndLogsAWarning()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "does-not-exist.binds");
        var log = new CapturingDiagnosticLog();
        var reader = new LiveBindingsReader(HolderWithBindsPath(path), log);

        var result = reader.Read();

        Assert.Empty(result.Elements);
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn);
    }

    [Fact]
    public void Read_UnparseableFile_ReturnsEmptyBindingsFile_AndLogsAWarning()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, "Custom.4.2.binds");
        File.WriteAllText(path, "not xml at all");
        var log = new CapturingDiagnosticLog();
        var reader = new LiveBindingsReader(HolderWithBindsPath(path), log);

        var result = reader.Read();

        Assert.Empty(result.Elements);
        Assert.Contains(log.Events, e => e.Level == DiagnosticLevel.Warn);
    }
}
