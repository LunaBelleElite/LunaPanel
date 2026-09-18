namespace LunaPanel.Server.Input;

/// <summary>
/// The decision - never the syscall. Given a <see cref="ForegroundContext"/>
/// snapshot, decides whether an injection attempt should proceed, with no
/// P/Invoke and no OS access of its own - fully testable by handing it a
/// hand-built <see cref="ForegroundContext"/>. <see cref="Win32KeyInjector"/>
/// is the only production caller.
/// </summary>
public static class InjectionGuard
{
    /// <summary>
    /// Process names (no ".exe") of Elite Dangerous's two shipped
    /// executables. Mirrors the file names
    /// <c>LunaPanel.Server.Discovery.EliteProductScanner</c> looks for on
    /// disk (<c>EliteDangerous64.exe</c> / <c>EliteDangerous32.exe</c>) -
    /// <see cref="System.Diagnostics.Process.ProcessName"/> never carries
    /// the extension, so it is dropped here rather than restated with it.
    /// </summary>
    public static readonly IReadOnlySet<string> EliteProcessNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EliteDangerous64", "EliteDangerous32" };

    public static InjectionAttemptResult Evaluate(ForegroundContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ForegroundProcessName is null || !EliteProcessNames.Contains(context.ForegroundProcessName))
        {
            return new InjectionAttemptResult(
                InjectionOutcome.GameNotForeground,
                "Elite Dangerous is not the active window. Click on the game, then try again.");
        }

        // Integrity levels are only compared when BOTH sides were actually
        // determined. A null on either side means the check itself failed
        // (e.g. OpenProcessToken was denied) - that is "unknown", not "a
        // mismatch was found", so it deliberately does not block injection.
        // See ref/docs/injection.md's "UIPI" section for why this matters:
        // a SendInput blocked by UIPI reports success and sets no error, so
        // this comparison is the only way to catch it before it happens.
        if (context.OurIntegrityLevel is { } ours &&
            context.ForegroundIntegrityLevel is { } theirs &&
            ours < theirs)
        {
            return new InjectionAttemptResult(
                InjectionOutcome.UipiSuspected,
                "Elite Dangerous is running at a higher privilege level than LunaPanel, so Windows will silently discard the keystroke (UIPI). Run both at the same privilege level - either both elevated, or neither.");
        }

        return new InjectionAttemptResult(InjectionOutcome.Sent, "Sent.");
    }
}
