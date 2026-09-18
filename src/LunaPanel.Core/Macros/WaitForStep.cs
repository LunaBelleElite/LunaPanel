using LunaPanel.Core.GameState;

namespace LunaPanel.Core.Macros;

/// <summary>
/// Gated step: <c>{ "waitFor": [conditions], "timeoutMs": t }</c>. Polls
/// (via <c>GameStateStore.WaitForAsync</c>) until the conditions are
/// satisfied or <see cref="Timeout"/> elapses, in which case the macro
/// aborts naming this step.
/// </summary>
/// <param name="Conditions">The parsed condition list, ANDed.</param>
/// <param name="ConditionTokens">The original condition tokens - see <see cref="RequireStep.ConditionTokens"/> for why these are kept separately.</param>
/// <param name="Timeout">How long to wait before giving up. Always positive - validated at load.</param>
public sealed record WaitForStep(ConditionList Conditions, IReadOnlyList<string> ConditionTokens, TimeSpan Timeout) : MacroStep;
