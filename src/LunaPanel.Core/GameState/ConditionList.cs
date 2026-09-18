namespace LunaPanel.Core.GameState;

/// <summary>
/// A list of <see cref="Condition"/>s, ANDed together. This is the only
/// form of composition the condition grammar supports - no OR, no nesting.
/// </summary>
public sealed class ConditionList
{
    public IReadOnlyList<Condition> Conditions { get; }

    private ConditionList(IReadOnlyList<Condition> conditions)
    {
        Conditions = conditions;
    }

    /// <exception cref="FormatException">
    /// Any token fails to parse - see <see cref="Condition.Parse"/>.
    /// </exception>
    public static ConditionList Parse(IEnumerable<string> tokens) =>
        new(tokens.Select(Condition.Parse).ToArray());

    public bool Evaluate(StatusSnapshot snapshot) => Conditions.All(c => c.Evaluate(snapshot));
}
