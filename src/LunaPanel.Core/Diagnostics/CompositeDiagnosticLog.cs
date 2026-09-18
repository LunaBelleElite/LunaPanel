namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// Fans one <see cref="DiagnosticEvent"/> out to both the durable file
/// writer and the in-memory ring the diagnostics view reads. This is the
/// <see cref="IDiagnosticLog"/> production code should hold - it exposes
/// both underlying components so a caller can still read
/// <see cref="RingBuffer"/>.<see cref="DiagnosticRingBuffer.Snapshot"/>
/// without going through the interface.
/// </summary>
public sealed class CompositeDiagnosticLog : IDiagnosticLog
{
    public DiagnosticLogWriter Writer { get; }

    public DiagnosticRingBuffer RingBuffer { get; }

    public CompositeDiagnosticLog(DiagnosticLogWriter writer, DiagnosticRingBuffer ringBuffer)
    {
        Writer = writer;
        RingBuffer = ringBuffer;
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        Writer.Write(diagnosticEvent);
        RingBuffer.Write(diagnosticEvent);
    }
}
