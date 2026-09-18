using LunaPanel.Core.Diagnostics;
using LunaPanel.Core.Layouts;

namespace LunaPanel.Tests.Layouts;

/// <summary>
/// Drives <see cref="OrphanMarkerStore"/> against real temp directories under
/// the test output - never the repo tree, never the user's real profile -
/// the same way <c>LayoutStoreTests</c> does.
/// </summary>
public class OrphanMarkerStoreTests
{
    private sealed class CapturingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "orphan-markers", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static readonly DateTimeOffset LastSeen = new(2026, 9, 7, 20, 14, 0, TimeSpan.Zero);

    [Fact]
    public void List_NothingWrittenYet_IsEmpty()
    {
        var store = new OrphanMarkerStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.Empty(store.List());
    }

    [Fact]
    public void Write_ThenList_RoundTripsEveryField()
    {
        var store = new OrphanMarkerStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.True(store.Write(new OrphanMarker("AABBCCDD00112233", "Phone 2", "phone", LastSeen)));

        var marker = Assert.Single(store.List());
        Assert.Equal("AABBCCDD00112233", marker.DeviceId);
        Assert.Equal("Phone 2", marker.Name);
        Assert.Equal("phone", marker.DeviceClass);
        Assert.Equal(LastSeen, marker.LastSeenAt);
    }

    /// <summary>
    /// The file name is what ties a marker to the layout it describes, so it
    /// is pinned as a literal rather than read back through this store's own
    /// <see cref="OrphanMarkerStore.List"/> (which would agree with any
    /// naming scheme, including one that collided with
    /// <c>layout-&lt;deviceId&gt;.json</c> itself).
    /// </summary>
    [Fact]
    public void Write_UsesTheOrphanPrefixedPerDeviceFileName()
    {
        var dir = NewTempDir();
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());

        store.Write(new OrphanMarker("AABBCCDD00112233", "Phone", "phone", LastSeen));

        Assert.True(File.Exists(Path.Combine(dir, "orphan-AABBCCDD00112233.json")));
    }

    [Fact]
    public void Write_SameDeviceTwice_KeepsOneMarker_WithTheLaterContent()
    {
        var store = new OrphanMarkerStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Write(new OrphanMarker("AABB", "Phone", "phone", LastSeen));
        store.Write(new OrphanMarker("AABB", "Phone renamed", "phone", LastSeen.AddDays(1)));

        var marker = Assert.Single(store.List());
        Assert.Equal("Phone renamed", marker.Name);
        Assert.Equal(LastSeen.AddDays(1), marker.LastSeenAt);
    }

    [Fact]
    public void List_ReturnsEveryMarkerWritten()
    {
        var store = new OrphanMarkerStore(NewTempDir(), new CapturingDiagnosticLog());

        store.Write(new OrphanMarker("AAAA", "Phone", "phone", LastSeen));
        store.Write(new OrphanMarker("BBBB", "Tablet", "tablet", LastSeen));

        Assert.Equal(new[] { "AAAA", "BBBB" }, store.List().Select(m => m.DeviceId).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// The marker store shares a directory with <see cref="LayoutStore"/>,
    /// <c>PanelSettingsStore</c> and <c>ThemeOverrideStore</c>, so it has to
    /// ignore their files rather than trying to parse them as markers.
    /// </summary>
    [Fact]
    public void List_IgnoresTheOtherPerDeviceFilesSharingTheDirectory()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "layout-AAAA.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "panel-settings-AAAA.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "theme-override-AAAA.json"), "{}");
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());

        store.Write(new OrphanMarker("BBBB", "Phone", "phone", LastSeen));

        Assert.Equal("BBBB", Assert.Single(store.List()).DeviceId);
    }

    /// <summary>
    /// A marker file that will not parse must not take the whole import list
    /// down with it - the other markers still describe layouts a commander
    /// may want back.
    /// </summary>
    [Fact]
    public void List_UnparseableMarker_IsSkipped_AndTheRestStillList()
    {
        var dir = NewTempDir();
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());
        store.Write(new OrphanMarker("GOOD", "Phone", "phone", LastSeen));
        File.WriteAllText(Path.Combine(dir, "orphan-BAD.json"), "{ this is not json");

        Assert.Equal("GOOD", Assert.Single(store.List()).DeviceId);
    }

    /// <summary>
    /// A file that parses as JSON but carries none of the marker's fields is
    /// skipped rather than surfacing as a nameless entry on the import list.
    /// </summary>
    [Fact]
    public void List_MarkerWithNoDeviceId_IsSkipped()
    {
        var dir = NewTempDir();
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());
        File.WriteAllText(Path.Combine(dir, "orphan-EMPTY.json"), "{}");

        Assert.Empty(store.List());
    }

    /// <summary>
    /// Half of "never the token" (<c>ref/docs/layout-import.md</c>). The
    /// needle is the word itself, scoped to a marker file, so that adding any
    /// token-bearing field to <see cref="OrphanMarker"/> - the one edit that
    /// could reintroduce this - makes it appear and turns this red.
    ///
    /// It is deliberately not "assert this file does not contain
    /// &lt;some 64 hex characters&gt;": no token is in play here at all, so
    /// such a needle could never appear whatever the code did, and would pass
    /// for free. The real end-to-end version, driving an actual minted token
    /// through a real forget, is
    /// <c>DevicesEndpointTests.Forget_WritesAnOrphanMarker_AndNeverTheToken</c>.
    /// </summary>
    [Fact]
    public void Write_MarkerFileNamesNoTokenBearingField()
    {
        var dir = NewTempDir();
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());

        store.Write(new OrphanMarker("AABBCCDD00112233", "Phone", "phone", LastSeen));

        var contents = File.ReadAllText(Path.Combine(dir, "orphan-AABBCCDD00112233.json"));
        Assert.DoesNotContain("token", contents, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------
    // Delete - the discard half of the import/recovery chooser
    // (ref/docs/layout-import.md)
    // -----------------------------------------------------------------

    [Fact]
    public void Delete_RemovesTheMarkerFile_AndReturnsTrue()
    {
        var dir = NewTempDir();
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());
        store.Write(new OrphanMarker("AABB", "Phone", "phone", LastSeen));

        var result = store.Delete("AABB");

        Assert.True(result);
        Assert.False(File.Exists(Path.Combine(dir, "orphan-AABB.json")));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Delete_NoMarkerForDevice_ReturnsFalse_AndIsNotAFailure()
    {
        var store = new OrphanMarkerStore(NewTempDir(), new CapturingDiagnosticLog());

        Assert.False(store.Delete("never-existed"));
    }

    [Fact]
    public void Delete_OneDevice_NeverTouchesAnothersMarker()
    {
        var dir = NewTempDir();
        var store = new OrphanMarkerStore(dir, new CapturingDiagnosticLog());
        store.Write(new OrphanMarker("AAAA", "Phone", "phone", LastSeen));
        store.Write(new OrphanMarker("BBBB", "Tablet", "tablet", LastSeen));

        store.Delete("AAAA");

        Assert.Equal("BBBB", Assert.Single(store.List()).DeviceId);
    }
}
