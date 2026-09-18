namespace LunaPanel.Core.Diagnostics;

/// <summary>
/// A sink for <see cref="DiagnosticEvent"/>s. Kept to a single method
/// deliberately - see <see cref="DiagnosticLogExtensions"/> for convenience
/// call shapes, which are extension methods rather than interface members
/// so every implementation only ever has one thing to get right.
/// </summary>
public interface IDiagnosticLog
{
    void Write(DiagnosticEvent diagnosticEvent);
}
