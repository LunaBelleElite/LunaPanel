using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Tests.Diagnostics;

public class DiagnosticRingBufferTests
{
    private static DiagnosticEvent Event(int n) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(n), DiagnosticLevel.Info, "Test", $"event-{n}");

    [Fact]
    public void Snapshot_FullRing_HoldsExactlyCapacityItems()
    {
        var ring = new DiagnosticRingBuffer(3);
        ring.Write(Event(1));
        ring.Write(Event(2));
        ring.Write(Event(3));

        var snapshot = ring.Snapshot();

        Assert.Equal(3, snapshot.Count);
    }

    [Fact]
    public void Snapshot_ReturnsEventsOldestFirst()
    {
        var ring = new DiagnosticRingBuffer(3);
        ring.Write(Event(1));
        ring.Write(Event(2));
        ring.Write(Event(3));

        var snapshot = ring.Snapshot();

        Assert.Equal(new[] { "event-1", "event-2", "event-3" }, snapshot.Select(e => e.Message));
    }

    [Fact]
    public void Write_BeyondCapacity_EvictsOldest()
    {
        var ring = new DiagnosticRingBuffer(3);
        ring.Write(Event(1));
        ring.Write(Event(2));
        ring.Write(Event(3));
        ring.Write(Event(4));

        var snapshot = ring.Snapshot();

        Assert.Equal(new[] { "event-2", "event-3", "event-4" }, snapshot.Select(e => e.Message));
    }

    [Fact]
    public void Write_ManyEvictionsInARow_StillTracksOldestFirstCorrectly()
    {
        var ring = new DiagnosticRingBuffer(2);
        for (var i = 1; i <= 10; i++)
        {
            ring.Write(Event(i));
        }

        var snapshot = ring.Snapshot();

        Assert.Equal(new[] { "event-9", "event-10" }, snapshot.Select(e => e.Message));
    }

    [Fact]
    public void Snapshot_BelowCapacity_HoldsOnlyWhatWasWritten()
    {
        var ring = new DiagnosticRingBuffer(5);
        ring.Write(Event(1));

        var snapshot = ring.Snapshot();

        Assert.Single(snapshot);
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticRingBuffer(0));
    }
}
