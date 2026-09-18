namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// Fixed-capacity, thread-safe, in-memory ring of the most recent
/// <see cref="DiagnosticEvent"/>s. This is what a diagnostics view reads -
/// <see cref="Snapshot"/> never touches disk, so reading it is always cheap.
/// Oldest event is evicted once the ring is at capacity.
/// </summary>
public sealed class DiagnosticRingBuffer : IDiagnosticLog
{
    private readonly DiagnosticEvent?[] _buffer;
    private readonly int _capacity;
    private readonly object _lock = new();
    private int _start;
    private int _count;

    public DiagnosticRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        _capacity = capacity;
        _buffer = new DiagnosticEvent?[capacity];
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        lock (_lock)
        {
            var index = (_start + _count) % _capacity;
            if (_count < _capacity)
            {
                _buffer[index] = diagnosticEvent;
                _count++;
            }
            else
            {
                _buffer[_start] = diagnosticEvent;
                _start = (_start + 1) % _capacity;
            }
        }
    }

    /// <summary>Oldest-first snapshot of currently-held events.</summary>
    public IReadOnlyList<DiagnosticEvent> Snapshot()
    {
        lock (_lock)
        {
            var result = new DiagnosticEvent[_count];
            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(_start + i) % _capacity]!;
            }

            return result;
        }
    }
}
