namespace LunaPanel.Server.Input;

/// <summary>
/// One injection decision: what happened, and a reason phrased for a
/// player, not a developer - this is the string a UI would show next to a
/// dead button.
/// </summary>
public sealed record InjectionAttemptResult(InjectionOutcome Outcome, string Reason);
