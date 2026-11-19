using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Tests.Diagnostics;

/// <summary>
/// Log file names are Gregorian <c>yyyyMMdd</c> whatever the machine's
/// culture. The writer formats the name and then parses it back (purge) with
/// the invariant culture; if the format side followed the current culture, an
/// ar-SA (Hijri) or th-TH (Buddhist) machine would write
/// <c>lunapanel-14480503.log</c> / a year-2569 name, and purge would read
/// today's file as ancient and delete it.
/// </summary>
public class DiagnosticLogWriterCultureTests
{
    private static readonly PathRedactor NoOpRedactor = new(Array.Empty<(string, string)>());

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-temp", "diagnostics-culture", testName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static TheoryData<string> Cultures() => new(CultureScope.NonGregorianCultures);

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Write_UnderNonGregorianCulture_NamesTheFileWithTheGregorianDate(string culture)
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 15, 30, 49, TimeSpan.Zero) };

        CultureScope.Run(culture, () =>
        {
            new DiagnosticLogWriter(dir, clock, retentionDays: 30, NoOpRedactor)
                .Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", "Started"));
        });

        var names = Directory.GetFiles(dir).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "lunapanel-20260905.log" }, names);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Purge_UnderNonGregorianCulture_KeepsTodayAndRemovesAGenuinelyOldFile(string culture)
    {
        var dir = NewTempDir();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 5, 15, 30, 49, TimeSpan.Zero) };
        File.WriteAllText(Path.Combine(dir, "lunapanel-20260801.log"), "old\n");
        File.WriteAllText(Path.Combine(dir, "lunapanel-20260904.log"), "yesterday\n");

        CultureScope.Run(culture, () =>
        {
            var writer = new DiagnosticLogWriter(dir, clock, retentionDays: 3, NoOpRedactor);
            writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", "first"));
            // A second write forces another purge pass over today's file.
            writer.Write(new DiagnosticEvent(clock.UtcNow, DiagnosticLevel.Info, "Server", "second"));
        });

        var names = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "lunapanel-20260904.log", "lunapanel-20260905.log" }, names);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(dir, "lunapanel-20260905.log")).Length);
    }
}
