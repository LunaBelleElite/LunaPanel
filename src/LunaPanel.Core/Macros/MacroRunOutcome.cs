namespace LunaPanel.Core.Macros;

/// <summary>Overall outcome of one <see cref="MacroRunner.RunAsync"/> call.</summary>
public enum MacroRunOutcome
{
    /// <summary>Every step succeeded.</summary>
    Success,

    /// <summary>A step failed; the macro stopped there and ran no further steps.</summary>
    Aborted,

    /// <summary>
    /// Refused outright by the injection lock on <see cref="MacroRunner"/> -
    /// never queued: a DIFFERENT macro is mid-keystroke and the keyboard is
    /// not free (<see cref="MacroRunResult.InjectionBusy"/>). A different
    /// macro id is free to run while the first one is merely waiting, and
    /// the SAME macro id no longer refuses at all - it cancels (see
    /// <see cref="Cancelled"/>). See <c>MacroRunner</c>'s own remarks and
    /// <c>ref/docs/macros.md</c>.
    /// </summary>
    Busy,

    /// <summary>
    /// The commander pressed this macro's own button again while it was
    /// running, and the run stopped where it got to (the ruling of
    /// 2026-09-09). Distinct from <see cref="Aborted"/> on purpose: nothing
    /// failed, and distinct from <see cref="Busy"/> because nothing was
    /// refused. Both the stopped run and the second press that stopped it
    /// report this - see <see cref="MacroRunResult.Cancelled"/>.
    /// </summary>
    Cancelled
}
