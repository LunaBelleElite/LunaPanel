namespace LunaPanel.Server.Input;

/// <summary>
/// What happened to one injection attempt (one <c>KeyDown</c> or
/// <c>KeyUp</c> call), as decided by <see cref="InjectionGuard"/> before
/// <see cref="Win32KeyInjector"/> ever calls <c>SendInput</c>.
///
/// Deliberately does NOT include an "unbound" member. An action naming no
/// keyboard chord at all is rejected by
/// <c>LunaPanel.Core.Bindings.BindResolver.Resolve</c> before a
/// <see cref="Core.Input.ScancodeInfo"/> exists to inject in the first
/// place - <see cref="Win32KeyInjector"/> is only ever called with a key
/// that already resolved, so it structurally cannot observe "unbound". See
/// <c>ref/docs/injection.md</c> for where that guarantee is actually pinned
/// (against <c>MacroRunner</c> + <c>BindResolver</c>, the real callers).
/// </summary>
public enum InjectionOutcome
{
    /// <summary>The guard passed; <c>SendInput</c> was called and reported success.</summary>
    Sent,

    /// <summary>Elite Dangerous is not the foreground window - refused before any syscall.</summary>
    GameNotForeground,

    /// <summary>
    /// Elite Dangerous is foreground, but LunaPanel's integrity level is
    /// lower than Elite's - Windows' User Interface Privilege Isolation
    /// (UIPI) would silently drop the keystroke (SendInput still reports
    /// success), so injection is refused before that happens. See
    /// <c>ref/docs/injection.md</c>'s "UIPI" section.
    /// </summary>
    UipiSuspected,
}
