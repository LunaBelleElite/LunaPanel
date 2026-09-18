namespace LunaPanel.Server.Input;

/// <summary>
/// Everything <see cref="InjectionGuard"/> needs to decide whether an
/// injection attempt should proceed, captured as one immutable snapshot so
/// the decision itself never touches Win32 - a test builds this by hand,
/// production builds it from <see cref="Win32ForegroundInspector.Capture"/>.
/// Same shape-of-reasoning as <c>LunaPanel.Server.Discovery.PathDiscoveryEnvironment</c>:
/// bundle the environment-dependent inputs so the logic that consumes them
/// stays a pure function.
/// </summary>
/// <param name="ForegroundProcessName">
/// The foreground window's owning process name (no ".exe", matching
/// <see cref="System.Diagnostics.Process.ProcessName"/>), or <c>null</c> if
/// it could not be determined at all (no foreground window, or the process
/// could not be opened).
/// </param>
/// <param name="ForegroundIntegrityLevel">
/// The foreground process's integrity level, or <c>null</c> if it could not
/// be determined (e.g. <c>OpenProcessToken</c> denied). <c>null</c> is
/// treated as "the check could not run", never as a mismatch - see
/// <see cref="InjectionGuard"/>.
/// </param>
/// <param name="OurIntegrityLevel">
/// LunaPanel's own process integrity level, or <c>null</c> if it could not
/// be determined.
/// </param>
public sealed record ForegroundContext(
    string? ForegroundProcessName,
    IntegrityLevel? ForegroundIntegrityLevel,
    IntegrityLevel? OurIntegrityLevel);
