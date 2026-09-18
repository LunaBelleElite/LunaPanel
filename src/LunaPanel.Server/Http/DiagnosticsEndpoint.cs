using LunaPanel.Core.Diagnostics;

namespace LunaPanel.Server.Http;

/// <summary>
/// <c>GET /api/diagnostics</c>'s handler logic - the "explains a dead button
/// without walking to the PC" surface (see this task's brief). Reads
/// straight off <see cref="DiagnosticRingBuffer.Snapshot"/>, oldest-first,
/// same as the buffer itself.
///
/// <see cref="DiagnosticRingBuffer"/> holds raw, unredacted events - the
/// redaction <see cref="DiagnosticLogWriter"/> applies happens at its own
/// format-time, never mutating the shared <see cref="DiagnosticEvent"/> the
/// ring buffer also received. This endpoint is a second place the same raw
/// event reaches an external reader (the paired tablet, over the LAN), so it
/// redacts independently here, with the same <see cref="PathRedactor"/> the
/// log file uses - not because the ring buffer is wrong to hold raw events
/// (something in-process might legitimately want them), but because nothing
/// that leaves the process should ever carry an unredacted path, and this is
/// the second and only other place that happens.
/// </summary>
public static class DiagnosticsEndpoint
{
    public sealed record EventDto(string Timestamp, string Level, string Category, string Message, string? Detail);

    public static IReadOnlyList<EventDto> BuildResponse(IReadOnlyList<DiagnosticEvent> events, PathRedactor redactor) =>
        events
            .Select(e => new EventDto(
                e.Timestamp.ToString("O"),
                e.Level.ToString(),
                e.Category,
                redactor.Redact(e.Message),
                e.Detail is null ? null : redactor.Redact(e.Detail)))
            .ToList();
}
