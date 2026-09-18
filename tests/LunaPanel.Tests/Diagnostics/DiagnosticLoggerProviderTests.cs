using LunaPanel.Core.Diagnostics;
using LunaPanel.Server.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LunaPanel.Tests.Diagnostics;

public class DiagnosticLoggerProviderTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingLog : IDiagnosticLog
    {
        public List<DiagnosticEvent> Events { get; } = new();

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    [Fact]
    public void Log_AtOrAboveMinimumLevel_ReachesTheDiagnosticSink()
    {
        var log = new RecordingLog();
        var clock = new ManualTimeProvider { UtcNow = new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero) };
        var provider = new DiagnosticLoggerProvider(log, clock, LogLevel.Warning);
        var logger = provider.CreateLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware");

        logger.Log(LogLevel.Warning, new EventId(0), "something ASP.NET noticed", null, (state, _) => state);

        var written = Assert.Single(log.Events);
        Assert.Equal(clock.UtcNow, written.Timestamp);
        Assert.Equal(DiagnosticLevel.Warn, written.Level);
        Assert.Equal("Microsoft.AspNetCore.Routing.EndpointMiddleware", written.Category);
        Assert.Equal("something ASP.NET noticed", written.Message);
        Assert.Null(written.Detail);
    }

    [Fact]
    public void Log_BelowMinimumLevel_NeverReachesTheDiagnosticSink()
    {
        var log = new RecordingLog();
        var clock = new ManualTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var provider = new DiagnosticLoggerProvider(log, clock, LogLevel.Warning);
        var logger = provider.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");

        logger.Log(LogLevel.Information, new EventId(0), "request handled", null, (state, _) => state);

        Assert.Empty(log.Events);
    }

    [Fact]
    public void IsEnabled_ReflectsTheConfiguredMinimumLevel()
    {
        var provider = new DiagnosticLoggerProvider(new RecordingLog(), TimeProvider.System, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.False(logger.IsEnabled(LogLevel.None));
    }

    [Theory]
    [InlineData(LogLevel.Trace, DiagnosticLevel.Debug)]
    [InlineData(LogLevel.Debug, DiagnosticLevel.Debug)]
    [InlineData(LogLevel.Information, DiagnosticLevel.Info)]
    [InlineData(LogLevel.Warning, DiagnosticLevel.Warn)]
    [InlineData(LogLevel.Error, DiagnosticLevel.Error)]
    [InlineData(LogLevel.Critical, DiagnosticLevel.Error)]
    public void Log_MapsEachLogLevelToTheExpectedDiagnosticLevel(LogLevel logLevel, DiagnosticLevel expected)
    {
        var log = new RecordingLog();
        // Trace/Debug/Information sit below the default Warning floor, so a
        // minimum of Trace is used here specifically to isolate the mapping
        // from the filtering this class also does - see the two tests above
        // for filtering itself.
        var provider = new DiagnosticLoggerProvider(log, TimeProvider.System, LogLevel.Trace);
        var logger = provider.CreateLogger("Test");

        logger.Log(logLevel, new EventId(0), "message", null, (state, _) => state);

        var written = Assert.Single(log.Events);
        Assert.Equal(expected, written.Level);
    }

    [Fact]
    public void Log_WithException_CarriesExceptionTextAsDetail()
    {
        var log = new RecordingLog();
        var provider = new DiagnosticLoggerProvider(log, TimeProvider.System, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");
        var exception = new InvalidOperationException("boom");

        logger.Log(LogLevel.Error, new EventId(0), "failed", exception, (state, ex) => $"{state}: {ex!.Message}");

        var written = Assert.Single(log.Events);
        Assert.Equal("failed: boom", written.Message);
        Assert.Contains("boom", written.Detail);
    }

    [Fact]
    public void BeginScope_ReturnsNullAndDoesNotThrow()
    {
        var provider = new DiagnosticLoggerProvider(new RecordingLog(), TimeProvider.System, LogLevel.Warning);
        var logger = provider.CreateLogger("Test");

        var scope = logger.BeginScope("some state");

        Assert.Null(scope);
    }
}
