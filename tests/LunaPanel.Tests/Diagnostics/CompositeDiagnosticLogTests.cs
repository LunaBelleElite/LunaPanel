using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Tests.Diagnostics;

public class CompositeDiagnosticLogTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "diagnostics", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Write_FansOutToBothWriterFileAndRingBuffer()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) };
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, new PathRedactor(Array.Empty<(string, string)>()));
        var ring = new DiagnosticRingBuffer(10);
        var composite = new CompositeDiagnosticLog(writer, ring);

        var evt = new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Warn, "Discovery", "found EDHM-UI install");
        composite.Write(evt);

        var fileLine = File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log")).Single();
        Assert.Contains("found EDHM-UI install", fileLine);

        var snapshot = ring.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("found EDHM-UI install", snapshot[0].Message);
    }
}
