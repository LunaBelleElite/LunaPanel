namespace LunaPanel.Server.Input;

/// <summary>
/// Windows Mandatory Integrity Control (MIC) levels, ordered low to high so
/// <c>&lt;</c>/<c>&gt;</c> comparisons mean what they look like they mean.
/// Values mirror the well-known <c>SECURITY_MANDATORY_*_RID</c> constants'
/// relative ordering (not their numeric RID values themselves - those are
/// mapped in <see cref="Win32ForegroundInspector"/>, the one place that
/// reads a real token).
/// </summary>
public enum IntegrityLevel
{
    Untrusted = 0,
    Low = 1,
    Medium = 2,
    MediumPlus = 3,
    High = 4,
    System = 5,
    Protected = 6,
}
