using System.Globalization;

namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// Writes <see cref="DiagnosticEvent"/>s to <c>lunapanel-YYYYMMDD.log</c>
/// files under an injected directory, rolling on the UTC day boundary and
/// deleting files older than the retention window. Never discovers the
/// directory itself - it is handed one, exactly like the clock.
///
/// Line format (pinned by
/// <c>DiagnosticLogWriterTests.WriteFormatsLine_AsIsoTimestamp_Level_Category_Message</c>):
/// <code>
/// {timestamp:yyyy-MM-ddTHH:mm:ss.fffffffZ} {LEVEL,-5} {category} | {message}[ | {detail}]
/// </code>
/// e.g. <c>2026-09-05T15:30:49.1234567Z ERROR Injection | SendInput returned 0 | GetLastError=1400</c>
///
/// One event is always exactly one line: any CR or LF inside the message or
/// detail is replaced with the literal two-character sequence <c>\n</c>
/// before the line is written, so a multi-line exception message cannot
/// split a record across lines.
/// </summary>
public sealed class DiagnosticLogWriter : IDiagnosticLog
{
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly int _retentionDays;
    private readonly PathRedactor _redactor;
    private readonly object _lock = new();

    public DiagnosticLogWriter(string directory, TimeProvider clock, int retentionDays, PathRedactor redactor)
    {
        _directory = directory;
        _clock = clock;
        _retentionDays = retentionDays;
        _redactor = redactor;
        Directory.CreateDirectory(_directory);
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        var line = FormatLine(diagnosticEvent, _redactor);

        lock (_lock)
        {
            var now = _clock.GetUtcNow();
            var path = LogPathFor(now);
            File.AppendAllText(path, line + "\n");
            PurgeOldFiles(now);
        }
    }

    private string LogPathFor(DateTimeOffset utcNow) =>
        Path.Combine(_directory, $"lunapanel-{utcNow.UtcDateTime:yyyyMMdd}.log");

    private static string FormatLine(DiagnosticEvent diagnosticEvent, PathRedactor redactor)
    {
        var timestamp = diagnosticEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        var level = diagnosticEvent.Level switch
        {
            DiagnosticLevel.Debug => "DEBUG",
            DiagnosticLevel.Info => "INFO ",
            DiagnosticLevel.Warn => "WARN ",
            DiagnosticLevel.Error => "ERROR",
            _ => diagnosticEvent.Level.ToString().ToUpperInvariant()
        };

        var message = EscapeForLine(redactor.Redact(diagnosticEvent.Message));
        var line = $"{timestamp} {level} {diagnosticEvent.Category} | {message}";

        if (diagnosticEvent.Detail is not null)
        {
            var detail = EscapeForLine(redactor.Redact(diagnosticEvent.Detail));
            line += $" | {detail}";
        }

        return line;
    }

    private static string EscapeForLine(string value) =>
        value.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");

    private void PurgeOldFiles(DateTimeOffset utcNow)
    {
        // retentionDays counts the current day, so retentionDays = 3 keeps
        // today plus the two previous days (three files total) and deletes
        // anything older.
        var cutoff = utcNow.UtcDateTime.Date.AddDays(-(_retentionDays - 1));

        foreach (var file in Directory.EnumerateFiles(_directory, "lunapanel-????????.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var datePart = name["lunapanel-".Length..];
            if (DateTime.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate)
                && fileDate < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
