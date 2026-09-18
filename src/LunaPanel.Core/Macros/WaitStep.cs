namespace LunaPanel.Core.Macros;

/// <summary>
/// Blind step: <c>{ "wait": ms }</c>. A fixed delay, driven by the runner's
/// injected <see cref="TimeProvider"/> - never a real sleep in a test.
/// </summary>
/// <param name="Duration">How long to wait. Never negative - validated at load.</param>
public sealed record WaitStep(TimeSpan Duration) : MacroStep;
