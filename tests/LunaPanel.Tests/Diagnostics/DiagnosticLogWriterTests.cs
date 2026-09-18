using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Tests.Diagnostics;

/// <summary>
/// Drives <see cref="DiagnosticLogWriter"/> against real temp directories
/// under the test output (never the repo tree or the user's real profile)
/// with a manually-advanced <see cref="TimeProvider"/> so day-roll and
/// retention can be proven without waiting for a real midnight.
/// </summary>
public class DiagnosticLogWriterTests
{
    private static readonly PathRedactor NoOpRedactor = new(Array.Empty<(string, string)>());

    /// <summary>
    /// A TimeProvider whose UtcNow is set directly by the test, so a day
    /// boundary can be crossed on demand instead of by waiting for one.
    /// </summary>
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
    public void Write_FormatsLine_AsIsoTimestamp_Level_Category_Message_Detail()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 15, 30, 49, TimeSpan.Zero) };
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, NoOpRedactor);

        var evt = new DiagnosticEvent(
            new DateTimeOffset(2026, 9, 5, 15, 30, 49, TimeSpan.Zero),
            DiagnosticLevel.Error,
            "Injection",
            "SendInput returned 0",
            "GetLastError=1400");

        writer.Write(evt);

        var lines = File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log"));
        Assert.Single(lines);
        Assert.Equal(
            "2026-09-05T15:30:49.0000000Z ERROR Injection | SendInput returned 0 | GetLastError=1400",
            lines[0]);
    }

    [Fact]
    public void Write_NoDetail_OmitsDetailSegment()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) };
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, NoOpRedactor);

        writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", "Started"));

        var lines = File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log"));
        Assert.Single(lines);
        Assert.Equal("2026-09-05T00:00:00.0000000Z INFO  Server | Started", lines[0]);
    }

    [Fact]
    public void Write_MessageContainsCrLf_ProducesExactlyOneLine()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) };
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, NoOpRedactor);

        writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Error, "Macro", "line one\r\nline two", "detail one\ndetail two"));
        writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Macro", "second event"));

        var lines = File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log"));

        Assert.Equal(2, lines.Length);
        Assert.Contains("line one\\nline two", lines[0]);
        Assert.Contains("detail one\\ndetail two", lines[0]);
        Assert.DoesNotContain("\r", lines[0]);
        Assert.Equal("second event", lines[1].Split(" | ")[1]);
    }

    [Fact]
    public void Write_PathsInMessageAndDetail_AreRedacted()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) };
        var redactor = new PathRedactor(new[] { (@"C:\Users\Owner\AppData\Local", "%LOCALAPPDATA%") });
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, redactor);

        writer.Write(new DiagnosticEvent(
            clock.UtcNow,
            DiagnosticLevel.Info,
            "Theme",
            @"Loaded C:\Users\Owner\AppData\Local\LunaPanel\theme.json",
            @"Backup at C:\Users\Owner\AppData\Local\LunaPanel\theme.json.bak"));

        var line = File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log")).Single();

        Assert.DoesNotContain("Owner", line);
        Assert.Contains(@"%LOCALAPPDATA%\LunaPanel\theme.json", line);
        Assert.Contains(@"%LOCALAPPDATA%\LunaPanel\theme.json.bak", line);
    }

    [Fact]
    public void Write_AcrossUtcDayBoundary_ProducesTwoDistinctFilesWithCorrectContent()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 23, 59, 0, TimeSpan.Zero) };
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, NoOpRedactor);

        writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", "before midnight"));

        clock.UtcNow = new DateTimeOffset(2026, 9, 6, 0, 1, 0, TimeSpan.Zero);
        writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", "after midnight"));

        var day1File = Path.Combine(dir, "lunapanel-20260905.log");
        var day2File = Path.Combine(dir, "lunapanel-20260906.log");

        Assert.True(File.Exists(day1File));
        Assert.True(File.Exists(day2File));
        Assert.Contains("before midnight", File.ReadAllText(day1File));
        Assert.Contains("after midnight", File.ReadAllText(day2File));
        Assert.DoesNotContain("after midnight", File.ReadAllText(day1File));
        Assert.DoesNotContain("before midnight", File.ReadAllText(day2File));
    }

    [Fact]
    public void Write_RetentionWindow_DeletesOnlyFilesOlderThanWindow()
    {
        var dir = NewTempDir();
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider { UtcNow = start };
        // retentionDays = 3 keeps today plus the two previous days.
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 3, NoOpRedactor);

        for (var day = 0; day < 5; day++)
        {
            clock.UtcNow = start.AddDays(day);
            writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", $"day {day}"));
        }

        var remaining = Directory.GetFiles(dir, "lunapanel-*.log")
            .Select(Path.GetFileName)
            .OrderBy(f => f)
            .ToArray();

        Assert.Equal(
            new[] { "lunapanel-20260903.log", "lunapanel-20260904.log", "lunapanel-20260905.log" },
            remaining);
    }

    [Fact]
    public void Write_ConcurrentWrites_ProduceExactlyNTimesMWellFormedLinesWithNoneInterleaved()
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero) };
        var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 30, NoOpRedactor);

        const int threadCount = 8;
        const int writesPerThread = 200;

        Parallel.For(0, threadCount, t =>
        {
            for (var i = 0; i < writesPerThread; i++)
            {
                writer.Write(new DiagnosticEvent(
                    clock.UtcNow,
                    DiagnosticLevel.Debug,
                    "Test",
                    $"thread={t} index={i}",
                    new string('x', 50)));
            }
        });

        var lines = File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log"));

        Assert.Equal(threadCount * writesPerThread, lines.Length);

        var seen = new HashSet<(int Thread, int Index)>();
        foreach (var line in lines)
        {
            // A well-formed line has exactly one " | " message segment
            // followed by exactly one " | " detail segment, and the detail
            // segment must be the expected 50 'x' characters - if two writes
            // interleaved mid-line, either this split would fail or the
            // (thread,index) pair would already be in `seen`.
            var parts = line.Split(" | ");
            Assert.Equal(3, parts.Length);
            Assert.Equal(new string('x', 50), parts[2]);

            var messagePart = parts[1]; // "thread=T index=I"
            var tokens = messagePart.Split(' ');
            var thread = int.Parse(tokens[0].Split('=')[1]);
            var index = int.Parse(tokens[1].Split('=')[1]);

            Assert.True(seen.Add((thread, index)), $"Duplicate or corrupted line for thread={thread} index={index}");
        }
    }
}
