using LunaPanel.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LunaPanel.Server.Diagnostics;

/// <summary>
/// Bridges ASP.NET Core's own <see cref="Microsoft.Extensions.Logging"/>
/// output (Kestrel, routing, and anything else the framework logs through
/// <c>ILogger&lt;T&gt;</c>) into the same <see cref="IDiagnosticLog"/> sink
/// LunaPanel's own code writes through - see <c>ref/docs/diagnostics.md</c>'s
/// "Why Core has no ILogger" for why this bridge lives here in
/// <c>LunaPanel.Server</c> rather than in Core: Core cannot take a dependency
/// on <c>Microsoft.Extensions.Logging.Abstractions</c> without a network
/// restore, while <c>LunaPanel.Server</c> already carries it via the
/// <c>Microsoft.NET.Sdk.Web</c> shared framework.
///
/// Defaults to forwarding only <see cref="LogLevel.Warning"/> and above:
/// ASP.NET's own default logging is extremely chatty at Information (every
/// request, every routing decision), and this project's own diagnostics log
/// is meant to be pasted into a bug report, not to duplicate request-by-
/// request access logging nobody asked for.
/// </summary>
public sealed class DiagnosticLoggerProvider : ILoggerProvider
{
    private readonly IDiagnosticLog _log;
    private readonly TimeProvider _clock;
    private readonly LogLevel _minimumLevel;

    public DiagnosticLoggerProvider(IDiagnosticLog log, TimeProvider clock, LogLevel minimumLevel = LogLevel.Warning)
    {
        _log = log;
        _clock = clock;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(_log, _clock, categoryName, _minimumLevel);

    public void Dispose()
    {
    }

    private sealed class DiagnosticLogger : ILogger
    {
        private readonly IDiagnosticLog _log;
        private readonly TimeProvider _clock;
        private readonly string _categoryName;
        private readonly LogLevel _minimumLevel;

        public DiagnosticLogger(IDiagnosticLog log, TimeProvider clock, string categoryName, LogLevel minimumLevel)
        {
            _log = log;
            _clock = clock;
            _categoryName = categoryName;
            _minimumLevel = minimumLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
            {
                return;
            }

            _log.Write(new DiagnosticEvent(_clock.GetUtcNow(), MapLevel(logLevel), _categoryName, message, exception?.ToString()));
        }

        private static DiagnosticLevel MapLevel(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => DiagnosticLevel.Debug,
            LogLevel.Information => DiagnosticLevel.Info,
            LogLevel.Warning => DiagnosticLevel.Warn,
            LogLevel.Error or LogLevel.Critical => DiagnosticLevel.Error,
            _ => DiagnosticLevel.Info
        };
    }
}
