namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// Convenience call shapes over <see cref="IDiagnosticLog.Write"/>, kept out
/// of the interface itself. These stamp the event with the wall clock
/// directly rather than an injected clock, because they exist for call-site
/// brevity in production code, not for anything a test needs to control -
/// tests that need a specific timestamp construct a <see cref="DiagnosticEvent"/>
/// directly and call <see cref="IDiagnosticLog.Write"/>.
/// </summary>
public static class DiagnosticLogExtensions
{
    public static void Debug(this IDiagnosticLog log, string category, string message, string? detail = null) =>
        log.Write(new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Debug, category, message, detail));

    public static void Info(this IDiagnosticLog log, string category, string message, string? detail = null) =>
        log.Write(new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Info, category, message, detail));

    public static void Warn(this IDiagnosticLog log, string category, string message, string? detail = null) =>
        log.Write(new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Warn, category, message, detail));

    public static void Error(this IDiagnosticLog log, string category, string message, string? detail = null) =>
        log.Write(new DiagnosticEvent(DateTimeOffset.UtcNow, DiagnosticLevel.Error, category, message, detail));
}
