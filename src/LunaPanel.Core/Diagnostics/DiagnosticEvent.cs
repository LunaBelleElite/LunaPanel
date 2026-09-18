namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// One diagnostic occurrence. Callers use <see cref="Category"/> values such
/// as <c>Injection</c>, <c>Macro</c>, <c>Discovery</c>, <c>Binds</c>,
/// <c>Theme</c>, <c>Status</c>, <c>Server</c> - kept as a plain string rather
/// than an enum so a new subsystem never needs a change here to log.
/// </summary>
/// <param name="Timestamp">When the event occurred. Callers supply this; it is never discovered by this type.</param>
/// <param name="Level">Severity.</param>
/// <param name="Category">Subsystem the event came from.</param>
/// <param name="Message">Human-readable summary. May not be multi-line by the time it reaches a sink - see <see cref="DiagnosticLogWriter"/>.</param>
/// <param name="Detail">Optional extra detail (e.g. an exception message or a Win32 error code).</param>
public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    DiagnosticLevel Level,
    string Category,
    string Message,
    string? Detail = null);
